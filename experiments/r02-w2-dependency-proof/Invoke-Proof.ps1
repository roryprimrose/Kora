[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [Parameter(Mandatory)][string] $PowerShellPath,
    [Parameter(Mandatory)][switch] $ConsentOwnedScratch
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $ConsentOwnedScratch -or -not $IsWindows -or -not [Environment]::Is64BitProcess) {
    throw 'Explicit owned-scratch consent and non-elevated Windows x64 required.'
}
$output = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $output) { throw 'EvidenceDirectory must be new.' }
$powershell = [IO.Path]::GetFullPath($PowerShellPath)
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($powershell)
if ($version.FileMajorPart -ne 7 -or $version.FileMinorPart -lt 4) { throw 'Existing PowerShell 7.4+ required; no acquisition.' }
function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $LASTEXITCODE" }
}
$declared = Join-Path $PSScriptRoot 'artifacts\declared'
$undeclared = Join-Path $PSScriptRoot 'artifacts\undeclared'
Invoke-DotNet -Arguments @('build', (Join-Path $PSScriptRoot 'payload\Payload.csproj'), '-c', 'Release',
    '-p:RestoreLockedMode=true', '-p:PayloadVariant=Declared', '-o', $declared)
Invoke-DotNet -Arguments @('build', (Join-Path $PSScriptRoot 'payload\Payload.csproj'), '-c', 'Release',
    '-p:RestoreLockedMode=true', '-p:PayloadVariant=Undeclared', '-o', $undeclared)
Invoke-DotNet -Arguments @('build', (Join-Path $PSScriptRoot 'tests\W2Tests.csproj'), '-c', 'Release', '-p:RestoreLockedMode=true')
Invoke-DotNet -Arguments @('test', '--project', (Join-Path $PSScriptRoot 'tests\W2Tests.csproj'),
    '-c', 'Release', '--no-build', '--', '--report-trx', '--results-directory',
    (Join-Path $PSScriptRoot "artifacts\tests-$([guid]::NewGuid().ToString('N'))"))
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$files = @(Get-ChildItem -LiteralPath $PSScriptRoot -File)
foreach ($directory in @('scripts', 'payload', 'tests', 'native')) {
    $files += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $directory) -File)
}
foreach ($file in @('Native.cs', 'Model.cs', 'NativeProcessHandle.cs')) {
    $files += Get-Item -LiteralPath (Join-Path $PSScriptRoot "..\r02-containment-proof\$file")
}
foreach ($file in @('global.json', 'Directory.Packages.props', 'Directory.Build.targets')) {
    $files += Get-Item -LiteralPath (Join-Path $root $file)
}
$source = [ordered]@{
    sourceRevision = (& git -C $root rev-parse HEAD)
    sdk = (& dotnet --version)
    utc = [DateTimeOffset]::UtcNow
    sourceDirty = $true
    files = @($files | Sort-Object FullName | ForEach-Object {
        @{ name = [IO.Path]::GetRelativePath($root, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    systemNativeInputs = @('kernel32.dll', 'advapi32.dll', 'userenv.dll', 'ntdll.dll') | ForEach-Object {
        $file = Join-Path ([Environment]::SystemDirectory) $_
        @{ name = $_; version = [Diagnostics.FileVersionInfo]::GetVersionInfo($file).FileVersion
            sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    }
}
& (Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\W2Proof.exe') run $output $powershell $declared $undeclared
$result = $LASTEXITCODE
if (-not (Test-Path -LiteralPath $output -PathType Container)) { throw 'Host failed before evidence creation.' }
$source | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'source.json')
if ($result -notin @(0, 2)) { throw "Fixture error $result; inspect the explicit failure and cleanup evidence." }
Write-Host 'Exit 2 is rejected/blocked admission, not OS-profile certification. No production executor is enabled.'
exit $result
