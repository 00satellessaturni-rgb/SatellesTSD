using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SatellesTSD.Infrastructure.Updates;

/// <summary>Where the program stands against the release folder.</summary>
public enum UpdateState
{
    /// <summary>No check was made (no folder, or the program is not the published exe).</summary>
    Off,
    UpToDate,
    Available,
    /// <summary>The folder is there but holds no latest.txt yet.</summary>
    NoRelease,
    /// <summary>The folder or latest.txt cannot be read.</summary>
    Unreachable,
    /// <summary>latest.txt cannot be used.</summary>
    BadRelease,
    /// <summary>A fault in the check itself.</summary>
    Failed,
}

/// <summary>Why a copy of the program is not updated in place.</summary>
public enum InstallBlock
{
    None,
    /// <summary>Run from a Visual Studio build folder, or under a debugger: rebuilt from the sources, not updated.</summary>
    BuildFolder,
    /// <summary>Run from a network drive: shared by others, replaced by hand when every window is closed.</summary>
    NetworkDrive,
}

/// <summary>A release as latest.txt describes it: the zip the publish script made, with its size and SHA-256, and the
/// exe's own size and SHA-256 inside it when the script wrote them.</summary>
public sealed record ReleaseManifest(Version Version, string FileName, long Size, string Sha256, string? Published, string? Commit, long? ExeSize = null, string? ExeSha256 = null)
{
    /// <summary>One build: the version and the checksum — a fixed build published again under the same version after a
    /// withdrawal must not be taken for the one that failed.</summary>
    public string BuildKey => $"{Version} {Sha256}";
}

public sealed record UpdateCheck(UpdateState State, string Folder, ReleaseManifest? Release, string? Detail);

/// <summary>What a program file says about itself (its version resource).</summary>
public sealed record ProgramInfo(string? Product, Version? Version);

/// <summary>An update that could not be made, with a code the window turns into words and the detail for the log.</summary>
public sealed class UpdateException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public const string ReadFailed = "READ_FAILED";
    public const string Incomplete = "INCOMPLETE";
    public const string TooLarge = "TOO_LARGE";
    public const string HashMismatch = "HASH_MISMATCH";
    public const string NotTheProgram = "NOT_THE_PROGRAM";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string SchemasMissing = "SCHEMAS_MISSING";
    public const string NoWriteAccess = "NO_WRITE_ACCESS";
    public const string Busy = "BUSY";
    public const string AnotherWindowUpdating = "ANOTHER_WINDOW_UPDATING";
    public const string SwapFailed = "SWAP_FAILED";
    public const string RevertFailed = "REVERT_FAILED";
    public const string BadZip = "BAD_ZIP";

    public string Code { get; } = code;
}

/// <summary>Updating the program from the release folder on the office NAS. tools\Publish-Release.ps1 puts each
/// release there as SatellesTSD-v&lt;version&gt;.zip (the exe, the schemas folder, release.txt) beside latest.txt, which
/// names it with its size and SHA-256. The program reads latest.txt when it starts; when the release is newer it says
/// so in the title and on its button, and on request copies the zip beside itself, checks it, unpacks it, swaps the
/// exe and the schemas folder in and starts the new program.
/// <para>The swap relies on what Windows allows with a running exe: it cannot be overwritten or deleted, but it can be
/// renamed, so the running file goes aside into a hidden folder beside it and the new file takes its name. The program
/// keeps its folder and its name; the store and the settings live elsewhere and are untouched.</para>
/// <para>The release folder is the trust boundary: whatever is published there runs on every PC that updates from it,
/// so only the owner may write to it. The size and SHA-256 in latest.txt protect against a half-copied or damaged
/// file, not against someone who can write to the folder.</para></summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class Updater
{
    /// <summary>Where every copy looks for releases; S: is the office share, mapped the same way on every PC.
    /// tools\Publish-Release.ps1 reads this line, so both use the same folder.</summary>
    public const string DefaultReleaseFolder = @"S:\2. DOKUMENDID JA FAILID\IT\SatellesTsdUpdate";

    /// <summary>A text file of this name beside the exe names another release folder on its first line: for trying a
    /// release on one PC before publishing it to everyone, or if the NAS moves.</summary>
    public const string FolderOverrideFile = "SatellesTSD.update-folder.txt";

    public const string ManifestName = "latest.txt";
    public const string ExeName = "SatellesTSD.exe";
    public const string ProductName = "SatellesTSD";
    public const string SchemasFolder = "schemas";

    /// <summary>Beside the exe, hidden: the download, the unpacked release, the previous version (kept, one, for going
    /// back by hand) and a note of a build that did not start.</summary>
    public const string StagingFolderName = ".SatellesTSD-update";
    private const string FailedNoteName = "failed.txt";

    /// <summary>An unreachable NAS holds the asking thread for some twenty seconds, and that thread cannot be stopped;
    /// the program stops waiting for it sooner.</summary>
    public const int CheckTimeoutMs = 15000;

    /// <summary>How long the old copy waits for the new one to say it is running before the user is asked whether to
    /// wait longer or go back. Settable so that a test can shorten it.</summary>
    public static int StartTimeoutMs { get; set; } = 60000;

    /// <summary>The new copy is started with this argument and the name of a signal it gives once its window is up and
    /// its own update check has run; a crash before then ends it with <see cref="FailedExitCode"/>.</summary>
    public const string ReadyArgument = "--update-ready";
    public const int FailedExitCode = 3;

    // ---------------------------------------------------------------- the release and the check

    /// <summary>The release folder for the exe: the one named in the override file beside it, else the default.</summary>
    public static string ReleaseFolderFor(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        try
        {
            var note = Path.Combine(Path.GetDirectoryName(exePath)!, FolderOverrideFile);
            if (File.Exists(note))
            {
                foreach (var line in File.ReadAllLines(note, Encoding.UTF8))
                {
                    // the quotes Explorer's "Copy as path" puts around a path are not part of it
                    var folder = line.Trim().Trim('"').Trim();
                    if (folder.Length > 0)
                    {
                        return folder;
                    }
                }
            }
        }
        catch (IOException)
        {
            // an unreadable note is no reason to stop checking: the usual folder applies
        }
        catch (UnauthorizedAccessException)
        {
        }

        return DefaultReleaseFolder;
    }

    /// <summary>"0.10.0" or "v0.10.0" as a version; null when it is not one.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return Regex.IsMatch(trimmed, @"^\d{1,6}(\.\d{1,6}){1,3}$") && Version.TryParse(trimmed, out var version) ? Normal(version) : null;
    }

    /// <summary>A version with every part present (0.10 = 0.10.0.0), so that equal versions compare equal.</summary>
    public static Version Normal(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Version(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    /// <summary>Reads latest.txt: key=value lines, blank lines and # comments ignored. Throws a FormatException for
    /// anything that cannot be relied on.</summary>
    public static ReleaseManifest ParseManifest(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                throw new FormatException($"{ManifestName}: a line without key=value: \"{line}\".");
            }

            // a key given twice would let one reader see another release than the next
            if (!values.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim()))
            {
                throw new FormatException($"{ManifestName}: '{line[..eq].Trim()}' is given twice.");
            }
        }

        var versionText = Required(values, "version");
        var version = ParseVersion(versionText) ?? throw new FormatException($"{ManifestName}: 'version' is not a version.");
        var file = Required(values, "file");
        // the release file is this very name in the release folder itself, never a path elsewhere
        if (!string.Equals(file, ReleaseFileName(versionText), StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"{ManifestName}: 'file' must be {ReleaseFileName(versionText)}.");
        }

        var size = PositiveNumber(values, "size", required: true)!.Value;
        var sha = HexDigest(values, "sha256", required: true)!;
        return new ReleaseManifest(version, file, size, sha, values.GetValueOrDefault("published"), values.GetValueOrDefault("commit"),
            PositiveNumber(values, "exe_size", required: false), HexDigest(values, "exe_sha256", required: false));
    }

    /// <summary>The release zip's name for a version as the publish script names it: "SatellesTSD-v0.11.0.zip".</summary>
    public static string ReleaseFileName(string version) => $"{ProductName}-v{version.TrimStart('v', 'V')}.zip";

    private static long? PositiveNumber(Dictionary<string, string> values, string key, bool required)
    {
        if (!values.TryGetValue(key, out var text) || text.Length == 0)
        {
            return required ? throw new FormatException($"{ManifestName}: '{key}' is missing.") : null;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : throw new FormatException($"{ManifestName}: '{key}' is not a positive number.");
    }

    private static string? HexDigest(Dictionary<string, string> values, string key, bool required)
    {
        if (!values.TryGetValue(key, out var text) || text.Length == 0)
        {
            return required ? throw new FormatException($"{ManifestName}: '{key}' is missing.") : null;
        }

        var sha = text.ToUpperInvariant();
        return Regex.IsMatch(sha, "^[0-9A-F]{64}$") ? sha : throw new FormatException($"{ManifestName}: '{key}' is not 64 hex digits.");
    }

    private static string Required(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && value.Length > 0 ? value : throw new FormatException($"{ManifestName}: '{key}' is missing.");

    /// <summary>Reads the folder's latest.txt and compares it with the running version. Never throws: the state says
    /// what was found, the detail why.</summary>
    public static UpdateCheck Check(string folder, Version current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(current);
        try
        {
            // latest.txt read straight away (one round trip to the share): no file means no release yet, no folder means unreachable
            ReleaseManifest release;
            try
            {
                release = ParseManifest(File.ReadAllText(Path.Combine(folder, ManifestName), Encoding.UTF8));
            }
            catch (FileNotFoundException)
            {
                return new UpdateCheck(UpdateState.NoRelease, folder, null, $"no {ManifestName} in the release folder");
            }
            catch (DirectoryNotFoundException)
            {
                return new UpdateCheck(UpdateState.Unreachable, folder, null, "the release folder cannot be reached");
            }
            catch (FormatException e)
            {
                return new UpdateCheck(UpdateState.BadRelease, folder, null, e.Message);
            }

            return new UpdateCheck(release.Version > Normal(current) ? UpdateState.Available : UpdateState.UpToDate, folder, release, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new UpdateCheck(UpdateState.Unreachable, folder, null, e.Message);
        }
        catch (Exception e)
        {
            return new UpdateCheck(UpdateState.Failed, folder, null, e.Message);
        }
    }

    /// <summary>The check on another thread, given up after the timeout (the thread itself cannot be stopped while a
    /// dead share holds it; it ends by itself later).</summary>
    public static async Task<UpdateCheck> CheckAsync(string folder, Version current, int timeoutMs = CheckTimeoutMs)
    {
        var work = Task.Run(() => Check(folder, current));
        var done = await Task.WhenAny(work, Task.Delay(timeoutMs)).ConfigureAwait(false);
        return done == work ? await work.ConfigureAwait(false) : new UpdateCheck(UpdateState.Unreachable, folder, null, $"the release folder did not answer within {timeoutMs / 1000} s");
    }

    // ---------------------------------------------------------------- the copy's place

    /// <summary>Why the copy at the path is not updated in place: a copy in a build folder is rebuilt from the sources;
    /// one on a network drive is shared by others and replaced by hand when every window is closed.</summary>
    public static InstallBlock InstallBlockOf(string exePath, bool debuggerAttached = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        if (debuggerAttached || exePath.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) || exePath.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase))
        {
            return InstallBlock.BuildFolder;
        }

        var onNetwork = exePath.StartsWith(@"\\", StringComparison.Ordinal);
        if (!onNetwork)
        {
            try
            {
                onNetwork = new DriveInfo(Path.GetPathRoot(exePath)!).DriveType == DriveType.Network;
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
        }

        return onNetwork ? InstallBlock.NetworkDrive : InstallBlock.None;
    }

    public static string StagingFolder(string exePath) => Path.Combine(Path.GetDirectoryName(exePath)!, StagingFolderName);

    /// <summary>Holds the update of this program's folder for one window at a time: a lock file in the hidden folder,
    /// held exclusively until disposed. Null when another window holds it.</summary>
    public static IDisposable? TakeInstallLock(string exePath)
    {
        var staging = PrepareStaging(exePath);
        try
        {
            return new FileStream(Path.Combine(staging, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>What a program file says about itself: FileVersionInfo reads the version resource of a single-file
    /// bundle too, which AssemblyName cannot.</summary>
    public static ProgramInfo ProgramInfoOf(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new ProgramInfo(info.ProductName, ParseVersion(info.FileVersion));
    }

    /// <summary>The version the exe on disk carries — which may differ from the running one's, when another window
    /// has installed a release since — or null when it cannot be read.</summary>
    public static Version? DiskVersion(string exePath)
    {
        try
        {
            return ProgramInfoOf(exePath).Version;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>True when exactly this build (version and checksum) already failed to start here.</summary>
    public static bool FailedHere(string exePath, ReleaseManifest release)
    {
        ArgumentNullException.ThrowIfNull(release);
        try
        {
            var note = Path.Combine(StagingFolder(exePath), FailedNoteName);
            return File.Exists(note) && string.Equals(File.ReadAllText(note, Encoding.UTF8).Trim(), release.BuildKey, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- the download, the unpacking, the checks

    /// <summary>Copies the release zip into the hidden folder beside the exe and checks it: its size and SHA-256
    /// against latest.txt. Copied byte by byte, so no "downloaded from the internet" mark comes with it. Returns the
    /// copied file. Throws <see cref="UpdateException"/> or OperationCanceledException; a partial file is removed.</summary>
    public static string Download(string folder, ReleaseManifest release, string exePath, IProgress<long>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(release);
        var staging = PrepareStaging(exePath);
        var target = Path.Combine(staging, $"download-{Guid.NewGuid():N}.zip");
        var source = Path.Combine(folder, release.FileName);
        try
        {
            FileStream input;
            try
            {
                input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new UpdateException(UpdateException.ReadFailed, $"{source}: {e.Message}", e);
            }

            string digest;
            using (var sha = SHA256.Create())
            {
                try
                {
                    using (input)
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long copied = 0;
                        while (true)
                        {
                            cancel.ThrowIfCancellationRequested();
                            var n = input.Read(buffer, 0, buffer.Length);
                            if (n == 0)
                            {
                                break;
                            }

                            output.Write(buffer, 0, n);
                            sha.TransformBlock(buffer, 0, n, null, 0);
                            copied += n;
                            if (copied > release.Size)
                            {
                                throw new UpdateException(UpdateException.TooLarge, $"the release file is larger than {ManifestName} says ({release.Size} bytes).");
                            }

                            progress?.Report(copied);
                        }

                        if (copied != release.Size)
                        {
                            throw new UpdateException(UpdateException.Incomplete, $"the release file is incomplete ({copied} of {release.Size} bytes) — it may still be copying.");
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new UpdateException(UpdateException.ReadFailed, e.Message, e);
                }

                sha.TransformFinalBlock([], 0, 0);
                digest = Hex(sha.Hash!);
            }

            if (!string.Equals(digest, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(UpdateException.HashMismatch, $"the release file's SHA-256 is not the one {ManifestName} names.");
            }

            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>Unpacks the checked zip into the hidden folder and returns the folder that holds the program (the
    /// zip's root folder, or the zip's root itself).</summary>
    public static string Unpack(string zipPath, string exePath)
    {
        var staging = PrepareStaging(exePath);
        var target = Path.Combine(staging, $"unpacked-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // every entry stays inside its folder: no rooted name, no step up (the extractor refuses those too, but a
                // release that needs refusing is said so by name, not by a stray file in the hidden folder)
                var stray = archive.Entries.FirstOrDefault(e => Path.IsPathRooted(e.FullName) || e.FullName.Split('/', '\\').Contains(".."));
                if (stray is not null)
                {
                    throw new UpdateException(UpdateException.BadZip, $"the release zip holds an entry outside its folder: {stray.FullName}");
                }
            }

            ZipFile.ExtractToDirectory(zipPath, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDeleteFolder(target);
            throw new UpdateException(UpdateException.BadZip, $"the release zip could not be unpacked: {e.Message}", e);
        }

        if (File.Exists(Path.Combine(target, ExeName)))
        {
            return target;
        }

        var inner = Directory.GetDirectories(target).FirstOrDefault(d => File.Exists(Path.Combine(d, ExeName)));
        if (inner is null)
        {
            TryDeleteFolder(target);
            throw new UpdateException(UpdateException.NotTheProgram, $"the release zip holds no {ExeName}.");
        }

        return inner;
    }

    /// <summary>The unpacked folder must hold this program at the version latest.txt names, with the schemas the
    /// XML export validates against.</summary>
    public static void VerifyProgram(string folder, ReleaseManifest release, Func<string, ProgramInfo>? reader = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        var exe = Path.Combine(folder, ExeName);
        if (!File.Exists(exe))
        {
            throw new UpdateException(UpdateException.NotTheProgram, $"the release holds no {ExeName}.");
        }

        ProgramInfo info;
        try
        {
            info = (reader ?? ProgramInfoOf)(exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new UpdateException(UpdateException.NotTheProgram, $"the release's {ExeName} could not be read: {e.Message}", e);
        }

        if (!string.Equals(info.Product, ProductName, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException(UpdateException.NotTheProgram, $"the release's {ExeName} is not {ProductName} (product '{info.Product}').");
        }

        if (info.Version is null || info.Version != release.Version)
        {
            throw new UpdateException(UpdateException.VersionMismatch, $"the release's {ExeName} carries version {info.Version?.ToString() ?? "?"}, not {release.Version} as {ManifestName} says.");
        }

        try
        {
            if (release.ExeSize is { } exeSize && new FileInfo(exe).Length != exeSize)
            {
                throw new UpdateException(UpdateException.HashMismatch, $"the release's {ExeName} is {new FileInfo(exe).Length} bytes, not {exeSize} as {ManifestName} says.");
            }

            if (release.ExeSha256 is { } exeSha)
            {
                using var stream = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!string.Equals(Hex(SHA256.HashData(stream)), exeSha, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateException(UpdateException.HashMismatch, $"the release's {ExeName} SHA-256 is not the one {ManifestName} names.");
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateException.ReadFailed, $"the release's {ExeName} could not be read: {e.Message}", e);
        }

        var schemas = Path.Combine(folder, SchemasFolder, "apa-tsd");
        if (!Directory.Exists(Path.Combine(schemas, "xbrl")) || !Directory.Exists(Path.Combine(schemas, "gl")))
        {
            throw new UpdateException(UpdateException.SchemasMissing, $"the release holds no {SchemasFolder}\\apa-tsd folder with its xbrl and gl parts.");
        }
    }

    // ---------------------------------------------------------------- the swap and the way back

    /// <summary>Puts the unpacked release in place of the program: the running exe and the schemas folder are renamed
    /// into the hidden folder (Windows allows renaming a running exe, not replacing it) and the new ones take their
    /// names. Returns the folder the previous ones went to, for <see cref="Revert"/>. Should a later step fail, the
    /// earlier ones are undone at once.</summary>
    public static string Swap(string exePath, string unpackedFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(unpackedFolder);
        var newExe = Path.Combine(unpackedFolder, ExeName);
        var newSchemas = Path.Combine(unpackedFolder, SchemasFolder);
        // antivirus may still be reading the new exe; it must be free before the running one goes aside, so that the
        // program's name is empty only between two renames
        WaitUntilFree(newExe);
        var programFolder = Path.GetDirectoryName(exePath)!;
        var schemas = Path.Combine(programFolder, SchemasFolder);
        var previous = Path.Combine(StagingFolder(exePath), $"previous-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        var previousExe = Path.Combine(previous, ExeName);
        var previousSchemas = Path.Combine(previous, SchemasFolder);
        var steps = new Stack<Action>();
        try
        {
            Directory.CreateDirectory(previous);
            File.Move(exePath, previousExe);
            steps.Push(() => File.Move(previousExe, exePath));
            File.Move(newExe, exePath);
            steps.Push(() => File.Move(exePath, newExe));
            if (Directory.Exists(schemas))
            {
                Directory.Move(schemas, previousSchemas);
                steps.Push(() => Directory.Move(previousSchemas, schemas));
            }

            Directory.Move(newSchemas, schemas);
            return previous;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            while (steps.Count > 0)
            {
                try
                {
                    steps.Pop()();
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            // an emptied previous-<stamp> folder must not stand as the newest at the next tidying
            try
            {
                if (Directory.Exists(previous) && Directory.GetFileSystemEntries(previous).Length == 0)
                {
                    Directory.Delete(previous);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw new UpdateException(UpdateException.SwapFailed, e.Message, e);
        }
    }

    /// <summary>Puts the previous program back after the new one failed to start, keeps the failed one aside, and notes
    /// that build so it is not offered as if nothing had happened. The new exe may be held for a moment, or already
    /// gone, and neither stops the previous one from going back. The previous one is copied back, not moved, so it
    /// stays in the hidden folder until that is tidied.</summary>
    public static void Revert(string exePath, string previousFolder, ReleaseManifest release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousFolder);
        ArgumentNullException.ThrowIfNull(release);
        var programFolder = Path.GetDirectoryName(exePath)!;
        var schemas = Path.Combine(programFolder, SchemasFolder);
        var failed = Path.Combine(StagingFolder(exePath), $"failed-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        try
        {
            Directory.CreateDirectory(failed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateException.RevertFailed, $"the previous version could not be put back ({e.Message}); it is kept in {previousFolder}.", e);
        }

        Exception? held = null;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                if (File.Exists(exePath))
                {
                    File.Move(exePath, Path.Combine(failed, ExeName));
                }

                held = null;
                break;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                held = e;
                if (attempt < 20)
                {
                    Thread.Sleep(250);
                }
            }
        }

        if (held is not null)
        {
            // the failed exe cannot be moved aside: the program is left whole (the new exe with its schemas) rather than
            // with no schemas at all, and the reason is said
            throw new UpdateException(UpdateException.RevertFailed, $"the previous version could not be put back: the new {ExeName} is held ({held.Message}); it is kept in {previousFolder}.", held);
        }

        try
        {
            if (Directory.Exists(schemas))
            {
                Directory.Move(schemas, Path.Combine(failed, SchemasFolder));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            File.Copy(Path.Combine(previousFolder, ExeName), exePath, overwrite: false);
            var previousSchemas = Path.Combine(previousFolder, SchemasFolder);
            if (Directory.Exists(previousSchemas))
            {
                CopyFolder(previousSchemas, schemas);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateException.RevertFailed, $"the previous version could not be put back ({e.Message}); it is kept in {previousFolder}.", e);
        }

        try
        {
            File.WriteAllText(Path.Combine(StagingFolder(exePath), FailedNoteName), release.BuildKey, Encoding.UTF8);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---------------------------------------------------------------- the start of the new program

    public enum StartOutcome
    {
        /// <summary>The new copy said it is running.</summary>
        Ready,
        /// <summary>It ended before saying so.</summary>
        Exited,
        /// <summary>Still running, silent — waiting at a message on start, perhaps.</summary>
        NoAnswer,
    }

    public sealed record StartResult(StartOutcome Outcome, int ExitCode = 0);

    /// <summary>The new copy, started with the ready signal: kept while its answer is awaited, so that a copy slow to
    /// answer can be waited for again, or stopped and the previous version put back.</summary>
    public sealed class StartedCopy : IDisposable
    {
        internal StartedCopy(Process child, EventWaitHandle ready)
        {
            Child = child;
            Ready = ready;
        }

        public Process Child { get; }

        public EventWaitHandle Ready { get; }

        public bool IsReady => Ready.WaitOne(0);

        /// <summary>Ends the copy that did not answer; true once it has ended.</summary>
        public bool Terminate()
        {
            try
            {
                if (!Child.HasExited)
                {
                    Child.Kill();
                }

                return Child.WaitForExit(10000);
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Child.HasExited;
            }
        }

        public void Dispose()
        {
            Child.Dispose();
            Ready.Dispose();
        }
    }

    /// <summary>Starts the exe with the ready signal.</summary>
    public static StartedCopy Start(string exePath, params string[] moreArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        var name = $"Local\\SatellesTSD-ready-{Guid.NewGuid():N}";
        var ready = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        try
        {
            var info = new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath)! };
            info.ArgumentList.Add(ReadyArgument);
            info.ArgumentList.Add(name);
            foreach (var argument in moreArguments)
            {
                info.ArgumentList.Add(argument);
            }

            var child = Process.Start(info) ?? throw new UpdateException(UpdateException.SwapFailed, "the new program did not start.");
            return new StartedCopy(child, ready);
        }
        catch
        {
            ready.Dispose();
            throw;
        }
    }

    /// <summary>Waits for the started copy to give its signal, to end, or for the timeout.</summary>
    public static StartResult WaitFor(StartedCopy copy, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(copy);
        var clock = Stopwatch.StartNew();
        do
        {
            if (copy.Ready.WaitOne(200))
            {
                return new StartResult(StartOutcome.Ready);
            }

            if (copy.Child.HasExited)
            {
                return copy.Ready.WaitOne(0) ? new StartResult(StartOutcome.Ready) : new StartResult(StartOutcome.Exited, copy.Child.ExitCode);
            }
        }
        while (clock.ElapsedMilliseconds < timeoutMs);

        return new StartResult(StartOutcome.NoAnswer);
    }

    /// <summary>Gives the signal <see cref="WaitFor"/> waits for; nothing happens when nobody waits.</summary>
    public static void SignalReady(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && EventWaitHandle.TryOpenExisting(name, out var signal))
        {
            using (signal)
            {
                signal.Set();
            }
        }
    }

    /// <summary>How many other windows of the program run from the path — they go on with the old version until
    /// closed. A copy whose file was renamed aside still reports the original path, so it is counted too.</summary>
    public static int OtherCopiesRunning(string exePath)
    {
        var count = 0;
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            try
            {
                if (process.Id != self && string.Equals(process.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // another user's process, or one that has just ended
            }

            process.Dispose();
        }

        return count;
    }

    // ---------------------------------------------------------------- the hidden folder

    /// <summary>Tidies the hidden folder at start: leftover downloads, unpacked releases and failed versions go, and of
    /// the previous versions only the newest stays. A file still in use (a window of an older version is open) is
    /// left for a later start. The folder goes when it is empty.</summary>
    public static void CleanUp(string exePath)
    {
        try
        {
            var staging = StagingFolder(exePath);
            if (!Directory.Exists(staging))
            {
                return;
            }

            // another window in the middle of an update keeps its download and its unpacked release
            using var held = TakeInstallLock(exePath);
            if (held is null)
            {
                return;
            }

            foreach (var file in Directory.GetFiles(staging, "download-*.zip"))
            {
                TryDelete(file);
            }

            foreach (var folder in Directory.GetDirectories(staging, "unpacked-*").Concat(Directory.GetDirectories(staging, "failed-*")))
            {
                TryDeleteFolder(folder);
            }

            // the names carry the time they were set aside, so the newest sorts last
            var previous = Directory.GetDirectories(staging, "previous-*").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var folder in previous.Take(Math.Max(0, previous.Count - 1)))
            {
                TryDeleteFolder(folder);
            }

            // the lock file alone (gone when the lock is let go) does not keep the folder
            if (Directory.GetFileSystemEntries(staging).All(e => string.Equals(Path.GetFileName(e), "install.lock", StringComparison.OrdinalIgnoreCase)))
            {
                held.Dispose();
                Directory.Delete(staging);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string PrepareStaging(string exePath)
    {
        var staging = StagingFolder(exePath);
        try
        {
            var created = Directory.CreateDirectory(staging);
            created.Attributes |= FileAttributes.Hidden;
            return staging;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateException.NoWriteAccess, $"the program's folder cannot be written to ({Path.GetDirectoryName(exePath)}): {e.Message}", e);
        }
    }

    private static void WaitUntilFree(string target)
    {
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }

                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 20)
                {
                    throw new UpdateException(UpdateException.Busy, "the release file is held by another program (an antivirus?); try again.", e);
                }

                Thread.Sleep(250);
            }
        }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var folder in Directory.GetDirectories(from))
        {
            CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}
