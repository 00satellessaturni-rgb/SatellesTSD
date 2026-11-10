<#
.SYNOPSIS
Publishes a release build of SatellesTSD: a self-contained win-x64 single-file app with the schemas beside it,
into a versioned folder and a zip of it — and the zip into the release folder on the NAS, where every copy of
the program finds it at its next start and offers the update (in the title and on its button).

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
6. Unless -LocalOnly: refuses a version that is not above every version ever on offer there (a withdrawn one
   counts: the copies that installed it compare version numbers only, so a build numbered at or below it
   would never reach them - a fix takes a number above the withdrawn one), copies the zip to the release folder
   on the NAS (via a .partial file, its SHA-256 checked), then writes latest.txt - last, so no copy is ever
   offered a file still being copied. The previous latest.txt is kept as latest.previous.txt. -Withdraw puts
   it back and sets the withdrawn zip aside (once per release).

The default output folder is <repo>\release (ignored by git). The release folder is read from Updater.cs
(DefaultReleaseFolder) unless -ReleaseFolder is given; only the owner may write to it: whatever is published
there runs on every PC. The first install on a PC: unpack the zip anywhere the user can write (not a network
drive, not the build folder) and run SatellesTSD.exe; from then on the program updates itself. The store file
and the settings live outside the folder.

.EXAMPLE
tools\Publish-Release.ps1
.EXAMPLE
tools\Publish-Release.ps1 -OutputFolder D:\SatellesTSD-releases -AllowDirty
#>
param(
    [string]$OutputFolder,
    # The release folder on the NAS every copy of the program updates from; read from Updater.cs unless given.
    [string]$ReleaseFolder,
    # Builds and zips locally only: nothing goes to the NAS.
    [switch]$LocalOnly,
    # Puts the previous release back on offer in the NAS folder and sets the withdrawn zip aside. Once per release.
    [switch]$Withdraw,
    [switch]$Force,
    # For trying the script: skips the clean-and-pushed check. Never for a real release; never published to the NAS.
    [switch]$AllowDirty
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'SatellesTSD\SatellesTSD.csproj'
$updater = Join-Path $repo 'SatellesTSD.Infrastructure\Updates\Updater.cs'
$utf8 = New-Object Text.UTF8Encoding $false

function Read-Manifest([string]$path) {
    $values = @{}
    foreach ($line in [IO.File]::ReadAllText($path, $utf8) -split "`n") {
        $line = $line.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
        $eq = $line.IndexOf('=')
        if ($eq -gt 0) { $values[$line.Substring(0, $eq).Trim()] = $line.Substring($eq + 1).Trim() }
    }
    return $values
}

function Write-NasLog([string]$folder, [string]$text) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "  $env:USERNAME  $text`r`n"
    [IO.File]::AppendAllText((Join-Path $folder 'publish.log'), $line, $utf8)
}

# --- the NAS folder --------------------------------------------------------------------------
if (-not $ReleaseFolder) {
    $source = [IO.File]::ReadAllText($updater, $utf8)
    $m = [regex]::Match($source, 'DefaultReleaseFolder = @"([^"]+)"')
    if (-not $m.Success) { Write-Host 'NOT PUBLISHED: DefaultReleaseFolder not found in Updater.cs.' -ForegroundColor Red; exit 1 }
    $ReleaseFolder = $m.Groups[1].Value
}
$manifest = Join-Path $ReleaseFolder 'latest.txt'
$previousManifest = Join-Path $ReleaseFolder 'latest.previous.txt'

# --- withdraw --------------------------------------------------------------------------------
if ($Withdraw) {
    if (-not (Test-Path -LiteralPath $previousManifest)) { Write-Host 'NOT WITHDRAWN: there is no latest.previous.txt to go back to.' -ForegroundColor Red; exit 1 }
    if (-not (Test-Path -LiteralPath $manifest)) { Write-Host "NOT WITHDRAWN: there is no latest.txt in $ReleaseFolder (an interrupted publish? rename latest.txt.new to latest.txt, or publish again)." -ForegroundColor Red; exit 1 }
    $withdrawn = (Read-Manifest $manifest)['version']
    $restored = (Read-Manifest $previousManifest)['version']
    # once withdrawn, latest.txt and latest.previous.txt name the same release: a second withdraw would take that one too
    if ($withdrawn -eq $restored) { Write-Host "NOT WITHDRAWN: $withdrawn is on offer and is also the previous release - it has been withdrawn already." -ForegroundColor Red; exit 1 }
    [IO.File]::Copy($manifest, (Join-Path $ReleaseFolder 'latest.withdrawn.txt'), $true)
    $staged = "$manifest.new"
    [IO.File]::Copy($previousManifest, $staged, $true)
    [IO.File]::Delete($manifest)
    [IO.File]::Move($staged, $manifest)
    Write-NasLog $ReleaseFolder "withdrew $withdrawn, offering $restored again"
    # the zip itself is set aside too, so that no copy can still fetch it - and a fixed build can be published under the same version
    $withdrawnZip = Join-Path $ReleaseFolder "SatellesTSD-v$withdrawn.zip"
    if ($withdrawn -and (Test-Path -LiteralPath $withdrawnZip)) {
        try {
            $aside = "$withdrawnZip.withdrawn"
            if (Test-Path -LiteralPath $aside) { [IO.File]::Delete($aside) }
            [IO.File]::Move($withdrawnZip, $aside)
        } catch {
            Write-Host "WARNING: SatellesTSD-v$withdrawn.zip could not be set aside (a PC may be copying it). In a minute, rename it by hand to SatellesTSD-v$withdrawn.zip.withdrawn - do not run -Withdraw again." -ForegroundColor Yellow
        }
    }
    Write-Host "Withdrawn: $withdrawn. Offered now: $restored. Copies already updated keep it; publish a version above $withdrawn to replace it everywhere." -ForegroundColor Green
    exit 0
}

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
# a trial from a dirty, unpushed or upstream-less tree is named as one and says so, so it is never taken for the release of its commit
$dirty = ($dirtyFiles.Count -gt 0) -or ($branch -notmatch '^## \S+\.\.\.\S+') -or ($branch -match '\[gone\]') -or ($branch -match '\[ahead')

# "0.10" and "0.10.0" are one version: every part filled, as the program compares them
function Normalize-Version([string]$text) {
    $parts = @($text.Split('.') | ForEach-Object { [int]$_ })
    while ($parts.Count -lt 4) { $parts += 0 }
    return New-Object Version($parts[0], $parts[1], $parts[2], $parts[3])
}

# The versions the release folder has ever had on offer: the "published" and "withdrew" lines of its publish.log
# (a publish writes its line last, after latest.txt, so an interrupted one left none), and what the manifests there
# name - on offer now, before that, and withdrawn. Only what reads as a version as the program reads one (up to
# four parts of digits) counts: a line damaged by hand never stops a release.
function Get-AnnouncedVersions([string]$folder) {
    $found = New-Object 'System.Collections.Generic.List[Version]'
    $shape = '^[0-9]{1,6}(\.[0-9]{1,6}){0,3}$'
    $log = Join-Path $folder 'publish.log'
    if (Test-Path -LiteralPath $log) {
        foreach ($line in [IO.File]::ReadAllText($log, $utf8) -split "`n") {
            $hit = [regex]::Match($line, '\s(?:published|withdrew)\s+([0-9]+(?:\.[0-9]+)*)')
            if ($hit.Success -and $hit.Groups[1].Value -match $shape) { $found.Add((Normalize-Version $hit.Groups[1].Value)) }
        }
    }
    foreach ($name in 'latest.txt', 'latest.previous.txt', 'latest.withdrawn.txt') {
        $path = Join-Path $folder $name
        if (Test-Path -LiteralPath $path) {
            $named = (Read-Manifest $path)['version']
            if ($named -match $shape) { $found.Add((Normalize-Version $named)) }
        }
    }
    return ,$found
}

# A release takes a number above every version the release folder has ever had on offer, a withdrawn one included:
# the copies that installed a build compare numbers only, so a fix published after a withdrawal under the withdrawn
# number - or under any number below it - would never reach the copies that took the withdrawn build.
function Refuse-AnnouncedVersion([string]$folder, [string]$version) {
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { return }
    $mine = Normalize-Version $version
    $live = Join-Path $folder 'latest.txt'
    if (Test-Path -LiteralPath $live) {
        $onOffer = (Read-Manifest $live)['version']
        if ($onOffer -match '^[0-9]{1,6}(\.[0-9]{1,6}){0,3}$' -and (Normalize-Version $onOffer) -eq $mine) {
            Refuse "$version is on offer in the release folder now (latest.txt): it is published already. For a new build, raise the version in the csproj first."
        }
    }
    $highest = $null
    foreach ($v in (Get-AnnouncedVersions $folder)) { if ($null -eq $highest -or $v -gt $highest) { $highest = $v } }
    if ($null -ne $highest -and $mine -le $highest) {
        Refuse "$version is not above $($highest.ToString(3)), the highest version the release folder has ever had on offer (its publish.log and manifests; a withdrawn one counts). The copies that installed that one compare version numbers only and would never take a build numbered at or below it: raise the version in the csproj above it first."
    }
}

# --- the version -----------------------------------------------------------------------------
$csproj = [IO.File]::ReadAllText($project, $utf8)
$m = [regex]::Match($csproj, '<Version>([0-9]+(?:\.[0-9]+)*)</Version>')
if (-not $m.Success) { Refuse 'no numeric <Version> (such as 0.10.0) in SatellesTSD\SatellesTSD.csproj.' }
$version = $m.Groups[1].Value
Write-Host "Version: $version (commit $($commit.Substring(0, 7))$(if ($dirty) { ', DIRTY TREE' }))"
# said before the build, which takes minutes; asked again where the release folder is written to
if (-not $LocalOnly -and -not $dirty) { Refuse-AnnouncedVersion $ReleaseFolder $version }

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

# --- the NAS: every copy of the program updates from here ------------------------------------
if ($LocalOnly) { Write-Host 'Not sent to the release folder (-LocalOnly).'; exit 0 }
if ($dirty) { Write-Host 'Not sent to the release folder: a dirty-tree trial is never published.' -ForegroundColor Yellow; exit 0 }
if (-not (Test-Path -LiteralPath $ReleaseFolder -PathType Container)) {
    Refuse "the release folder does not exist or cannot be reached: $ReleaseFolder (create it first: read access for the office, write access for you only)."
}
$announced = $null
if (Test-Path -LiteralPath $manifest) {
    $liveManifest = Read-Manifest $manifest
    $live = $liveManifest['version']
    $announced = $liveManifest['file']
    if ($live -and ((Normalize-Version $version) -le (Normalize-Version $live))) { Refuse "$version is not newer than the published $live. Raise the version in the csproj first." }
}
Refuse-AnnouncedVersion $ReleaseFolder $version
$zipName = "$name.zip"
$nasZip = Join-Path $ReleaseFolder $zipName
$zipHash = Sha256 $zip
$zipSize = (Get-Item -LiteralPath $zip).Length
if ((Test-Path -LiteralPath $nasZip) -and ((Sha256 $nasZip) -eq $zipHash)) {
    Write-Host "$zipName is already there (same content) - announcing it."
} else {
    if (Test-Path -LiteralPath $nasZip) {
        # a zip never announced by latest.txt is a publish that stopped half way: it is replaced; an announced one never is
        if ($announced -eq $zipName) { Refuse "$zipName is on offer in the release folder with different content; withdraw it first." }
        Write-Host "$zipName is there from an interrupted publish - replacing it."
        [IO.File]::Delete($nasZip)
    }
    Write-Host "Copying $zipName to the release folder..."
    $partial = "$nasZip.partial"
    [IO.File]::Copy($zip, $partial, $true)
    if ((Sha256 $partial) -ne $zipHash) { [IO.File]::Delete($partial); Refuse 'the copy in the release folder does not match the build.' }
    [IO.File]::Move($partial, $nasZip)
}
# latest.txt last, so no copy is ever offered a file still being copied; the previous one kept for -Withdraw
$text = "# SatellesTSD release - written by tools\Publish-Release.ps1`r`n" +
        "version=$version`r`nfile=$zipName`r`nsize=$zipSize`r`nsha256=$zipHash`r`nexe_size=$size`r`nexe_sha256=$hash`r`npublished=$published`r`ncommit=$commit`r`n"
$staged = "$manifest.new"
[IO.File]::WriteAllBytes($staged, $utf8.GetBytes($text))
if (Test-Path -LiteralPath $manifest) {
    [IO.File]::Copy($manifest, $previousManifest, $true)
    [IO.File]::Delete($manifest)
}
[IO.File]::Move($staged, $manifest)
Write-NasLog $ReleaseFolder "published $version  $zipHash  commit $commit"
Write-Host "Released $version in $ReleaseFolder. Every copy of the program offers it at its next start." -ForegroundColor Green
