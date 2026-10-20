[CmdletBinding()]
param(
    # Build and check everything, then print the tag/upload commands instead of running them.
    [switch]$DryRun,
    # Publish from a branch other than main (dry runs on a feature branch).
    [switch]$AllowBranch
)

# The fork's own release, built on this Mac (osx-arm64 only) until the fork releases through
# GitHub Actions: the client and launcher zips, manifest.json, launcher-fingerprint.json and
# rynth-navdata.json, published to the OpenACReleaseRepository in Directory.Build.props.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'publish-fork-release requires PowerShell 7 or newer.' }
if (-not $IsMacOS) { throw 'Fork releases are built on macOS (osx-arm64).' }

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Read the properties the same way publish-bin.ps1 does (several PropertyGroups are allowed).
[xml]$props = Get-Content -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$repository = [string]($props.Project.PropertyGroup | ForEach-Object { $_.OpenACReleaseRepository } | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '-fork\.\d+$') { throw "Version '$version' is not a fork version (x.y.z-fork.n)." }
if ([string]::IsNullOrWhiteSpace($repository)) { throw 'Directory.Build.props has no OpenACReleaseRepository.' }

if ($AllowBranch -and -not $DryRun) { throw '-AllowBranch is for dry runs only; publish from main.' }

$status = & git -C $RepoRoot status --porcelain
if ($LASTEXITCODE) { throw 'git status failed.' }
if ($status) { throw 'The working tree has changes; commit or stash them first.' }
$branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD)
if ($LASTEXITCODE) { throw 'git rev-parse failed.' }
$branch = $branch.Trim()
if ($branch -ne 'main' -and -not $AllowBranch) { throw "On '$branch'; fork releases come from main (or pass -AllowBranch for a dry run)." }
$tag = "v$version"

if (-not $DryRun) {
    # Everything that can fail cheaply is checked before the build or any side effect.
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The gh CLI is not installed.' }
    & gh auth status *> $null
    if ($LASTEXITCODE) { throw 'gh is not logged in (gh auth status failed).' }
    & git -C $RepoRoot remote get-url origin *> $null
    if ($LASTEXITCODE) { throw "This checkout has no 'origin' remote." }
    if (& git -C $RepoRoot tag --list $tag) { throw "Tag $tag already exists locally." }
    $remoteTag = & git -C $RepoRoot ls-remote --tags origin "refs/tags/$tag"
    if ($LASTEXITCODE) { throw 'git ls-remote against origin failed.' }
    if ($remoteTag) { throw "Tag $tag already exists on origin." }
    $head = (& git -C $RepoRoot rev-parse HEAD).Trim()
    $originMain = (& git -C $RepoRoot rev-parse origin/main)
    if ($LASTEXITCODE) { throw 'git rev-parse origin/main failed.' }
    if ($originMain.Trim() -ne $head) { throw 'HEAD is not origin/main; push main first.' }
}

& (Join-Path $PSScriptRoot 'publish-bin.ps1') -MacOnly
if ($LASTEXITCODE) { throw 'publish-bin failed.' }

$bin = Join-Path $RepoRoot 'bin'
$files = @('client-osx-arm64.zip', 'launcher-osx-arm64.zip', 'manifest.json', 'launcher-fingerprint.json', 'rynth-navdata.json') |
    ForEach-Object { Join-Path $bin $_ }
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Release file '$file' is missing." }
}

# No --prerelease; --latest keeps the launcher's releases/latest/download route on this release.
$notes = "OpenAC fork release $version (macOS Apple silicon only). The launcher installs the RynthNav navdata pinned in rynth-navdata.json."
$commands = @(
    "git -C `"$RepoRoot`" tag $tag",
    "git -C `"$RepoRoot`" push origin $tag",
    "gh release create $tag -R $repository --title `"OpenAC $version`" --notes `"$notes`" --latest $($files -join ' ')"
)
if ($DryRun) {
    Write-Host 'Dry run: the release would be published with:' -ForegroundColor Cyan
    $commands | ForEach-Object { Write-Host "  $_" }
    return
}

& git -C $RepoRoot tag $tag
if ($LASTEXITCODE) { throw "git tag $tag failed." }
& git -C $RepoRoot push origin $tag
if ($LASTEXITCODE) {
    & git -C $RepoRoot tag -d $tag
    throw "git push of $tag failed; the local tag was deleted."
}
& gh release create $tag -R $repository --title "OpenAC $version" --notes $notes --latest @files
if ($LASTEXITCODE) { throw 'gh release create failed; the tag is pushed, re-run gh release create by hand.' }
Write-Host "Published $tag to https://github.com/$repository/releases/tag/$tag" -ForegroundColor Green
