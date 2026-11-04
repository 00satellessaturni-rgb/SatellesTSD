<#
.SYNOPSIS
Publishes a release build of SatellesTSD: a self-contained win-x64 single-file app with the schemas beside it,
into a versioned folder and a zip of it.

.DESCRIPTION
1. Checks that the git tree is clean and pushed (the .claude folder aside), so the release is a commit anyone
   can find. -AllowDirty skips the check, for trying the script only.
2. Reads the version from SatellesTSD\SatellesTSD.csproj (<Version>), the same one the window title shows.
3. Runs `dotnet publish` for win-x64, self-contained, single file, the native SQLite library extracted at run
   time, trimming off (WinForms, ClosedXML, AngleSharp and reflection-based JSON do not survive it).
4. Checks the output: the exe, the schemas\apa-tsd tree (the XML export validates against it), no stray
   runtimes folder.
5. Copies it to <OutputFolder>\SatellesTSD-v<version>\, writes release.txt (version, commit, date, the exe's
   size and SHA-256) into it, zips the folder next to it, and appends a line to <OutputFolder>\publish.log.
   A folder already there for the same version is refused unless -Force.

The default output folder is <repo>\release (ignored by git). Installing on a PC: copy the folder anywhere
the user can write (not a network drive, not the build folder), run SatellesTSD.exe; the store file and the
settings live outside the folder, so a newer folder copied over an older one keeps everything.

.EXAMPLE
tools\Publish-Release.ps1
.EXAMPLE
tools\Publish-Release.ps1 -OutputFolder D:\SatellesTSD-releases -AllowDirty
#>
param(
    [string]$OutputFolder,
    [switch]$Force,
    # For trying the script: skips the clean-and-pushed check. Never for a real release.
    [switch]$AllowDirty
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'SatellesTSD\SatellesTSD.csproj'
$utf8 = New-Object Text.UTF8Encoding $false

function Refuse([string]$why) {
    Write-Host ''
    Write-Host "NOT PUBLISHED: $why" -ForegroundColor Red
    exit 1
}

function Sha256([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}

# --- the commit ------------------------------------------------------------------------------
$commit = (& git -C $repo rev-parse HEAD).Trim()
# the session's .claude folder is the tool's, never the release's
$dirtyFiles = @(& git -C $repo status --porcelain | Where-Object { $_ -notmatch '^\?\? \.claude/' })
$branch = (& git -C $repo status -sb | Select-Object -First 1)
if (-not $AllowDirty) {
    if ($dirtyFiles.Count -gt 0) { Refuse "the git tree has uncommitted changes. Commit (and push) first.`n$($dirtyFiles -join "`n")" }
    # "## master...origin/master" with nothing in brackets: a branch with an upstream, level with it
    if ($branch -notmatch '^## \S+\.\.\.\S+') { Refuse "the branch has no upstream to be found at ($branch). Push it first." }
    if ($branch -match '\[gone\]') { Refuse "the upstream is gone ($branch). Push first." }
    if ($branch -match '\[ahead') { Refuse "the last commit is not pushed yet ($branch). Push first." }
}
# a trial from a dirty tree is named as one and says so, so it is never taken for the release of its commit
$dirty = $dirtyFiles.Count -gt 0

# --- the version -----------------------------------------------------------------------------
$csproj = [IO.File]::ReadAllText($project, $utf8)
$m = [regex]::Match($csproj, '<Version>([0-9]+(?:\.[0-9]+)*)</Version>')
if (-not $m.Success) { Refuse 'no numeric <Version> (such as 0.10.0) in SatellesTSD\SatellesTSD.csproj.' }
$version = $m.Groups[1].Value
Write-Host "Version: $version (commit $($commit.Substring(0, 7))$(if ($dirty) { ', DIRTY TREE' }))"

# --- where to --------------------------------------------------------------------------------
if (-not $OutputFolder) { $OutputFolder = Join-Path $repo 'release' }
if (-not (Test-Path -LiteralPath $OutputFolder -PathType Container)) { New-Item -ItemType Directory -Path $OutputFolder | Out-Null }
$name = "SatellesTSD-v$version"
if ($dirty) { $name = "$name-dirty" }
$target = Join-Path $OutputFolder $name
$zip = Join-Path $OutputFolder "$name.zip"
if ((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath $zip)) {
    if (-not $Force) { Refuse "$name is already in $OutputFolder. Raise the version in the csproj, or -Force to replace it." }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
}

# --- the build -------------------------------------------------------------------------------
$publishDir = Join-Path $repo "SatellesTSD\bin\Release\net10.0-windows\win-x64\publish"
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
Write-Host 'Publishing (win-x64, self-contained, single file)...'
& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:PublishReadyToRun=false `
    -p:DebugType=none -nologo -v minimal
if ($LASTEXITCODE -ne 0) { Refuse 'dotnet publish failed.' }

# --- the output ------------------------------------------------------------------------------
$exe = Join-Path $publishDir 'SatellesTSD.exe'
if (-not (Test-Path -LiteralPath $exe)) { Refuse "no SatellesTSD.exe in $publishDir." }
$schemas = Join-Path $publishDir 'schemas\apa-tsd'
if (-not (Test-Path -LiteralPath (Join-Path $schemas 'xbrl')) -or -not (Test-Path -LiteralPath (Join-Path $schemas 'gl'))) {
    Refuse "the schemas folder is incomplete in $publishDir (schemas\apa-tsd\xbrl and \gl are needed for the XML export)."
}
if (Test-Path -LiteralPath (Join-Path $publishDir 'runtimes')) { Refuse 'a runtimes folder is in the output: the native libraries were not bundled.' }
$fileVersion = [version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion
$wanted = [version]$version
if ($fileVersion.Major -ne $wanted.Major -or $fileVersion.Minor -ne $wanted.Minor -or [Math]::Max($fileVersion.Build, 0) -ne [Math]::Max($wanted.Build, 0)) {
    Refuse "the exe carries version $fileVersion, not $version - is it built from this commit?"
}

# --- copy, describe, zip ---------------------------------------------------------------------
New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $target -Recurse
$size = (Get-Item -LiteralPath $exe).Length
$hash = Sha256 $exe
$published = Get-Date -Format 'dd.MM.yyyy HH:mm'
$text = "# SatellesTSD release - written by tools\Publish-Release.ps1`r`n" +
        "version=$version`r`ncommit=$commit`r`ndirty=$(if ($dirty) { 'true (built from uncommitted changes - a trial, not the release of this commit)' } else { 'false' })`r`n" +
        "published=$published`r`nexe=SatellesTSD.exe`r`nsize=$size`r`nsha256=$hash`r`n" +
        "install=unpack beside the previous version, close the app, point the shortcut at this folder's SatellesTSD.exe; the store (.db) and the settings (%AppData%\SatellesTSD) live outside it`r`n"
[IO.File]::WriteAllBytes((Join-Path $target 'release.txt'), $utf8.GetBytes($text))
# the zip entry by entry, named with '/' as the format wants: both Compress-Archive and ZipFile.CreateFromDirectory
# under Windows PowerShell 5.1 (.NET Framework) write '\' into the names, which other unpackers take literally
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $target -File -Recurse) {
        $relative = $file.FullName.Substring($target.Length).TrimStart('\').Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "$name/$relative", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archive.Dispose()
}
$line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "  $env:USERNAME  published $version$(if ($dirty) { ' (dirty tree)' })  $hash  commit $commit`r`n"
[IO.File]::AppendAllText((Join-Path $OutputFolder 'publish.log'), $line, $utf8)

Write-Host ''
Write-Host "Published $version to $target ($size bytes exe, zip beside it)$(if ($dirty) { ' - A TRIAL FROM A DIRTY TREE, not a release' })." -ForegroundColor Green
