<#
.SYNOPSIS
    Builds artifacts\BirthdayChecker-Setup-<version>.exe: self-contained publish + Inno Setup.
.NOTES
    Needs Inno Setup 6 (winget install JRSoftware.InnoSetup). Version comes from Directory.Build.props.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = @(([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "No valid <Version> in Directory.Build.props: '$version'" }
$publish = Join-Path $root 'artifacts\publish'

# Fresh folder: files left over from an older build would be packed into the installer.
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
# One exe: the install folder's root holds only it, the uninstaller and the user's files.
dotnet publish "$root\src\BirthdayChecker.App" -c Release -r win-x64 --self-contained true -o $publish `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$version" "/DSourceDir=$publish" (Join-Path $PSScriptRoot 'BirthdayChecker.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Get-Item (Join-Path $root "artifacts\BirthdayChecker-Setup-$version.exe")
