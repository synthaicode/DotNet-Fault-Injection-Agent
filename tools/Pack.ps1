#Requires -Version 7.0
[CmdletBinding()]
param([string] $Version, [switch] $CandidateOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$arguments = @('pack', (Join-Path $root 'src/Tool/SourceMap.csproj'), '-c', 'Release',
    '-o', (Join-Path $root 'artifacts/packages'), '--nologo')
if (-not $Version) {
    [xml] $project = Get-Content -Raw (Join-Path $root 'src/Tool/SourceMap.csproj')
    $Version = [string]$project.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid package version.' }
$arguments += "-p:PackageVersion=$Version"
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'NuGet package creation failed.' }
if (-not $CandidateOnly) {
    & (Join-Path $PSScriptRoot 'Scan-Package.ps1') -Package (Join-Path $root "artifacts/packages/DotNetFaultInjectionAgent.Tool.$Version.nupkg")
}
