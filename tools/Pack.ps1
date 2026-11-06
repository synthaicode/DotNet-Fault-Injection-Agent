#Requires -Version 7.0
[CmdletBinding()]
param([string] $Version, [switch] $CandidateOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$arguments = @('pack', (Join-Path $root 'src/Tool/SourceMap.csproj'), '-c', 'Release',
    '-o', (Join-Path $root 'artifacts/packages'), '--nologo')
if ($Version) { $arguments += "-p:PackageVersion=$Version" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'NuGet package creation failed.' }
if (-not $CandidateOnly) {
    if (-not $Version) { $Version = '0.1.0-preview.4' }
    & (Join-Path $PSScriptRoot 'Scan-Package.ps1') -Package (Join-Path $root "artifacts/packages/DotNetFaultInjectionAgent.Tool.$Version.nupkg")
}
