#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Package)
$ErrorActionPreference = 'Stop'
$packagePath = (Resolve-Path -LiteralPath $Package).ProviderPath
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root ('artifacts/package-scans/' + [guid]::NewGuid().ToString('N'))
$extracted = Join-Path $output 'extracted'
New-Item -ItemType Directory -Force $output | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $extracted)
$hash = (Get-FileHash $packagePath).Hash
if ($IsWindows) {
    Update-MpSignature -ErrorAction Stop
    $status = Get-MpComputerStatus
    if (-not $status.AntivirusEnabled -or -not $status.AMServiceEnabled -or $status.AntivirusSignatureLastUpdated -lt (Get-Date).AddDays(-3)) {
        throw 'Microsoft Defender is unavailable or its definitions are stale.'
    }
    $scanner = Get-ChildItem "$env:ProgramData/Microsoft/Windows Defender/Platform" -Filter MpCmdRun.exe -Recurse |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $scanner) { throw 'Microsoft Defender command-line scanner is missing.' }
    foreach ($target in @($packagePath, $extracted)) {
        & $scanner.FullName -Scan -ScanType 3 -File $target -DisableRemediation 2>&1 |
            Tee-Object -FilePath (Join-Path $output 'scan.log') -Append
        if ($LASTEXITCODE -ne 0) { throw "Defender scan failed or detected a threat: $LASTEXITCODE" }
    }
    $engine = 'Microsoft Defender'
    $engineVersion = [string]$status.AMEngineVersion
    $definitions = [string]$status.AntivirusSignatureVersion
} else {
    # The caller must successfully run freshclam before this script.
    & clamscan --recursive --infected --scan-archive=yes --alert-encrypted=yes --alert-exceeds-max=yes --max-filesize=256M --max-scansize=1024M --fail-if-cvd-older-than=3 "--log=$(Join-Path $output 'scan.log')" $packagePath $extracted
    if ($LASTEXITCODE -ne 0) { throw "ClamAV scan failed or detected a threat: $LASTEXITCODE" }
    $engine = 'ClamAV'
    $engineVersion = (& clamscan --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read scanner version.' }
    $definitions = $engineVersion
}
if ((Get-FileHash $packagePath).Hash -ne $hash) { throw 'Package changed during the scan.' }
[ordered]@{ Package = (Split-Path -Leaf $packagePath); Sha256 = $hash; Engine = $engine;
    EngineVersion = $engineVersion; Definitions = $definitions; ScannedAtUtc = [DateTime]::UtcNow.ToString('o');
    ArchiveAndExtractedFiles = $true; Result = 'passed' } | ConvertTo-Json |
    Set-Content (Join-Path $output 'summary.json') -Encoding utf8
Write-Host "Virus scan passed: $output"
