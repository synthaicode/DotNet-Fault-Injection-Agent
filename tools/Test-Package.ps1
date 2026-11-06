#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Package, [string] $SourceUrl)
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
    foreach ($required in @('README.md','LICENSE','AI-plan-guide.md','schemas/injection-plan.v3.schema.json',
        'licenses/Roslyn.LICENSE.txt','licenses/Roslyn.ThirdPartyNotices.rtf',
        'tools/net10.0/any/DotNetFaultInjectionAgent.Tool.dll',
        'tools/net10.0/any/Planner.dll', 'tools/net10.0/any/Microsoft.CodeAnalysis.dll',
        'tools/net10.0/any/Microsoft.CodeAnalysis.CSharp.dll','tools/net10.0/any/DotnetToolSettings.xml')) {
        if ($required -notin $names) { throw "Package entry missing: $required" }
    }
    foreach ($file in @('Rewrite.dll','Rewrite.xml','MicrosoftInstrumentationEngine_x64.dll','Invoke-FaultInjection.ps1','manifest.json','CLRInstrumentationEngine.LICENSE.txt','CLRInstrumentationEngine.NOTICES.txt','nlohmann-json.LICENSE.txt')) {
        if ("tools/net10.0/any/runtime/win-x64/$file" -notin $names) { throw "Runtime payload missing: $file" }
    }
    if (($names | Where-Object { $_ -notlike 'tools/net10.0/any/runtime/win-x64/*' -and $_ -notlike 'tools/net10.0/any/runtime/linux-x64/*' }) -match '(^|/)(tests|samples|work)/|(^|/)(Probe|SqlTarget|CoreFileTarget|OrderAgentProbe|DotNetFaultInjectionAgent.Runtime)\.(dll|exe)$|\.so$|(^|/)Rewrite\.dll$') {
        throw 'Package contains a fixture, runtime or native injection component.'
    }
    foreach ($file in @('libInstrumentationEngine.so','libDotNetFaultInjectionAgent.so','Rewrite.xml','DotNetFaultInjectionAgent.Runtime.dll','DotNetFaultInjectionAgent.Runtime.deps.json','Invoke-FaultInjection.sh','manifest.json','CLRInstrumentationEngine.LICENSE.txt','CLRInstrumentationEngine.NOTICES.txt','libxml2.LICENSE.txt','utfcpp.LICENSE.txt')) {
        if ("tools/net10.0/any/runtime/linux-x64/$file" -notin $names) { throw "Linux payload missing: $file" }
    }
    $manifest = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
    if ($manifest.Count -ne 1) { throw 'Expected one NuGet manifest.' }
    $reader = [IO.StreamReader]::new($manifest[0].Open())
    try { [xml] $metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $id = [string]$metadata.package.metadata.id
    $version = [string]$metadata.package.metadata.version
    if ($id -ne 'DotNetFaultInjectionAgent.Tool') { throw "Unexpected package ID: $id" }
    if ($metadata.package.metadata.license.InnerText -ne 'MIT' -or $metadata.package.metadata.license.type -ne 'expression') { throw 'MIT SPDX metadata is missing.' }
    if ($metadata.package.metadata.readme -ne 'README.md') { throw 'NuGet README metadata is missing.' }
} finally { $archive.Dispose() }
$escapedFeed = [Security.SecurityElement]::Escape($(if ($SourceUrl) { $SourceUrl } else { $feed }))
"<configuration><packageSources><clear/><add key=`"packagefeed`" value=`"$escapedFeed`"/></packageSources></configuration>" |
    Set-Content (Join-Path $output 'NuGet.Config') -Encoding utf8
$originalCache = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = Join-Path $output 'cache'
    & dotnet tool install $id --version $version --tool-path $toolPath --configfile (Join-Path $output 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Clean tool installation failed.' }
} finally { $env:NUGET_PACKAGES = $originalCache }
$installedArchive = @(Get-ChildItem -LiteralPath $toolPath -Force -Recurse -File -Filter "$($id.ToLowerInvariant()).$version.nupkg")
if ($installedArchive.Count -ne 1 -or (Get-FileHash $installedArchive[0].FullName).Hash -ne (Get-FileHash $packagePath).Hash) {
    throw 'The package fetched from the selected feed differs from the tested package.'
}
$command = Join-Path $toolPath $(if ($IsWindows) { 'dotnet-fault-injection.exe' } else { 'dotnet-fault-injection' })
$runtimeDirectory = Join-Path $output 'exported-runtime'
$runtimeList = & $command runtime list
if ($LASTEXITCODE -ne 0 -or ('win-x64' -notin ($runtimeList | ConvertFrom-Json).bundled -or 'linux-x64' -notin ($runtimeList | ConvertFrom-Json).bundled)) { throw 'Runtime discovery failed.' }
& $command runtime export --output $runtimeDirectory
if ($LASTEXITCODE -ne 0) { throw 'Runtime export failed.' }
& $command runtime export --output $runtimeDirectory 2>$null
if ($LASTEXITCODE -ne 1) { throw 'Runtime overwrite was not rejected.' }
if ($IsWindows) {
    foreach ($fixture in @('SqlTarget','CoreFileTarget')) {
        $fixtureRoot = Join-Path $root "tests/Fixtures/$fixture"
        & dotnet build $fixtureRoot -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Runtime test fixture build failed.' }
        $framework = if ($fixture -eq 'SqlTarget') { 'net48' } else { 'net10.0' }
        $binary = Join-Path $fixtureRoot "bin/Release/$framework/$fixture.$(if ($fixture -eq 'SqlTarget') {'exe'} else {'dll'})"
        $source = Join-Path $fixtureRoot 'Program.cs'
        $references = if ($fixture -eq 'SqlTarget') {
            "${env:ProgramFiles(x86)}/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8"
        } else {
            $sdkRoot = Split-Path -Parent (Get-Command dotnet).Source
            $pack = Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Ref') -Directory |
                Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
            Join-Path $pack.FullName 'ref/net10.0'
        }
        $candidates = & $command windows-fixture list $source $binary $references
        if ($LASTEXITCODE -ne 0) { throw 'Fixture candidate listing failed.' }
        $candidate = @($candidates | Where-Object { $_ -match '^\d+: Program.WriteSelected\(' })
        if ($candidate.Count -ne 1) { throw 'Expected one selected file-write site.' }
        $selection = ($candidate[0] -split ':',2)[0]
        $planPath = Join-Path $output "$fixture-plan.json"
        & $command windows-fixture plan $source $binary $references $selection $planPath
        if ($LASTEXITCODE -ne 0) { throw 'Fixture plan creation failed.' }
        $plan = Get-Content -Raw $planPath | ConvertFrom-Json
        $plan.path = Join-Path $output "$fixture-test.txt"
        $plan | ConvertTo-Json -Depth 10 | Set-Content $planPath -Encoding utf8
        & $command windows-fixture verify $source $binary $references $planPath
        if ($LASTEXITCODE -ne 0) { throw 'Fixture plan verification failed.' }
        $exe = Join-Path $fixtureRoot "bin/Release/$framework/$fixture.exe"
        $targetArguments = if ($fixture -eq 'SqlTarget') { @('--file',$plan.path) } else { @($plan.path) }
        $result = & (Join-Path $runtimeDirectory 'Invoke-FaultInjection.ps1') -Target $exe -Plan $planPath -TargetArguments $targetArguments
        $result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output "$fixture-result.json") -Encoding utf8
        if ($result.ExitCode -ne 0 -or -not $result.PlanAccepted -or -not $result.IlRewritten -or
            $result.Firings -ne 1 -or -not $result.BinaryUnchanged -or $result.Output -notmatch 'IOException' -or
            -not (Test-Path -LiteralPath $plan.path)) { throw 'Exported runtime did not inject once and permit the later file write.' }
    }
}
$guide = (& $command guide) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Installed CLI failed guide.' }
$expectedGuide = [IO.File]::ReadAllText((Join-Path $root 'src/Tool/AI-plan-guide.md')).Replace("`r`n", "`n").TrimEnd()
if ($guide.TrimEnd() -cne $expectedGuide) { throw 'Installed guide differs from repository guide.' }
foreach ($helpArguments in @(@(), @('--help'))) {
    $helpText = (& $command @helpArguments) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $helpText -notmatch 'dotnet-fault-injection guide') { throw 'Guide discovery failed.' }
}
& $command guide unexpected 2>$null
if ($LASTEXITCODE -ne 1) { throw 'Invalid guide arguments were not rejected.' }
$targets = & $command capabilities
if ($LASTEXITCODE -ne 0 -or @((($targets -join "`n") | ConvertFrom-Json).targets).Count -ne 3) { throw "Target capabilities failed." }
$capabilities = & $command dotnet capabilities
if ($LASTEXITCODE -ne 0 -or ($capabilities | ConvertFrom-Json).planVersion -ne 3) { throw 'Installed CLI failed capabilities.' }
$probe = Join-Path $root 'tests/PlanDrivenInjection/Probe'
& dotnet build $probe -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$source = Join-Path $probe 'Program.cs'
$binary = Join-Path $probe 'bin/Release/net8.0/Probe.dll'
$line = (Select-String -LiteralPath $source -SimpleMatch 'File.WriteAllText(path, "selected")').LineNumber
$plan = Join-Path $output 'plan.json'
& $command dotnet plan $binary $source $line $plan
if ($LASTEXITCODE -ne 0) { throw 'Installed CLI failed to create a plan.' }
& $command dotnet verify $binary $source $line $plan
if ($LASTEXITCODE -ne 0) { throw 'Installed CLI failed plan verification.' }
if ($IsLinux) {
    $targetHash = (Get-FileHash $binary).Hash
    $lines = @(& bash (Join-Path $runtimeDirectory 'Invoke-FaultInjection.sh') --target $binary --plan $plan -- (Join-Path $output 'files') 12 2>&1 | ForEach-Object ToString)
    if ($LASTEXITCODE -ne 0) { throw 'Packaged Linux launcher failed.' }
    $lines | Set-Content (Join-Path $output 'linux-runtime.log') -Encoding utf8
    $rows = @($lines | Where-Object { $_ -like '{"index"*' } | ForEach-Object { $_ | ConvertFrom-Json })
    $events = @($lines | Where-Object { $_ -like '{"event":"chaos.fired"*' })
    if ($rows.Count -ne 12 -or @($rows | Where-Object error).Count -ne 1 -or $events.Count -ne 1 -or
        -not ($lines -match '^plan accepted:') -or -not ($lines -match '^IL rewritten:')) { throw 'Linux runtime did not inject exactly once.' }
    if ($targetHash -ne (Get-FileHash $binary).Hash) { throw 'Linux target binary changed.' }
}
$summary = [pscustomobject]@{ PackageId=$id; Version=$version; Sha256=(Get-FileHash $packagePath).Hash;
    ContentVerified=$true; CleanInstall=$true; RuntimeDirectory=$runtimeDirectory; GuideVerified=$true; Capabilities=$true; PlanCreated=$true; PlanVerified=$true; ToolDirectory=$toolPath }
$summary | ConvertTo-Json | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
$summary
