#Requires -Version 7
<#
.SYNOPSIS
    Builds the DAXDiff Windows downloads in ./dist: tests, a self-contained win-x64 publish (no .NET needed on the
    target), an MSI installer and a portable zip of the same files.
#>
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$app = Join-Path $root 'publish' 'app'
$dist = Join-Path $root 'dist'

$version = (dotnet msbuild "$root/src/DaxDiff.Domain/DaxDiff.Domain.csproj" -getProperty:Version).Trim()
if ($LASTEXITCODE) { throw 'Could not read the version.' }

dotnet test "$root/tests/DaxDiff.Tests.Unit" -c $Configuration
if ($LASTEXITCODE) { throw 'Tests failed.' }

foreach ($dir in $app, $dist) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
dotnet publish "$root/src/DaxDiff.Desktop/DaxDiff.Desktop.csproj" -c $Configuration -r win-x64 --self-contained true -o $app
if ($LASTEXITCODE) { throw 'Publish failed.' }

dotnet build "$root/installer/DaxDiff.Installer.wixproj" -c $Configuration -p:AppDir=$app -o $dist
if ($LASTEXITCODE) { throw 'Installer build failed.' }

# Portable copy for users who cannot run an MSI (no administrator rights): unzip anywhere and start DAXDiff.exe.
$zip = Join-Path $dist "DAXDiff-$version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip

Get-ChildItem $dist -Include *.msi, *.zip -Recurse | ForEach-Object { Write-Host "Download: $($_.FullName)" }
