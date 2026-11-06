#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EngineSource,
    [Parameter(Mandatory)][string] $ClrHeaders,
    [Parameter(Mandatory)][string] $JsonHeaders
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows native build requires Windows.' }
$root = Split-Path -Parent $PSScriptRoot
$engineRoot = (Resolve-Path -LiteralPath $EngineSource).ProviderPath
$clrRoot = (Resolve-Path -LiteralPath $ClrHeaders).ProviderPath
$jsonRoot = (Resolve-Path -LiteralPath $JsonHeaders).ProviderPath
$revision = & git -C $engineRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -ne '0055c6fce2fe16770f5908ef14cfd7f8a42c2fab') { throw 'Use the pinned Engine source revision.' }
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts') | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ build tools, ATL and Windows SDK are required.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
$output = Join-Path $root 'src/Native/Windows/bin'
New-Item -ItemType Directory -Force $output | Out-Null
& dotnet restore (Join-Path $engineRoot 'NativeNuGetRestore.sln') --packages (Join-Path $engineRoot 'packages')
if ($LASTEXITCODE -ne 0) { throw 'Engine build dependency restore failed.' }
$batch = Join-Path $root 'artifacts/build-runtime.cmd'
@"
@echo off
call "$vcvars"
if errorlevel 1 exit /b 1
msbuild "$engineRoot/src/InstrumentationEngine/InstrumentationEngine.vcxproj" /m /p:Configuration=Release /p:Platform=x64 /p:SignType=None /p:SpectreMitigation=false /v:minimal
if errorlevel 1 exit /b 1
cd /d "$output"
cl /nologo /std:c++17 /LD /D_WINDLL /EHsc /DUNICODE /D_UNICODE /I"$engineRoot/src/InstrumentationEngine.Api" /I"$clrRoot" /I"$jsonRoot" "$root/src/Native/Windows/Rewrite.cpp" /Fe:Rewrite.dll ole32.lib mscoree.lib bcrypt.lib /link /EXPORT:DllGetClassObject
"@ | Set-Content $batch -Encoding utf8
& $env:ComSpec /d /c $batch
if ($LASTEXITCODE -ne 0) { throw 'Native runtime build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'src/Native/Windows/Rewrite.xml') -Destination $output -Force
& "$PSScriptRoot/Stage-WindowsRuntime.ps1" -EngineSource $engineRoot
