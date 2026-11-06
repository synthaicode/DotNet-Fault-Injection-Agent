#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $EngineSource)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$engineRoot = (Resolve-Path -LiteralPath $EngineSource).ProviderPath
$revision = & git -C $engineRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -ne '0055c6fce2fe16770f5908ef14cfd7f8a42c2fab') { throw 'Use the pinned CLRInstrumentationEngine source revision.' }
if (& git -C $engineRoot status --porcelain --untracked-files=no) { throw 'Engine tracked sources must be unmodified.' }
$output = Join-Path $root 'artifacts/runtime/win-x64'
New-Item -ItemType Directory -Force $output | Out-Null
foreach ($file in @('Rewrite.dll','Rewrite.xml')) {
    Copy-Item -LiteralPath (Join-Path $root "src/Native/Windows/bin/$file") -Destination $output -Force
}
# Only use the release output built from the checked source, never a Visual Studio installation DLL.
Copy-Item -LiteralPath (Join-Path $engineRoot 'bin/Release/x64/MicrosoftInstrumentationEngine_x64.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $engineRoot 'LICENSE') -Destination (Join-Path $output 'CLRInstrumentationEngine.LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $engineRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $output 'CLRInstrumentationEngine.NOTICES.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'tools/Invoke-FaultInjection.ps1') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $output 'DotNetFaultInjectionAgent.LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'docs/packaging/nlohmann-json.LICENSE.txt') -Destination $output -Force
$files = [ordered]@{}
foreach ($name in @('Rewrite.dll','Rewrite.xml','MicrosoftInstrumentationEngine_x64.dll',
    'Invoke-FaultInjection.ps1','CLRInstrumentationEngine.LICENSE.txt','CLRInstrumentationEngine.NOTICES.txt',
    'DotNetFaultInjectionAgent.LICENSE.txt','nlohmann-json.LICENSE.txt')) {
    $files[$name] = (Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash
}
[ordered]@{ rid='win-x64'; engineRevision=$revision; engineBuild='Release x64, SignType=None, SpectreMitigation=false'; files=$files } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'manifest.json') -Encoding utf8
