#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Sample,
    [string] $Image = 'dotnet-fault-injection-runtime:local',
    [switch] $ConditionMismatch
)
$ErrorActionPreference = 'Stop'
$samplePath = (Resolve-Path -LiteralPath $Sample).Path
$output = Join-Path $samplePath ('evidence/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $samplePath 'plan.json') -Destination $output
$condition = if ($ConditionMismatch) { 'disabled' } else { 'enabled' }
$arguments = @('run','--rm',
    '--mount', "type=bind,source=$(Join-Path $samplePath 'target'),target=/app,readonly",
    '--mount', "type=bind,source=$(Join-Path $samplePath 'tools/runtime'),target=/profiler,readonly",
    '--mount', "type=bind,source=$output,target=/test", '-w','/app')
foreach ($value in @(
    'CORECLR_ENABLE_PROFILING=1',
    'CORECLR_PROFILER={324F817A-7420-4E6D-B3C1-143FBED6D855}',
    'CORECLR_PROFILER_PATH=/profiler/libInstrumentationEngine.so',
    'MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent=/profiler/Rewrite.xml',
    'MicrosoftInstrumentationEngine_DisableCodeSignatureValidation=1',
    'DOTNET_ADDITIONAL_DEPS=/profiler/DotNetFaultInjectionAgent.Runtime.deps.json',
    'DOTNET_FAULT_INJECTION_PLAN=/test/plan.json', "CHAOS_TEST_CASE=$condition"
)) { $arguments += @('-e',$value) }
$arguments += @($Image,'timeout','30s','dotnet','Probe.dll','/test/files','12')
$lines = @(& docker @arguments 2>&1 | ForEach-Object ToString)
$exitCode = $LASTEXITCODE
$lines | Set-Content -LiteralPath (Join-Path $output 'process.log') -Encoding utf8NoBOM
$firings = @($lines | Where-Object { $_ -match '^\{"event":"chaos.fired"' }).Count
[pscustomobject]@{ ExitCode=$exitCode; Firings=$firings; Evidence=$output; Output=($lines -join "`n") }
