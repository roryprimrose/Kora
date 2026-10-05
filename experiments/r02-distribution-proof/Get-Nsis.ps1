[CmdletBinding()]
param([ValidateSet('windows', 'source')][string] $Archive = 'windows')
. (Join-Path $PSScriptRoot 'Common.ps1')
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tools.lock.json') -Raw |
    ConvertFrom-Json -AsHashtable
$entry = $lock.nsis[$Archive]
$directory = Join-Path $PSScriptRoot 'tools'
if (!(Test-Path -LiteralPath $directory)) { New-ProofDirectory $directory }
Assert-NoLinks $directory
$name = if ($Archive -eq 'windows') { "nsis-$($lock.nsis.version).zip" } else { "nsis-$($lock.nsis.version)-src.tar.bz2" }
$path = Join-Path $directory $name
if (!(Test-Path -LiteralPath $path)) {
    $temporary = "$path.download"
    if (Test-Path -LiteralPath $temporary) { throw 'Partial download exists; review it before retrying.' }
    Invoke-WebRequest -Uri $entry.url -OutFile $temporary -TimeoutSec 120
    if ((Get-FileHash -LiteralPath $temporary).Hash.ToLowerInvariant() -ne $entry.sha256) {
        throw 'NSIS archive hash mismatch; untrusted bytes retained but not extracted.'
    }
    Move-Item -LiteralPath $temporary -Destination $path
}
if ((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $entry.sha256) {
    throw 'NSIS archive hash mismatch.'
}
if ($Archive -eq 'windows') {
    $extracted = Join-Path $directory "nsis-$($lock.nsis.version)"
    if (!(Test-Path -LiteralPath $extracted)) {
        Expand-Archive -LiteralPath $path -DestinationPath $directory
    }
    Write-Host "Verified archive: $path. Compiler: $(Join-Path $extracted 'makensis.exe')"
}
else {
    Write-Host "Verified source archive: $path. Build NSIS natively on Linux; no Wine/Windows compiler substitution."
}
