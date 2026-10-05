[CmdletBinding()]
param(
    [switch]$PublishAssets,
    [switch]$AllowBlockedCrossUser
)

$ErrorActionPreference = 'Stop'
if (-not [System.OperatingSystem]::IsWindows()) {
    throw 'This proof supports Windows only.'
}
Push-Location $PSScriptRoot
try {
    dotnet restore StorageProof.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Locked restore failed ($LASTEXITCODE)." }
    dotnet build StorageProof.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Proof build failed ($LASTEXITCODE)." }
    dotnet run --project StorageProof.csproj -c Release --no-build
    if ($LASTEXITCODE -eq 2 -and $AllowBlockedCrossUser) {
        Write-Warning 'Automated subset passed; actual second-user protection is BLOCKED. This is not release acceptance.'
    }
    elseif ($LASTEXITCODE -ne 0) {
        throw "Proof failed or required second-user validation is blocked ($LASTEXITCODE)."
    }
    if ($PublishAssets) {
        $assets = @()
        foreach ($rid in @('win-x64', 'win-x86')) {
            dotnet publish StorageProof.csproj -c Release -r $rid --self-contained false --no-restore
            if ($LASTEXITCODE -ne 0) { throw "$rid asset publish failed ($LASTEXITCODE)." }
            $name = 'e_sqlcipher.dll'
            $path = Join-Path $PSScriptRoot "bin\Release\net10.0\$rid\publish\$name"
            $bytes = [System.IO.File]::ReadAllBytes($path)
            $offset = [BitConverter]::ToInt32($bytes, 60)
            $machine = [BitConverter]::ToUInt16($bytes, $offset + 4)
            $expected = if ($rid -eq 'win-x64') { 0x8664 } else { 0x14c }
            if ($bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a -or $machine -ne $expected) {
                throw "$rid native PE architecture mismatch."
            }
            $assets += [pscustomobject]@{
                rid = $rid
                nativeFile = $name
                bytes = $bytes.Length
                machine = $machine
                sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                publish = 'passed'
                execution = if ($rid -eq 'win-x64') { 'portable proof exercised Windows x64 native engine; see latest.json' } else { 'not executed' }
            }
        }
        [pscustomobject]@{
            generatedUtc = [DateTimeOffset]::UtcNow
            buildHost = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
            assets = $assets
            supportedOperatingSystem = 'Windows'
            excludedScope = 'Linux runtime support and Linux-host cross-build validation are outside this Windows-only proof, not readiness gates.'
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'evidence\latest-native.json'
    }
}
finally {
    Pop-Location
}
