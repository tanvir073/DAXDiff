#Requires -Version 7
<#
.SYNOPSIS
    Builds the DAXDiff installer: tests, a self-contained win-x64 publish (no .NET needed on the target) and an MSI in ./dist.
#>
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$app = Join-Path $root 'publish' 'app'
$dist = Join-Path $root 'dist'

dotnet test "$root/tests/DaxDiff.Tests.Unit" -c $Configuration
if ($LASTEXITCODE) { throw 'Tests failed.' }

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
dotnet publish "$root/src/DaxDiff.Desktop/DaxDiff.Desktop.csproj" -c $Configuration -r win-x64 --self-contained true -o $app
if ($LASTEXITCODE) { throw 'Publish failed.' }

dotnet build "$root/installer/DaxDiff.Installer.wixproj" -c $Configuration -p:AppDir=$app -o $dist
if ($LASTEXITCODE) { throw 'Installer build failed.' }

Get-ChildItem $dist -Filter *.msi | ForEach-Object { Write-Host "Installer: $($_.FullName)" }
