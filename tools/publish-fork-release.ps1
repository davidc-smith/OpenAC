[CmdletBinding()]
param(
    # Build and check everything, then print the tag/upload commands instead of running them.
    [switch]$DryRun,
    # Publish from a branch other than the product's release branch (dry runs on a feature branch).
    [switch]$AllowBranch
)

# The fork's own release, tagged <prefix><version> from the product's branch, built on this Mac
# (osx-arm64 only) until the fork releases through
# GitHub Actions: the client and launcher zips, manifest.json, launcher-fingerprint.json and
# rynth-navdata.json, published to the OpenACReleaseRepository in Directory.Build.props.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Check $LASTEXITCODE by hand so the custom messages below win over pwsh's own native-command error.
$PSNativeCommandUseErrorActionPreference = $false

if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'publish-fork-release requires PowerShell 7 or newer.' }
if (-not $IsMacOS) { throw 'Fork releases are built on macOS (osx-arm64).' }

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Read the properties the same way publish-bin.ps1 does (several PropertyGroups are allowed).
function Get-OpenACProperty([string]$Name) {
    $value = & dotnet msbuild (Join-Path $RepoRoot 'src/AcDream.App/AcDream.App.csproj') "-getProperty:$Name"
    if ($LASTEXITCODE -or [string]::IsNullOrWhiteSpace($value)) { throw "Could not read $Name from the build." }
    return ([string]($value -join "`n")).Trim()
}
[xml]$props = Get-Content -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props') -Raw
$version = Get-OpenACProperty 'OpenACReleaseVersion'
$product = Get-OpenACProperty 'OpenACProduct'
$tagPrefix = Get-OpenACProperty 'OpenACReleaseTagPrefix'
$label = Get-OpenACProperty 'OpenACReleaseLabel'
# Each product releases from the branch line that owns it (BRANCHING.md).
$releaseBranch = switch ($label) {
    'gateway' { 'main' }
    'rynth' { 'rynth' }
    default { throw "No release branch is known for release label '$label'." }
}
if ($tagPrefix -notmatch '^[a-z]+(-[a-z]+)*-v$') { throw "Tag prefix '$tagPrefix' is not '<product>-v'." }
$repository = [string]($props.Project.PropertyGroup | ForEach-Object { $_.OpenACReleaseRepository } | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '-(gateway|rynth)\.\d+$') { throw "Version '$version' is not an OpenAC Gateway release version (x.y.z-gateway.n or x.y.z-rynth.n)." }
if ([string]::IsNullOrWhiteSpace($repository)) { throw 'Directory.Build.props has no OpenACReleaseRepository.' }

if ($AllowBranch -and -not $DryRun) { throw "-AllowBranch is for dry runs only; publish from $releaseBranch." }

$status = & git -C $RepoRoot status --porcelain
if ($LASTEXITCODE) { throw 'git status failed.' }
if ($status) { throw 'The working tree has changes; commit or stash them first.' }
$branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD)
if ($LASTEXITCODE) { throw 'git rev-parse failed.' }
$branch = $branch.Trim()
if ($branch -ne $releaseBranch -and -not $AllowBranch) { throw "On '$branch'; $product releases come from $releaseBranch (or pass -AllowBranch for a dry run)." }
$tag = "$tagPrefix$version"

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
    & git -C $RepoRoot fetch origin $releaseBranch
    if ($LASTEXITCODE) { throw "git fetch origin $releaseBranch failed." }
    $head = (& git -C $RepoRoot rev-parse HEAD)
    if ($LASTEXITCODE) { throw 'git rev-parse HEAD failed.' }
    $head = $head.Trim()
    $originMain = (& git -C $RepoRoot rev-parse "origin/$releaseBranch")
    if ($LASTEXITCODE) { throw "git rev-parse origin/$releaseBranch failed." }
    if ($originMain.Trim() -ne $head) { throw "HEAD is not origin/$releaseBranch; push $releaseBranch first." }
}

$cleanupFailed = $false
try {
    & (Join-Path $PSScriptRoot 'publish-bin.ps1') -MacOnly
    if ($LASTEXITCODE) { throw 'publish-bin failed.' }
}
finally {
    # Restoring packages while publishing rewrites the packages.*.lock.json files for this RID (and
    # may create new ones); put them back so the tree is as it was, also when publish-bin fails.
    & git -C $RepoRoot checkout -- '*packages.*.lock.json'
    if ($LASTEXITCODE) { Write-Warning 'git checkout of the lock files failed.'; $cleanupFailed = $true }
    $strayLocks = @(& git -C $RepoRoot ls-files --others --exclude-standard -- '*packages.*.lock.json')
    if ($LASTEXITCODE) { Write-Warning 'git ls-files failed.'; $cleanupFailed = $true }
    foreach ($lock in $strayLocks) {
        try { Remove-Item -LiteralPath (Join-Path $RepoRoot $lock) -Force -ErrorAction Stop }
        catch { Write-Warning "Could not remove $lock`: $($_.Exception.Message)"; $cleanupFailed = $true }
    }
    if (-not $cleanupFailed) {
        Write-Host "Restored the lock files publish-bin touched; removed $($strayLocks.Count) untracked one(s)."
    }
}
if ($cleanupFailed) { throw 'Restoring the lock files failed.' }

$bin = Join-Path $RepoRoot 'bin'
$files = @('client-osx-arm64.zip', 'launcher-osx-arm64.zip', 'manifest.json', 'launcher-fingerprint.json', 'rynth-navdata.json') |
    ForEach-Object { Join-Path $bin $_ }
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Release file '$file' is missing." }
}

# Marked not-latest on purpose (--latest=false; gh otherwise marks it latest by date/version):
# both products release here and each launcher finds its own newest release by tag prefix on
# the releases feed, never through releases/latest.
$notes = "$product release $version (macOS Apple silicon only). The launcher installs the RynthNav navdata pinned in rynth-navdata.json."
$commands = @(
    "git -C `"$RepoRoot`" tag $tag",
    "git -C `"$RepoRoot`" push origin $tag",
    "gh release create $tag -R $repository --title `"$product $version`" --notes `"$notes`" --latest=false $($files -join ' ')"
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
& gh release create $tag -R $repository --title "$product $version" --notes $notes --latest=false @files
if ($LASTEXITCODE) { throw 'gh release create failed; the tag is pushed, re-run gh release create by hand.' }
Write-Host "Published $tag to https://github.com/$repository/releases/tag/$tag" -ForegroundColor Green
