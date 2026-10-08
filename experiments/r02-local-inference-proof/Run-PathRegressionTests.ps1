[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new regression output directory.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$processDirectory = [Environment]::CurrentDirectory
$results = [Collections.Generic.List[string]]::new()
Push-Location $output
try {
    # PowerShell location and the process directory deliberately differ, as in linked-worktree sessions.
    [Environment]::CurrentDirectory = $PSScriptRoot
    & (Join-Path $PSScriptRoot 'Run-Qualification.ps1') -OutputDirectory '.\qualification' -ValidateOnly
    $qualification = Get-Content -LiteralPath '.\qualification\qualification.json' -Raw | ConvertFrom-Json
    if ($qualification.ExitCode -ne 0 -or -not $qualification.ValidateOnly) {
        throw 'Deterministic qualification did not complete.'
    }
    $results.Add('Qualification output follows PowerShell location.')
    $validation = Get-Content -LiteralPath '.\qualification\validation\validation.json' -Raw | ConvertFrom-Json
    if (-not $validation.ObservationSkipped -or $validation.PassedSelfTests -lt 55) {
        throw 'Nested validation did not remain deterministic.'
    }
    $results.Add('Nested validation output follows PowerShell location; no endpoint observation.')
    & (Join-Path $PSScriptRoot 'Record-Machine.ps1') -OutputPath '.\machine-child\machine.json'
    if (-not (Test-Path -LiteralPath '.\machine-child\machine.json' -PathType Leaf)) {
        throw 'Machine evidence was not written to the caller location.'
    }
    $results.Add('Machine output creates its parent in the PowerShell location.')
    $rejected = $false
    try {
        & (Join-Path $PSScriptRoot 'Run-Qualification.ps1') -OutputDirectory '.\qualification' -ValidateOnly
    } catch {
        if ($_.Exception.Message -notlike 'Use a new output directory*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Existing evidence was not protected from overwrite.' }
    $results.Add('Existing relative evidence is rejected before validation.')
    foreach ($writer in @('Record-Machine.ps1', 'Record-CandidateMetadata.ps1')) {
        $rejected = $false
        try {
            & (Join-Path $PSScriptRoot $writer) -OutputPath '.\machine-child\machine.json'
        } catch {
            if ($_.Exception.Message -notlike 'Refusing to overwrite*') { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "$writer did not protect existing evidence." }
        $results.Add("$writer rejects existing relative evidence before inventory or network access.")
    }
} finally {
    [Environment]::CurrentDirectory = $processDirectory
    Pop-Location
}
[ordered]@{
    Schema = 'Kora.R02.PathRegression.v1'
    RecordedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Passed = $results.ToArray()
    Scope = 'File-only wrapper regression; no runtime observation, generation or residency change.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'path-regression.json') -Encoding utf8
