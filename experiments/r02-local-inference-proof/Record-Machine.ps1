[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) {
    throw 'Refusing to overwrite existing machine evidence.'
}
$os = Get-CimInstance Win32_OperatingSystem
$processors = @(Get-CimInstance Win32_Processor)
$power = (& powercfg /getactivescheme | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not record active power scheme.' }
$revision = (& git rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not record repository revision.' }
$status = @(& git status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Could not record repository status.' }
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
$exeEvidence = $null
if (Test-Path -LiteralPath $exe) {
    $file = Get-Item -LiteralPath $exe
    $exeEvidence = @{
        Location = '%LOCALAPPDATA%\Programs\Ollama\ollama.exe'
        LengthBytes = $file.Length
        FileVersion = $file.VersionInfo.FileVersion
        Sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    }
}
$disks = @(Get-PhysicalDisk | Select-Object MediaType, Size, BusType)
$volumes = @(Get-Volume | Where-Object DriveLetter | Select-Object DriveLetter, Size, SizeRemaining)
$report = [ordered]@{
    Schema = 'Kora.R02.MachineEvidence.v1'
    RecordedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Revision = $revision
    WorkingTreeChanges = $status
    OS = @{ Caption = $os.Caption; Version = $os.Version; BuildNumber = $os.BuildNumber; Architecture = $os.OSArchitecture }
    Cpu = @($processors | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
    VisibleRamBytes = [long]$os.TotalVisibleMemorySize * 1024
    Disks = $disks
    Volumes = $volumes
    PowerScheme = $power
    OllamaExecutable = $exeEvidence
    OllamaHostSetting = $env:OLLAMA_HOST
    OllamaModelStorageSetting = if ($env:OLLAMA_MODELS) { 'Custom path configured; record redacted path and storage volume separately.' } else { 'Default %USERPROFILE%\.ollama\models' }
    UIAndContention = 'Not a reference trial: Kora UI not launched; normal service contention not controlled or quantified.'
    ReferenceFloor = 'At least 8 logical CPU cores, 16 GiB RAM, SSD, supported Windows 11 x64; CPU-only inference must be verified separately.'
    NetworkBlock = 'Not approved or applied on this shared machine.'
    InventoryPrivacy = 'No clipboard, host name, user name, serial numbers, accounts, network destinations or environment-variable dump collected.'
}
$parent = Split-Path -Parent $OutputPath
[IO.Directory]::CreateDirectory($parent) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
