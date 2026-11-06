#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $OrderProject,
    [string] $OrderBinary,
    [string] $ToolDirectory,
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$samples = Join-Path $root 'samples'
if (-not $ToolDirectory) { $ToolDirectory = Join-Path $root '.tools' }
$ToolDirectory = (Resolve-Path -LiteralPath $ToolDirectory).ProviderPath
$tool = Join-Path $ToolDirectory 'dotnet-fault-injection.exe'
$windowsRuntime = Join-Path $root 'src/Native/Windows/bin'
$v3 = Join-Path $root 'tests/PlanDrivenInjection'
if (-not (Test-Path -LiteralPath $tool)) { throw 'Install DotNetFaultInjectionAgent.Tool into .tools before preparing samples.' }
if ([bool]$OrderProject -ne [bool]$OrderBinary) { throw 'Supply both OrderProject and OrderBinary.' }
foreach ($file in @('Rewrite.dll', 'Rewrite.xml', 'MicrosoftInstrumentationEngine_x64.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $windowsRuntime $file))) { throw "Build and assemble the Windows runtime first: $file" }
}
$net48 = "${env:ProgramFiles(x86)}/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8"
$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$pack = Get-ChildItem (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory |
    Where-Object Name -Match '^10\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $pack) { throw '.NET 10 reference assemblies are required.' }
$net10 = Join-Path $pack.FullName 'ref/net10.0'
foreach ($project in @('CoreFileTarget', 'SqlTarget', 'ProfilerTarget', 'SyncService')) {
    dotnet build (Join-Path $root "tests/Fixtures/$project") -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
dotnet build (Join-Path $root 'tests/OrderAgentProbe/Host') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Order host build failed.' }
dotnet build (Join-Path $root 'src/Planner') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Planner build failed.' }
dotnet publish (Join-Path $v3 'Probe') -c Release -o (Join-Path $v3 'Probe/bin/publish') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Probe publish failed.' }
dotnet build (Join-Path $root 'src/Runtime') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Runtime build failed.' }

function New-SampleLayout([string] $Name, [string] $Output, [string] $Source, [string] $Kind = 'WindowsPlan') {
    $directory = Join-Path $samples $Name
    if ((Test-Path -LiteralPath (Join-Path $directory 'plan.json')) -and -not $Force) { throw "Existing plan: $Name. Use -Force only to replace generated sample files." }
    $toolOutput = [IO.Path]::GetFullPath((Join-Path $directory 'tools'))
    $expectedParent = [IO.Path]::GetFullPath($directory).TrimEnd('\') + '\'
    if ($Force -and $Kind -in @('Observation','LinuxPlan') -and (Test-Path -LiteralPath $toolOutput)) {
        if (-not $toolOutput.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Tool output escaped the sample directory.' }
        Remove-Item -LiteralPath $toolOutput -Recurse -Force
    }
    if ($Force -and $Kind -eq 'WindowsFixed') {
        $store = Join-Path $toolOutput '.store'
        if (Test-Path -LiteralPath $store) {
            if (-not ([IO.Path]::GetFullPath($store)).StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Tool store escaped sample directory.' }
            Remove-Item -LiteralPath $store -Recurse -Force
        }
        $shim = Join-Path $toolOutput 'dotnet-fault-injection.exe'
        if (Test-Path -LiteralPath $shim) { Remove-Item -LiteralPath $shim }
    }
    foreach ($part in @('target', 'reference')) { New-Item -ItemType Directory -Force (Join-Path $directory $part) | Out-Null }
    if ($Kind -ne 'Observation') { New-Item -ItemType Directory -Force (Join-Path $directory 'tools/runtime') | Out-Null }
    Get-ChildItem -LiteralPath $Output -File | Where-Object Name -NotIn @('Rewrite.dll','Rewrite.xml','MicrosoftInstrumentationEngine_x64.dll') |
        Copy-Item -Destination (Join-Path $directory 'target')
    Get-ChildItem -LiteralPath $Output -Filter '*.pdb' -File | Copy-Item -Destination (Join-Path $directory 'reference')
    Copy-Item -LiteralPath $Source -Destination (Join-Path $directory 'reference/Program.cs')
    # The planning tool is distributed as a complete tool installation, not only its shim.
    if ($Kind -eq 'WindowsPlan') {
        Copy-Item -LiteralPath $tool -Destination (Join-Path $directory 'tools')
        Copy-Item -LiteralPath (Join-Path $ToolDirectory '.store') -Destination (Join-Path $directory 'tools') -Recurse -Force
    }
    if ($Kind -in @('WindowsPlan','WindowsFixed')) {
        Copy-Item -LiteralPath (Join-Path $root 'tools/Invoke-FaultInjection.ps1') -Destination (Join-Path $directory 'tools')
        foreach ($file in @('Rewrite.dll', 'Rewrite.xml', 'MicrosoftInstrumentationEngine_x64.dll')) {
            $from = Join-Path $windowsRuntime $file
            $to = Join-Path $directory "tools/runtime/$file"
            if ((Test-Path -LiteralPath $to) -and (Get-FileHash -LiteralPath $from).Hash -eq (Get-FileHash -LiteralPath $to).Hash) { continue }
            Copy-Item -LiteralPath $from -Destination $to
        }
    }
    return $directory
}
function New-LegacyPlan([string] $Directory, [string] $Source, [string] $Binary, [string] $References, [string] $Method) {
    $candidates = @(& $tool list $Source $Binary $References)
    if ($LASTEXITCODE -ne 0) { throw 'Candidate listing failed.' }
    $selected = @($candidates | Where-Object { $_ -match ('^\d+: ' + [regex]::Escape($Method) + ' -> ') })
    if ($selected.Count -ne 1) { throw "Expected one candidate: $Method" }
    $selection = ($selected[0] -split ':', 2)[0]
    $temporary = Join-Path $Directory ('.plan-' + [guid]::NewGuid() + '.json')
    & $tool plan $Source $Binary $References $selection $temporary
    if ($LASTEXITCODE -ne 0) { throw 'Plan generation failed.' }
    $plan = Get-Content -Raw -LiteralPath $temporary | ConvertFrom-Json -AsHashtable
    Remove-Item -LiteralPath $temporary
    return $plan
}
function Save-Plan($Directory, $Plan, $Source, $Binary, $References) {
    $path = Join-Path $Directory 'plan.json'
    $Plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    & $tool verify $Source $Binary $References $path
    if ($LASTEXITCODE -ne 0) { throw "Generated plan did not verify: $Directory" }
}
$coreSource = Join-Path $root 'tests/Fixtures/CoreFileTarget/Program.cs'
$directory = New-SampleLayout 'CoreFileTarget' (Join-Path $root 'tests/Fixtures/CoreFileTarget/bin/Release/net10.0') $coreSource
$binary = Join-Path $directory 'target/CoreFileTarget.dll'
$plan = New-LegacyPlan $directory $coreSource $binary $net10 'Program.WriteSelected(string, string)'
$plan.path = Join-Path $directory 'target/write-probe.txt'
Save-Plan $directory $plan $coreSource $binary $net10
$sqlSource = Join-Path $root 'tests/Fixtures/SqlTarget/Program.cs'
foreach ($entry in @(
    @('SqlTimeout', 'Program.Selected(System.Data.SqlClient.SqlCommand)'),
    @('FileWriteFailure', 'Program.WriteSelected(string, string)'),
    @('TcpConnectionRefused', 'Program.ConnectSelected(System.Net.Sockets.TcpClient, string, int)')
)) {
    $directory = New-SampleLayout $entry[0] (Join-Path $root 'tests/Fixtures/SqlTarget/bin/Release/net48') $sqlSource
    $binary = Join-Path $directory 'target/SqlTarget.exe'
    $plan = New-LegacyPlan $directory $sqlSource $binary $net48 $entry[1]
    if ($plan.ContainsKey('path')) { $plan.path = Join-Path $directory 'target/write-probe.txt' }
    Save-Plan $directory $plan $sqlSource $binary $net48
}
foreach ($entry in @(@('ProfilerReturnValue','ProfilerTarget'), @('SharedFlags','SyncService'))) {
    $kind = if ($entry[0] -eq 'SharedFlags') { 'Observation' } else { 'WindowsFixed' }
    $null = New-SampleLayout $entry[0] (Join-Path $root "tests/Fixtures/$($entry[1])/bin/Release/net48") (Join-Path $root "tests/Fixtures/$($entry[1])/Program.cs") $kind
}
$directory = New-SampleLayout 'PlanDrivenInjection' (Join-Path $v3 'Probe/bin/publish') (Join-Path $v3 'Probe/Program.cs') 'LinuxPlan'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-LinuxSample.ps1') -Destination (Join-Path $directory 'tools')
foreach ($file in @('libDotNetFaultInjectionAgent.so','libInstrumentationEngine.so','Rewrite.xml')) {
    Copy-Item -LiteralPath (Join-Path $root "src/Native/Linux/bin/$file") -Destination (Join-Path $directory 'tools/runtime')
}
Copy-Item -LiteralPath (Join-Path $root 'src/Runtime/bin/Release/net8.0/DotNetFaultInjectionAgent.Runtime.dll') -Destination (Join-Path $directory 'target')
Copy-Item -LiteralPath (Join-Path $root 'src/Runtime/bin/Release/net8.0/DotNetFaultInjectionAgent.Runtime.deps.json') -Destination (Join-Path $directory 'tools/runtime')
New-Item -ItemType Directory -Force (Join-Path $directory 'tools/planner') | Out-Null
Get-ChildItem (Join-Path $root 'src/Planner/bin/Release/net8.0') -File | Copy-Item -Destination (Join-Path $directory 'tools/planner')
$source = Join-Path $v3 'Probe/Program.cs'
$line = (Select-String -LiteralPath $source -SimpleMatch 'File.WriteAllText(path, "selected")').LineNumber
$temporary = Join-Path $directory ('.plan-'+[guid]::NewGuid()+'.json')
dotnet (Join-Path $directory 'tools/planner/Planner.dll') plan (Join-Path $directory 'target/Probe.dll') $source $line $temporary
if ($LASTEXITCODE -ne 0) { throw 'Version 3 plan failed.' }
$plan = Get-Content -Raw -LiteralPath $temporary | ConvertFrom-Json -AsHashtable
Remove-Item -LiteralPath $temporary
$plan.target.path = '/app/Probe.dll'
$plan.conditions.environment = @{ CHAOS_TEST_CASE = 'enabled' }
$plan.maxFirings = 1
$plan.fault = @{kind='throw';exceptionType='System.IO.IOException';message='planned file failure';delayMs=0}
$plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $directory 'plan.json') -Encoding utf8NoBOM
dotnet (Join-Path $directory 'tools/planner/Planner.dll') verify (Join-Path $directory 'target/Probe.dll') $source $line (Join-Path $directory 'plan.json')
if ($LASTEXITCODE -ne 0) { throw 'Version 3 plan verification failed.' }
if ($OrderProject) {
    $OrderProject = (Resolve-Path -LiteralPath $OrderProject).Path
    $OrderBinary = (Resolve-Path -LiteralPath $OrderBinary).Path
    foreach ($name in @('OrderTimeout','OrderDelay')) {
        $directory = New-SampleLayout $name (Join-Path $root 'tests/OrderAgentProbe/Host/bin/Release/net48') (Join-Path $root 'tests/OrderAgentProbe/Host/Program.cs')
        Copy-Item -LiteralPath $OrderBinary -Destination (Join-Path $directory 'target')
        Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($OrderBinary,'.pdb')) -Destination (Join-Path $directory 'target')
        Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($OrderBinary,'.pdb')) -Destination (Join-Path $directory 'reference')
        $plan = New-LegacyPlan $directory $OrderProject $OrderBinary $net48 'MultithreadedInMemoryTableInsert.MultithreadedOrderInsertMain.PerformSqlTask(int, MultithreadedInMemoryTableInsert.MultithreadedOrderInsertMain)'
        $plan.server = '(localdb)\MSSQLLocalDB'
        $plan.database = 'DotNetFaultInjectionAgentOrder_' + [guid]::NewGuid().ToString('N')
        $plan.procedure = 'Website.InsertCustomerOrders'
        $plan.maxFirings = 1
        if ($name -eq 'OrderDelay') { $plan.fault = 'delay-before-send'; $plan.delayMs = 4000 }
        Save-Plan $directory $plan $OrderProject $OrderBinary $net48
        New-Item -ItemType Directory -Force (Join-Path $directory 'reference/external') | Out-Null
        Get-ChildItem (Split-Path -Parent $OrderProject) | Where-Object Name -NotIn @('bin','obj') |
            Copy-Item -Destination (Join-Path $directory 'reference/external') -Recurse -Force
    }
}
Write-Output 'Sample deployment files and plans prepared. No sample applications were executed.'
