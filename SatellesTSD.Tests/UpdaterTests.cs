using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SatellesTSD.Infrastructure.Updates;

namespace SatellesTSD.Tests;

/// <summary>The updater's mechanics on temp folders: the manifest, the check's states, the download's checks, the
/// unpacking and the program check, the swap of the exe and the schemas and the way back, the tidying. The real exe
/// is stood in for by a text file; the program check takes a reader for the fake.</summary>
public class UpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"satellestsd-updater-{Guid.NewGuid():N}");
    private readonly string _releases;
    private readonly string _program;
    private readonly string _exe;

    public UpdaterTests()
    {
        _releases = Path.Combine(_root, "releases");
        _program = Path.Combine(_root, "program");
        _exe = Path.Combine(_program, Updater.ExeName);
        Directory.CreateDirectory(_releases);
        Directory.CreateDirectory(_program);
        File.WriteAllText(_exe, "exe 0.10.0");
        Directory.CreateDirectory(Path.Combine(_program, "schemas", "apa-tsd", "xbrl"));
        File.WriteAllText(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "old.xsd"), "old");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static ProgramInfo FakeReader(string path)
    {
        var text = File.ReadAllText(path);
        return text.StartsWith("exe ", StringComparison.Ordinal) ? new ProgramInfo("SatellesTSD", Updater.ParseVersion(text[4..])) : new ProgramInfo("Other", null);
    }

    /// <summary>A release zip as the publish script makes one: a root folder with the exe, release.txt and the schemas.</summary>
    private (string Zip, ReleaseManifest Release) Publish(string version, bool withSchemas = true, string exeText = "exe ")
    {
        var name = $"SatellesTSD-v{version}";
        var folder = Path.Combine(_root, "build", name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Updater.ExeName), exeText + version);
        File.WriteAllText(Path.Combine(folder, "release.txt"), $"version={version}");
        if (withSchemas)
        {
            Directory.CreateDirectory(Path.Combine(folder, "schemas", "apa-tsd", "xbrl"));
            Directory.CreateDirectory(Path.Combine(folder, "schemas", "apa-tsd", "gl"));
            File.WriteAllText(Path.Combine(folder, "schemas", "apa-tsd", "xbrl", "new.xsd"), "new " + version);
        }

        var zip = Path.Combine(_releases, name + ".zip");
        ZipFile.CreateFromDirectory(Path.Combine(_root, "build", name), zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        var bytes = File.ReadAllBytes(zip);
        var exeBytes = File.ReadAllBytes(Path.Combine(folder, Updater.ExeName));
        var release = new ReleaseManifest(Updater.ParseVersion(version)!, name + ".zip", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "04.11.2026 15:00", "abc123", exeBytes.Length, Convert.ToHexString(SHA256.HashData(exeBytes)));
        File.WriteAllText(Path.Combine(_releases, Updater.ManifestName), $"# test\nversion={version}\nfile={release.FileName}\nsize={release.Size}\nsha256={release.Sha256}\npublished={release.Published}\ncommit={release.Commit}\nexe_size={release.ExeSize}\nexe_sha256={release.ExeSha256}\n", Encoding.UTF8);
        return (zip, release);
    }

    [Fact]
    public void Versions_parse_with_or_without_the_v_and_compare_as_numbers()
    {
        Assert.Equal(new Version(0, 10, 0, 0), Updater.ParseVersion("0.10.0"));
        Assert.Equal(new Version(0, 10, 0, 0), Updater.ParseVersion("v0.10"));
        Assert.Null(Updater.ParseVersion("0.10.0-beta"));
        Assert.Null(Updater.ParseVersion("ten"));
        Assert.Null(Updater.ParseVersion("10"));
        Assert.True(Updater.ParseVersion("0.11.0")! > Updater.ParseVersion("0.10.0")!);
        Assert.True(Updater.ParseVersion("0.10.10")! > Updater.ParseVersion("0.10.9")!);
    }

    [Fact]
    public void The_manifest_needs_its_four_facts_and_nothing_odd()
    {
        var release = Updater.ParseManifest("# a comment\r\nversion=0.11.0\r\nfile=SatellesTSD-v0.11.0.zip\r\nsize=48176162\r\nsha256=" + new string('a', 64) + "\r\npublished=04.11.2026 15:00\r\ncommit=abc\r\nexe_size=121453831\r\nexe_sha256=" + new string('b', 64) + "\r\n");
        Assert.Equal((new Version(0, 11, 0, 0), "SatellesTSD-v0.11.0.zip", 48176162L, new string('A', 64), "04.11.2026 15:00", "abc"), (release.Version, release.FileName, release.Size, release.Sha256, release.Published, release.Commit));
        Assert.Equal((121453831L, new string('B', 64)), (release.ExeSize, release.ExeSha256));
        Assert.Null(Updater.ParseManifest("version=1.0\nfile=SatellesTSD-v1.0.zip\nsize=1\nsha256=" + new string('a', 64)).ExeSize);
        Assert.Contains("version", Assert.Throws<FormatException>(() => Updater.ParseManifest("file=a.zip\nsize=1\nsha256=" + new string('a', 64))).Message);
        Assert.Contains("file", Assert.Throws<FormatException>(() => Updater.ParseManifest("version=1.0\nfile=..\\SatellesTSD-v1.0.zip\nsize=1\nsha256=" + new string('a', 64))).Message);
        Assert.Contains("file", Assert.Throws<FormatException>(() => Updater.ParseManifest("version=1.0\nfile=other.zip\nsize=1\nsha256=" + new string('a', 64))).Message);
        Assert.Contains("size", Assert.Throws<FormatException>(() => Updater.ParseManifest("version=1.0\nfile=SatellesTSD-v1.0.zip\nsize=0\nsha256=" + new string('a', 64))).Message);
        Assert.Contains("sha256", Assert.Throws<FormatException>(() => Updater.ParseManifest("version=1.0\nfile=SatellesTSD-v1.0.zip\nsize=1\nsha256=xyz")).Message);
        Assert.Contains("exe_sha256", Assert.Throws<FormatException>(() => Updater.ParseManifest("version=1.0\nfile=SatellesTSD-v1.0.zip\nsize=1\nsha256=" + new string('a', 64) + "\nexe_sha256=short")).Message);
        Assert.Contains("key=value", Assert.Throws<FormatException>(() => Updater.ParseManifest("version=1.0\nnonsense\n")).Message);
        Assert.Contains("twice", Assert.Throws<FormatException>(() => Updater.ParseManifest("version=1.0\nVersion=2.0\nfile=SatellesTSD-v1.0.zip\nsize=1\nsha256=" + new string('a', 64))).Message);
    }

    [Fact]
    public async Task The_check_says_what_it_found()
    {
        var current = new Version(0, 10, 0);
        Assert.Equal(UpdateState.Unreachable, Updater.Check(Path.Combine(_root, "nowhere"), current).State);
        Assert.Equal(UpdateState.Unreachable, Updater.Check(@"\\nowhere-at-all-" + Guid.NewGuid().ToString("N")[..8] + @"\share\folder", current).State);
        Assert.Equal(UpdateState.NoRelease, Updater.Check(_releases, current).State);
        File.WriteAllText(Path.Combine(_releases, Updater.ManifestName), "version=soon\n");
        Assert.Equal(UpdateState.BadRelease, Updater.Check(_releases, current).State);

        Publish("0.10.0");
        Assert.Equal(UpdateState.UpToDate, Updater.Check(_releases, current).State);
        var (_, newer) = Publish("0.11.0");
        var check = Updater.Check(_releases, current);
        Assert.Equal((UpdateState.Available, newer), (check.State, check.Release));
        Assert.Equal(UpdateState.UpToDate, Updater.Check(_releases, new Version(0, 12, 0)).State); // older on the NAS: nothing offered

        var timed = await Updater.CheckAsync(_releases, current, timeoutMs: 15000);
        Assert.Equal(UpdateState.Available, timed.State);
    }

    [Fact]
    public void The_download_takes_only_a_whole_and_matching_file()
    {
        var (zip, release) = Publish("0.11.0");
        var copied = new List<long>();
        var downloaded = Updater.Download(_releases, release, _exe, new Progress<long>(copied.Add), CancellationToken.None);
        Assert.StartsWith(Updater.StagingFolder(_exe), downloaded, StringComparison.Ordinal);
        Assert.Equal(release.Size, new FileInfo(downloaded).Length);
        Assert.True((File.GetAttributes(Updater.StagingFolder(_exe)) & FileAttributes.Hidden) != 0);

        Assert.Equal(UpdateException.HashMismatch, Assert.Throws<UpdateException>(() => Updater.Download(_releases, release with { Sha256 = new string('0', 64) }, _exe, null, CancellationToken.None)).Code);
        Assert.Equal(UpdateException.TooLarge, Assert.Throws<UpdateException>(() => Updater.Download(_releases, release with { Size = release.Size - 1 }, _exe, null, CancellationToken.None)).Code);
        Assert.Equal(UpdateException.Incomplete, Assert.Throws<UpdateException>(() => Updater.Download(_releases, release with { Size = release.Size + 1 }, _exe, null, CancellationToken.None)).Code);
        Assert.Equal(UpdateException.ReadFailed, Assert.Throws<UpdateException>(() => Updater.Download(_releases, release with { FileName = "missing.zip" }, _exe, null, CancellationToken.None)).Code);
        Assert.Single(Directory.GetFiles(Updater.StagingFolder(_exe), "download-*.zip")); // the failed ones were removed
        File.Delete(zip);
    }

    [Fact]
    public void The_unpacked_release_is_checked_to_be_this_program_at_the_named_version_with_its_schemas()
    {
        var (_, release) = Publish("0.11.0");
        var downloaded = Updater.Download(_releases, release, _exe, null, CancellationToken.None);
        var folder = Updater.Unpack(downloaded, _exe);
        Assert.Equal("SatellesTSD-v0.11.0", Path.GetFileName(folder));
        Updater.VerifyProgram(folder, release, FakeReader);
        Assert.Equal(UpdateException.VersionMismatch, Assert.Throws<UpdateException>(() => Updater.VerifyProgram(folder, release with { Version = new Version(0, 12, 0, 0) }, FakeReader)).Code);
        Assert.Equal(UpdateException.HashMismatch, Assert.Throws<UpdateException>(() => Updater.VerifyProgram(folder, release with { ExeSha256 = new string('0', 64) }, FakeReader)).Code);
        Assert.Equal(UpdateException.HashMismatch, Assert.Throws<UpdateException>(() => Updater.VerifyProgram(folder, release with { ExeSize = 1 }, FakeReader)).Code);
        using (var held = Updater.TakeInstallLock(_exe))
        {
            Assert.NotNull(held);
            Assert.Null(Updater.TakeInstallLock(_exe)); // another window is updating
        }

        var again = Updater.TakeInstallLock(_exe);
        Assert.NotNull(again);
        again!.Dispose();

        var (_, other) = Publish("0.12.0", exeText: "not ");
        var otherFolder = Updater.Unpack(Updater.Download(_releases, other, _exe, null, CancellationToken.None), _exe);
        Assert.Equal(UpdateException.NotTheProgram, Assert.Throws<UpdateException>(() => Updater.VerifyProgram(otherFolder, other, FakeReader)).Code);

        var (_, bare) = Publish("0.13.0", withSchemas: false);
        var bareFolder = Updater.Unpack(Updater.Download(_releases, bare, _exe, null, CancellationToken.None), _exe);
        Assert.Equal(UpdateException.SchemasMissing, Assert.Throws<UpdateException>(() => Updater.VerifyProgram(bareFolder, bare, FakeReader)).Code);

        File.WriteAllText(Path.Combine(_releases, "junk.zip"), "not a zip");
        Assert.Equal(UpdateException.BadZip, Assert.Throws<UpdateException>(() => Updater.Unpack(Path.Combine(_releases, "junk.zip"), _exe)).Code);
    }

    [Fact]
    public void The_swap_renames_the_running_program_aside_and_the_way_back_restores_it()
    {
        var (_, release) = Publish("0.11.0");
        var folder = Updater.Unpack(Updater.Download(_releases, release, _exe, null, CancellationToken.None), _exe);
        var previous = Updater.Swap(_exe, folder);
        Assert.Equal("exe 0.11.0", File.ReadAllText(_exe));
        Assert.Equal("new 0.11.0", File.ReadAllText(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "new.xsd")));
        Assert.False(File.Exists(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "old.xsd")));
        Assert.Equal("exe 0.10.0", File.ReadAllText(Path.Combine(previous, Updater.ExeName)));
        Assert.Equal("old", File.ReadAllText(Path.Combine(previous, "schemas", "apa-tsd", "xbrl", "old.xsd")));
        Assert.False(Directory.Exists(Path.Combine(folder, "schemas")));

        // the new one did not start: the previous goes back, the failed build is remembered
        Updater.Revert(_exe, previous, release);
        Assert.Equal("exe 0.10.0", File.ReadAllText(_exe));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "old.xsd")));
        Assert.False(File.Exists(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "new.xsd")));
        Assert.True(Updater.FailedHere(_exe, release));
        Assert.False(Updater.FailedHere(_exe, release with { Sha256 = new string('1', 64) }));
        var failed = Assert.Single(Directory.GetDirectories(Updater.StagingFolder(_exe), "failed-*"));
        Assert.Equal("exe 0.11.0", File.ReadAllText(Path.Combine(failed, Updater.ExeName)));

        // the next start tidies: the failed and unpacked folders go, of the previous ones the newest alone stays, the note stays
        var staging = Updater.StagingFolder(_exe);
        var older = Path.Combine(staging, "previous-20200101-000000-000");
        Directory.CreateDirectory(older);
        File.WriteAllText(Path.Combine(older, Updater.ExeName), "exe 0.9.0");
        using (var busy = Updater.TakeInstallLock(_exe))
        {
            Updater.CleanUp(_exe); // another window is in the middle of an update: nothing is touched
            Assert.Single(Directory.GetDirectories(staging, "failed-*"));
        }

        Updater.CleanUp(_exe);
        Assert.Empty(Directory.GetDirectories(staging, "failed-*"));
        Assert.Empty(Directory.GetDirectories(staging, "unpacked-*"));
        Assert.Empty(Directory.GetFiles(staging, "download-*.zip"));
        Assert.Equal(previous, Assert.Single(Directory.GetDirectories(staging, "previous-*")));
        Assert.True(Updater.FailedHere(_exe, release));
    }

    [Fact]
    public void A_swap_that_fails_half_way_undoes_what_it_did()
    {
        var (_, release) = Publish("0.11.0");
        var folder = Updater.Unpack(Updater.Download(_releases, release, _exe, null, CancellationToken.None), _exe);
        Directory.Delete(Path.Combine(folder, "schemas"), true); // the last step cannot be made
        Assert.Equal(UpdateException.SwapFailed, Assert.Throws<UpdateException>(() => Updater.Swap(_exe, folder)).Code);
        Assert.Equal("exe 0.10.0", File.ReadAllText(_exe));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "old.xsd")));
        Assert.Equal("exe 0.11.0", File.ReadAllText(Path.Combine(folder, Updater.ExeName)));
        Assert.Empty(Directory.GetDirectories(Updater.StagingFolder(_exe), "previous-*")); // no emptied folder left to pass for the newest
    }

    [Fact]
    public void The_copy_a_failed_update_left_is_on_record_for_its_store_until_the_store_opens_again()
    {
        var store = Path.Combine(Path.GetDirectoryName(_exe)!, "pood.db");
        var copy = store + ".before-v99";
        File.WriteAllText(copy, "the store before the upgrade");
        Assert.Null(Updater.FailedUpdateCopy(_exe, store, 98)); // no update failed here: nothing on record

        // the failed build's note alone says nothing: it is never removed
        Directory.CreateDirectory(Updater.StagingFolder(_exe));
        File.WriteAllText(Path.Combine(Updater.StagingFolder(_exe), "failed.txt"), "0.99.0.0 abc");
        Assert.Null(Updater.FailedUpdateCopy(_exe, store, 98));

        // noted when the update failed: offered from then on, however long after, whatever the clocks say
        Updater.NoteStoreCopy(_exe, store, copy);
        Assert.Equal(copy, Updater.FailedUpdateCopy(_exe, store, 98));
        Assert.Equal(copy, Updater.FailedUpdateCopy(_exe, store.ToUpperInvariant(), 98), ignoreCase: true);
        Assert.Null(Updater.FailedUpdateCopy(_exe, Path.Combine(Path.GetDirectoryName(_exe)!, "teine.db"), 98)); // another store's is not this one's

        // another file under the copy's name (another computer's upgrade, months on) is not the one on record
        File.WriteAllText(copy, "another computer's copy, made later");
        File.SetLastWriteTimeUtc(copy, new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.Null(Updater.FailedUpdateCopy(_exe, store, 98));

        // a later failed update's copy takes the store's one entry: the earlier episode's copy is offered no more
        var later = store + ".before-v100";
        File.WriteAllText(later, "the store before the later upgrade");
        Updater.NoteStoreCopy(_exe, store, later);
        Assert.Equal(later, Updater.FailedUpdateCopy(_exe, store, 98));
        Assert.Single(File.ReadAllLines(Path.Combine(Updater.StagingFolder(_exe), "store-copies.txt")));

        // another store's entry stands beside it
        var other = Path.Combine(Path.GetDirectoryName(_exe)!, "teine.db");
        File.WriteAllText(other + ".before-v99", "teine");
        Updater.NoteStoreCopy(_exe, other, other + ".before-v99");
        Assert.Equal(2, File.ReadAllLines(Path.Combine(Updater.StagingFolder(_exe), "store-copies.txt")).Length);

        // a build at the copy's schema or beyond has nothing to go back to in it: not offered to it
        Assert.Null(Updater.FailedUpdateCopy(_exe, store, 100));

        // the failed version itself opening the store (a retry of the update) ends nothing: the entry stays for the
        // program that is put back again
        Updater.ForgetStoreCopy(_exe, store, 100);
        Assert.Equal(later, Updater.FailedUpdateCopy(_exe, store, 98));

        // the copies an update made while it ran: one that was not there before, or is another file now — the latest written
        var before = Updater.StoreCopiesOf(store);
        Assert.Equal(2, before.Count);
        Assert.Null(Updater.CopyMadeSince(before, store)); // nothing changed: nothing made
        File.WriteAllText(store + ".before-v101", "made by the update");
        File.SetLastWriteTimeUtc(store + ".before-v101", new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal(store + ".before-v101", Updater.CopyMadeSince(before, store));
        File.SetLastWriteTimeUtc(later, new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc)); // and an old name written anew, later still
        Assert.Equal(later, Updater.CopyMadeSince(before, store));
        Updater.NoteStoreCopy(_exe, store, later);
        Assert.Empty(Updater.StoreCopiesOf(null));

        // an older build opens the store as its own again: the episode is over, nothing of it is offered later
        Updater.ForgetStoreCopy(_exe, store, 98);
        Assert.Null(Updater.FailedUpdateCopy(_exe, store, 98));
        Assert.Equal(other + ".before-v99", Updater.FailedUpdateCopy(_exe, other, 98));
    }

    [Fact]
    public void A_failed_exe_that_cannot_be_moved_aside_leaves_the_program_whole_and_says_so()
    {
        var (_, release) = Publish("0.11.0");
        var folder = Updater.Unpack(Updater.Download(_releases, release, _exe, null, CancellationToken.None), _exe);
        var previous = Updater.Swap(_exe, folder);
        using (new FileStream(_exe, FileMode.Open, FileAccess.Read, FileShare.Read)) // held, as an antivirus would
        {
            Assert.Equal(UpdateException.RevertFailed, Assert.Throws<UpdateException>(() => Updater.Revert(_exe, previous, release)).Code);
        }

        Assert.Equal("exe 0.11.0", File.ReadAllText(_exe));
        Assert.True(File.Exists(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "new.xsd"))); // the new exe keeps its schemas
        Assert.False(Updater.FailedHere(_exe, release));
    }

    [Fact]
    public void The_release_folder_can_be_overridden_beside_the_exe_and_some_copies_are_never_updated()
    {
        Assert.Equal(Updater.DefaultReleaseFolder, Updater.ReleaseFolderFor(_exe));
        File.WriteAllText(Path.Combine(_program, Updater.FolderOverrideFile), "\n  " + _releases + "  \n", Encoding.UTF8);
        Assert.Equal(_releases, Updater.ReleaseFolderFor(_exe));
        File.WriteAllText(Path.Combine(_program, Updater.FolderOverrideFile), "\"" + _releases + "\"\n", Encoding.UTF8); // Explorer's "Copy as path"
        Assert.Equal(_releases, Updater.ReleaseFolderFor(_exe));

        Assert.Equal(InstallBlock.BuildFolder, Updater.InstallBlockOf(@"C:\src\SatellesTSD\bin\Debug\net10.0-windows\SatellesTSD.exe"));
        Assert.Equal(InstallBlock.BuildFolder, Updater.InstallBlockOf(@"C:\src\SatellesTSD\bin\Release\net10.0-windows\win-x64\publish\SatellesTSD.exe"));
        Assert.Equal(InstallBlock.BuildFolder, Updater.InstallBlockOf(_exe, debuggerAttached: true));
        Assert.Equal(InstallBlock.NetworkDrive, Updater.InstallBlockOf(@"\\nas\share\SatellesTSD\SatellesTSD.exe"));
        Assert.Equal(InstallBlock.None, Updater.InstallBlockOf(_exe));
    }

    [Fact]
    public void The_ready_signal_is_given_and_heard_and_a_silent_start_is_told_apart()
    {
        var name = $"Local\\SatellesTSD-ready-test-{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        Updater.SignalReady("Local\\SatellesTSD-nobody-waits");
        Updater.SignalReady(name);
        Assert.True(ready.WaitOne(0));
    }

    [Fact]
    public void A_copy_back_that_fails_leaves_the_new_program_whole_and_says_so()
    {
        var (_, release) = Publish("0.11.0");
        var folder = Updater.Unpack(Updater.Download(_releases, release, _exe, null, CancellationToken.None), _exe);
        var previous = Updater.Swap(_exe, folder);
        using (new FileStream(Path.Combine(previous, Updater.ExeName), FileMode.Open, FileAccess.Read, FileShare.None)) // the kept copy cannot be read back
        {
            Assert.Equal(UpdateException.RevertFailed, Assert.Throws<UpdateException>(() => Updater.Revert(_exe, previous, release)).Code);
        }

        Assert.Equal("exe 0.11.0", File.ReadAllText(_exe)); // the new exe came back from the failed folder, with its schemas
        Assert.Equal("new 0.11.0", File.ReadAllText(Path.Combine(_program, "schemas", "apa-tsd", "xbrl", "new.xsd")));
        Assert.False(Updater.FailedHere(_exe, release));
    }
}
