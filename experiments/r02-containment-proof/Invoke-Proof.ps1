[CmdletBinding()]
param(
    [string] $EvidenceDirectory = (Join-Path $PSScriptRoot "artifacts\proof-$([Guid]::NewGuid().ToString('N'))"),
    [string] $PowerShellPath = (Get-Command pwsh -ErrorAction Stop).Source
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not [Environment]::Is64BitProcess) {
    throw 'This experiment requires a non-elevated Windows x64 host.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
try {
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Do not run this proof elevated. Privileged deployment trials require separate approval.'
    }
} finally {
    $identity.Dispose()
}

$project = Join-Path $PSScriptRoot 'ContainmentProof.csproj'
$exe = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\ContainmentProof.exe'
$versionText = & $PowerShellPath -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()'
if ($LASTEXITCODE -ne 0) { throw 'The selected PowerShell failed its no-profile version probe.' }
$version = [Version]::Parse($versionText.Trim())
if ($version.Major -ne 7 -or $version -lt [Version]'7.4') {
    throw "Unsupported PowerShell $version; this proof requires 7.4 or later in major version 7."
}

& dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw "Experiment build failed: $LASTEXITCODE" }
& $exe self-test
if ($LASTEXITCODE -ne 0) { throw "Deterministic receipt tests failed: $LASTEXITCODE" }
& $exe run $EvidenceDirectory $PowerShellPath
$proofExit = $LASTEXITCODE
if ($proofExit -notin 0, 2) { throw "OS trial failed: $proofExit; inspect retained evidence and errors." }

[ordered]@{
    build = 'Passed'
    receiptTests = 'Passed (5 assertions; supplementary only)'
    osProofExitCode = $proofExit
    osProof = if ($proofExit -eq 0) { 'Measured assertions passed; production profile NOT certified' } else { 'Unsupported or unproven; examine trials.json' }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'validation.json') -Encoding utf8
Write-Host "Evidence: $EvidenceDirectory"
exit $proofExit
