#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path -Parent $PSScriptRoot
$desktop = Join-Path $root 'src\Kora\Kora.csproj'
$application = Join-Path $root 'src\Kora.Application\Kora.Application.csproj'
$probe = Join-Path $PSScriptRoot 'async-boundaries\Probe.targets'

Push-Location $root
try {
    # The real adapters must build with the repository's unchanged analyzer set.
    & dotnet build $desktop --configuration Release --no-restore --no-incremental
    if ($LASTEXITCODE -ne 0) { throw 'The production async event/command adapters failed to build.' }

    $output = & dotnet build $application --configuration Release --no-restore --no-incremental `
        -p:BuildProjectReferences=false "-p:CustomAfterMicrosoftCommonTargets=$probe" `
        -p:KoraAsyncVoidServiceProbe=true 2>&1 | Out-String
    $probeExit = $LASTEXITCODE
    Write-Host $output

    # A failed probe must not leave stale probe outputs for subsequent tests.
    & dotnet build $application --configuration Release --no-restore --no-incremental -p:BuildProjectReferences=false
    if ($LASTEXITCODE -ne 0) { throw 'The production application failed to rebuild after the probe.' }

    if ($probeExit -eq 0) { throw 'An unjustified async-void application service unexpectedly compiled.' }
    if ($output -notmatch 'UnjustifiedAsyncVoidService\.cs\(\d+,\d+\): error VSTHRD100:') {
        throw 'The probe failed without the required VSTHRD100 error on the unjustified service.'
    }
    if ($output -match '\berror (?!VSTHRD100:)[A-Z]+\d+:') {
        throw 'The probe failed for an unrelated reason; async-void enforcement is not established.'
    }
    Write-Host 'Async-void analysis passed: real adapters compile; the unjustified application service fails with VSTHRD100.'
}
finally {
    Pop-Location
}
