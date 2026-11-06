#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Package)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packagePath = (Resolve-Path -LiteralPath $Package).ProviderPath
$output = Join-Path $root ('artifacts/package-tests/' + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $output 'feed'
$toolPath = Join-Path $output 'tool'
New-Item -ItemType Directory -Force $feed | Out-Null
Copy-Item -LiteralPath $packagePath -Destination $feed
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $names = @($archive.Entries.FullName)
    foreach ($required in @('README.md','schemas/injection-plan.v3.schema.json',
        'tools/net10.0/any/DotNetFaultInjectionAgent.Tool.dll',
        'tools/net10.0/any/Planner.dll', 'tools/net10.0/any/Microsoft.CodeAnalysis.dll',
        'tools/net10.0/any/Microsoft.CodeAnalysis.CSharp.dll','tools/net10.0/any/DotnetToolSettings.xml')) {
        if ($required -notin $names) { throw "Package entry missing: $required" }
    }
    if ($names -match '(^|/)(tests|samples|work)/|(^|/)(Probe|SqlTarget|CoreFileTarget|OrderAgentProbe|DotNetFaultInjectionAgent.Runtime)\.(dll|exe)$|\.so$|(^|/)Rewrite\.dll$') {
        throw 'Package contains a fixture, runtime or native injection component.'
    }
    $manifest = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
    if ($manifest.Count -ne 1) { throw 'Expected one NuGet manifest.' }
    $reader = [IO.StreamReader]::new($manifest[0].Open())
    try { [xml] $metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $id = [string]$metadata.package.metadata.id
    $version = [string]$metadata.package.metadata.version
    if ($id -ne 'DotNetFaultInjectionAgent.Tool') { throw "Unexpected package ID: $id" }
    if ($metadata.package.metadata.readme -ne 'README.md') { throw 'NuGet README metadata is missing.' }
} finally { $archive.Dispose() }
$escapedFeed = [Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear/><add key=`"local`" value=`"$escapedFeed`"/></packageSources></configuration>" |
    Set-Content (Join-Path $output 'NuGet.Config') -Encoding utf8
$originalCache = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = Join-Path $output 'cache'
    & dotnet tool install $id --version $version --tool-path $toolPath --configfile (Join-Path $output 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Clean tool installation failed.' }
} finally { $env:NUGET_PACKAGES = $originalCache }
$command = Join-Path $toolPath $(if ($IsWindows) { 'dotnet-fault-injection.exe' } else { 'dotnet-fault-injection' })
$capabilities = & $command v3 capabilities
if ($LASTEXITCODE -ne 0 -or ($capabilities | ConvertFrom-Json).planVersion -ne 3) { throw 'Installed CLI failed capabilities.' }
$probe = Join-Path $root 'tests/PlanDrivenInjection/Probe'
& dotnet build $probe -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$source = Join-Path $probe 'Program.cs'
$binary = Join-Path $probe 'bin/Release/net8.0/Probe.dll'
$line = (Select-String -LiteralPath $source -SimpleMatch 'File.WriteAllText(path, "selected")').LineNumber
$plan = Join-Path $output 'plan.json'
& $command v3 plan $binary $source $line $plan
if ($LASTEXITCODE -ne 0) { throw 'Installed CLI failed to create a plan.' }
& $command v3 verify $binary $source $line $plan
if ($LASTEXITCODE -ne 0) { throw 'Installed CLI failed plan verification.' }
$summary = [pscustomobject]@{ PackageId=$id; Version=$version; Sha256=(Get-FileHash $packagePath).Hash;
    ContentVerified=$true; CleanInstall=$true; Capabilities=$true; PlanCreated=$true; PlanVerified=$true; ToolDirectory=$toolPath }
$summary | ConvertTo-Json | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
$summary
