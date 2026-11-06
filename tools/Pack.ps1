#Requires -Version 7.0
[CmdletBinding()]
param([string] $Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$arguments = @('pack', (Join-Path $root 'src/Tool/SourceMap.csproj'), '-c', 'Release',
    '-o', (Join-Path $root 'artifacts/packages'), '--nologo')
if ($Version) { $arguments += "-p:PackageVersion=$Version" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'NuGet package creation failed.' }
