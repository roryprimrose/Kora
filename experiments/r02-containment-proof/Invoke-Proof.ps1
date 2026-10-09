[CmdletBinding()]
param(
    [string] $EvidenceDirectory = (Join-Path $PSScriptRoot "artifacts\proof-$([Guid]::NewGuid().ToString('N'))"),
    [string] $PowerShellPath = (Get-Command pwsh -ErrorAction Stop | Select-Object -First 1).Source,
    [switch] $NetworkHandoff,
    [switch] $PrepareOnly,
    [switch] $ConsentOwnedScratch
)

$ErrorActionPreference = 'Stop'
if ($PrepareOnly -and ($ConsentOwnedScratch -or $NetworkHandoff)) {
    throw 'PrepareOnly cannot request live trials or an administrator handoff.'
}
if (-not $PrepareOnly -and -not $ConsentOwnedScratch) {
    throw 'Live trials require separately approved owned scratch, local TCP endpoints, synthetic credential and temporary AppContainer profile effects; pass ConsentOwnedScratch only after approval.'
}
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Evidence destination must be new.' }
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

& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Experiment build failed: $LASTEXITCODE" }
& $exe self-test
if ($LASTEXITCODE -ne 0) { throw "Deterministic receipt tests failed: $LASTEXITCODE" }
if ($PrepareOnly) {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $revision = & git -C $repository rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the preparation source revision.' }
    $sourceStatus = & git -C $repository status --porcelain -- experiments/r02-containment-proof global.json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot distinguish committed and locally modified preparation inputs.' }
    $sdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the preparation SDK.' }
    $sources = Get-ChildItem -LiteralPath $PSScriptRoot -File |
        Where-Object { $_.Extension -in '.cs', '.ps1', '.csproj', '.props' } |
        Sort-Object Name |
        ForEach-Object {
            [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        }
    $candidate = @($exe, [IO.Path]::ChangeExtension($exe, '.dll')) |
        ForEach-Object {
            [ordered]@{ name = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
        }
    New-Item -ItemType Directory -Path $EvidenceDirectory -ErrorAction Stop | Out-Null
    [ordered]@{
        preparationOnly = $true
        utc = [DateTimeOffset]::UtcNow.ToString('O')
        sourceRevision = $revision.Trim()
        sourceInputsLocallyModified = @($sourceStatus).Count -gt 0
        sdkVersion = $sdkVersion.Trim()
        sdkSelectionSha256 = (Get-FileHash -LiteralPath (Join-Path $repository 'global.json') -Algorithm SHA256).Hash
        osVersion = [Environment]::OSVersion.Version.ToString()
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        powerShellVersion = $version.ToString()
        powerShellExecutableSha256 = (Get-FileHash -LiteralPath $PowerShellPath -Algorithm SHA256).Hash
        sources = @($sources)
        candidate = @($candidate)
        receiptTests = 'Passed (5 deterministic assertions; not OS containment evidence)'
        liveTrialsPerformed = $false
        supportedReferenceOsQualified = $false
        protectedRuntimeClosureQualified = $false
        networkAttribution = 'Unproven'
        productionProfileCertified = $false
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'preparation.json') -Encoding utf8
    Write-Host "File-only preparation completed: $EvidenceDirectory; no OS trial or network collection."
    exit 0
}
$mode = if ($NetworkHandoff) { 'run-with-network-handoff' } else { 'run' }
& $exe $mode $EvidenceDirectory $PowerShellPath consent-owned-scratch-local-network-synthetic-credential
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
