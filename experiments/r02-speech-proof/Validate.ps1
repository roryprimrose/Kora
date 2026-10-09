[CmdletBinding()]
param(
    [string]$TestOutput = (Join-Path $PSScriptRoot 'evidence\unit-tests.txt')
)

$ErrorActionPreference = 'Stop'
$TestOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($TestOutput)
Push-Location $PSScriptRoot
try {
    $python = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path $python)) { throw 'Create the experiment-local Python environment first; see README.' }
    & $python -m pip check
    if ($LASTEXITCODE -ne 0) { throw 'Experiment dependency validation failed.' }
    & $python -m compileall -q prepare.py fixtures.py capture_probe.py benchmark.py test_benchmark.py privacy_receipts.py test_privacy_receipts.py
    if ($LASTEXITCODE -ne 0) { throw 'Experiment compilation failed.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $TestOutput) -Force | Out-Null
    & $python -m unittest -v test_benchmark test_privacy_receipts 2>&1 | Tee-Object -FilePath $TestOutput
    if ($LASTEXITCODE -ne 0) { throw 'Experiment tests failed.' }
}
finally {
    Pop-Location
}
