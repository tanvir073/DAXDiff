#Requires -Version 7
<#
.SYNOPSIS
    Restores, builds, tests and publishes DAXDiff (Release, win-x64, framework-dependent).
#>
param(
    [string]$Configuration = 'Release',
    [string]$Output = (Join-Path $PSScriptRoot '..' 'publish')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

dotnet restore "$root/DaxDiff.sln"
dotnet build "$root/DaxDiff.sln" -c $Configuration --no-restore
dotnet test "$root/tests/DaxDiff.Tests.Unit" -c $Configuration --no-build
dotnet publish "$root/src/DaxDiff.Desktop/DaxDiff.Desktop.csproj" -c $Configuration -o $Output --no-restore

Write-Host "Published to $Output"
