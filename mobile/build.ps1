<#
.SYNOPSIS
    Builds artifacts\BirthdayChecker-Mobile-<version>.apk: Release, signed with the release key.
.NOTES
    The key and its password live in the main repo folder, outside git (.gitignore): bdc-release.keystore,
    bdc-release.password. Android installs an update only with the same key — keep a copy of both elsewhere.
    Version comes from mobile\Directory.Build.props; the release tag is mobile-v<version>.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The main folder, also from a linked worktree (its .git is the common one).
$main = Split-Path (git -C $root rev-parse --path-format=absolute --git-common-dir) -Parent
$keystore = Join-Path $main 'bdc-release.keystore'
$password = Join-Path $main 'bdc-release.password'
foreach ($f in $keystore, $password) { if (-not (Test-Path $f)) { throw "Signing file not found: $f (restore it from the backup)" } }

$version = @(([xml](Get-Content "$PSScriptRoot\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "No valid <Version> in mobile\Directory.Build.props: '$version'" }
$publish = Join-Path $root 'artifacts\mobile'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
# From scratch: an incremental build keeps the -Signed.apk signed by another key (a plain Release publish uses the debug one).
foreach ($d in 'bin', 'obj') {
    $old = Join-Path $PSScriptRoot "BirthdayChecker.Mobile\$d\Release"
    if (Test-Path $old) { Remove-Item $old -Recurse -Force }
}
# env: — the build reads the password itself, so it shows up neither in the command line nor in logs.
# (file: would be simpler, but the signer gets that path unquoted and fails on the space in "_my soft".)
$env:BDC_KEYSTORE_PASS = (Get-Content $password -Raw).Trim()
try {
    dotnet publish "$PSScriptRoot\BirthdayChecker.Mobile" -f net10.0-android -c Release -o $publish `
        -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$keystore" -p:AndroidSigningKeyAlias=bdc `
        -p:AndroidSigningStorePass=env:BDC_KEYSTORE_PASS -p:AndroidSigningKeyPass=env:BDC_KEYSTORE_PASS
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
finally {
    Remove-Item Env:BDC_KEYSTORE_PASS
}

$apk = Join-Path $root "artifacts\BirthdayChecker-Mobile-$version.apk"
Copy-Item (Join-Path $publish 'com.zlatous.birthdaychecker-Signed.apk') $apk -Force
Get-Item $apk
