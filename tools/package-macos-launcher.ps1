[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$ZipPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7 -or -not $IsMacOS) {
    throw 'package-macos-launcher requires PowerShell 7 on macOS.'
}

$publish = [IO.Path]::GetFullPath($PublishDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$zip = [IO.Path]::GetFullPath($ZipPath)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function Get-OpenACProperty([string]$Name) {
    $value = & dotnet msbuild (Join-Path $repoRoot 'src/AcDream.App/AcDream.App.csproj') "-getProperty:$Name"
    if ($LASTEXITCODE -or [string]::IsNullOrWhiteSpace($value)) { throw "Could not read $Name from the build." }
    return ([string]($value -join "`n")).Trim()
}
# Each product line ships its own bundle so OpenAC Gateway and OpenAC Gateway Rynth install side by side.
$product = Get-OpenACProperty 'OpenACProduct'
$bundleIdentifier = Get-OpenACProperty 'OpenACBundleIdentifier'
$bundleName = "$product.app"
if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
    throw "Published launcher directory '$publish' does not exist."
}

$launcher = Join-Path $publish 'acdream-launcher'
$bake = Join-Path $publish 'acdream-bake'
foreach ($required in @($launcher, $bake)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Published launcher output is missing '$required'."
    }
}

$bundle = Join-Path $output $bundleName
if (Test-Path -LiteralPath $bundle) {
    Remove-Item -LiteralPath $bundle -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

$contents = Join-Path $bundle 'Contents'
$macos = Join-Path $contents 'MacOS'
$resources = Join-Path $contents 'Resources'
New-Item -ItemType Directory -Path $macos -Force | Out-Null
New-Item -ItemType Directory -Path $resources -Force | Out-Null

# PublishSingleFile keeps the launcher and bake executable-shaped. Preserve the
# complete publish directory in case a future Avalonia runtime emits a native
# sidecar; every entry remains inside Contents/MacOS where it is part of the
# signed application payload.
Get-ChildItem -LiteralPath $publish -Force | ForEach-Object {
    if ($_.Extension -ne '.pdb') {
        Copy-Item -LiteralPath $_.FullName -Destination $macos -Recurse -Force
    }
}
& /bin/chmod 755 (Join-Path $macos 'acdream-launcher') (Join-Path $macos 'acdream-bake')
if ($LASTEXITCODE -ne 0) { throw 'chmod failed for a macOS launcher executable.' }

$iconSources = @{
    'icon_16x16.png' = 'acdream-launcher-16.png'
    'icon_16x16@2x.png' = 'acdream-launcher-32.png'
    'icon_32x32.png' = 'acdream-launcher-32.png'
    'icon_32x32@2x.png' = 'acdream-launcher-64.png'
    'icon_128x128.png' = 'acdream-launcher-128.png'
    'icon_128x128@2x.png' = 'acdream-launcher-256.png'
    'icon_256x256.png' = 'acdream-launcher-256.png'
    'icon_256x256@2x.png' = 'acdream-launcher-512.png'
    'icon_512x512.png' = 'acdream-launcher-512.png'
    'icon_512x512@2x.png' = 'acdream-launcher-1024.png'
}
$iconSet = Join-Path $output 'OpenAC.iconset'
if (Test-Path -LiteralPath $iconSet) {
    Remove-Item -LiteralPath $iconSet -Recurse -Force
}
New-Item -ItemType Directory -Path $iconSet -Force | Out-Null
foreach ($entry in $iconSources.GetEnumerator()) {
    $source = Join-Path $repoRoot (Join-Path 'assets/icons' $entry.Value)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Launcher icon source '$source' is missing."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $iconSet $entry.Key)
}
$icon = Join-Path $resources 'OpenAC.icns'
& /usr/bin/iconutil --convert icns --output $icon $iconSet
if ($LASTEXITCODE -ne 0) { throw 'iconutil could not create OpenAC.icns.' }
Remove-Item -LiteralPath $iconSet -Recurse -Force

$plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key>
  <string>$product</string>
  <key>CFBundleExecutable</key>
  <string>acdream-launcher</string>
  <key>CFBundleIconFile</key>
  <string>OpenAC.icns</string>
  <key>CFBundleIdentifier</key>
  <string>$bundleIdentifier</string>
  <key>CFBundleName</key>
  <string>$product</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$Version</string>
  <key>CFBundleVersion</key>
  <string>$Version</string>
  <key>LSMinimumSystemVersion</key>
  <string>14.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
"@
[IO.File]::WriteAllText(
    (Join-Path $contents 'Info.plist'),
    $plist,
    [Text.UTF8Encoding]::new($false))

# The graphical client uses mach_absolute_time for frame pacing on Apple
# silicon. The valid reason declares elapsed-time calculations for timers;
# none of that timing information is used for device fingerprinting.
$privacyManifest = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>NSPrivacyAccessedAPITypes</key>
  <array>
    <dict>
      <key>NSPrivacyAccessedAPIType</key>
      <string>NSPrivacyAccessedAPICategorySystemBootTime</string>
      <key>NSPrivacyAccessedAPITypeReasons</key>
      <array>
        <string>35F9.1</string>
      </array>
    </dict>
  </array>
</dict>
</plist>
"@
[IO.File]::WriteAllText(
    (Join-Path $resources 'PrivacyInfo.xcprivacy'),
    $privacyManifest,
    [Text.UTF8Encoding]::new($false))

& /usr/bin/plutil -lint (Join-Path $contents 'Info.plist')
if ($LASTEXITCODE -ne 0) { throw "$bundleName Info.plist did not validate." }
foreach ($required in @(
    (Join-Path $macos 'acdream-launcher'),
    (Join-Path $macos 'acdream-bake'),
    (Join-Path $resources 'OpenAC.icns'),
    (Join-Path $resources 'PrivacyInfo.xcprivacy'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "$bundleName is missing '$required'."
    }
}

# Contents/MacOS is the bundle's code folder: codesign refuses a bundle with a
# plain file there ("code object is not signed at all ... In subcomponent"),
# and it would do so only here, on a Mac. Name any such file up front.
$notCode = foreach ($entry in Get-ChildItem -LiteralPath $macos -File -Recurse | Sort-Object FullName) {
    $kind = (& /usr/bin/file -b $entry.FullName).Trim()
    if ($LASTEXITCODE -ne 0) { throw "file could not inspect '$($entry.FullName)'." }
    if (-not $kind.StartsWith('Mach-O', [StringComparison]::Ordinal)) { $entry.FullName }
}
if (@($notCode).Count -gt 0) {
    throw "$bundleName/Contents/MacOS may hold only code, but the launcher publish output put these there: $(@($notCode) -join ', ')."
}

# Sign every executable nested in the bundle before signing the bundle itself.
# This is deliberately ad-hoc: CI has no Apple Developer certificate, yet
# amfid still requires a valid local signature for the renamed app hosts.
foreach ($candidate in Get-ChildItem -LiteralPath $macos -File -Recurse | Sort-Object FullName) {
    $kind = (& /usr/bin/file -b $candidate.FullName).Trim()
    if ($LASTEXITCODE -ne 0) { throw "file could not inspect '$($candidate.FullName)'." }
    if (-not $kind.StartsWith('Mach-O', [StringComparison]::Ordinal)) { continue }
    & /usr/bin/codesign --force --sign - --timestamp=none $candidate.FullName
    if ($LASTEXITCODE -ne 0) { throw "Could not ad-hoc sign '$($candidate.FullName)'." }
    & /usr/bin/codesign --verify --strict $candidate.FullName
    if ($LASTEXITCODE -ne 0) { throw "Nested signature validation failed for '$($candidate.FullName)'." }
}
& /usr/bin/codesign --force --sign - --timestamp=none $bundle
if ($LASTEXITCODE -ne 0) { throw "Could not ad-hoc sign $bundleName." }
& /usr/bin/codesign --verify --deep --strict $bundle
if ($LASTEXITCODE -ne 0) { throw "$bundleName signature validation failed." }

New-Item -ItemType Directory -Path (Split-Path -Parent $zip) -Force | Out-Null
# This macOS stamps com.apple.provenance on every file; --sequesterRsrc would store it as
# __MACOSX/ AppleDouble entries that the bundle check below rejects. The signatures are embedded
# in the Mach-O files and _CodeSignature, so extended attributes are not needed.
& /usr/bin/ditto -c -k --norsrc --noextattr --keepParent $bundle $zip
if ($LASTEXITCODE -ne 0) { throw "ditto could not archive $bundleName." }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if ($archive.Entries.Count -eq 0 -or @($archive.Entries | Where-Object {
            -not $_.FullName.StartsWith("$bundleName/", [StringComparison]::Ordinal)
        }).Count -ne 0) {
        throw "The macOS launcher ZIP must contain only the $bundleName bundle."
    }
} finally {
    $archive.Dispose()
}

Write-Host "Created macOS launcher bundle: $bundle"
Write-Host "Created macOS launcher archive: $zip"
