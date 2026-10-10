[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EvidenceDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows -or -not [Environment]::Is64BitProcess) {
    throw 'File-only W2 preparation requires Windows x64.'
}
$output = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $output) { throw 'EvidenceDirectory must be new; prior evidence is never overwritten.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$relative = [IO.Path]::GetRelativePath($root, $PSScriptRoot)
$steps = [Collections.Generic.List[object]]::new()
$receipt = [ordered]@{
    schema = 'Kora.W2Preparation.v1'
    startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceRevision = $null
    sourceDirty = $null
    sourceStatus = @()
    sdk = $null
    powershell = $PSVersionTable.PSVersion.ToString()
    scope = 'FileOnlyPreparation'
    status = 'Failed'
    exitCode = 1
    steps = $steps
    tests = $null
    sourceFiles = @()
    payloadFiles = @()
    qualification = 'NotRun'
    notRun = @('Invoke-Proof', 'Worker', 'RestrictedToken', 'AppContainer', 'NativeLoading',
        'InstalledProtection', 'WindowsControlEffects', 'NetworkTrials', 'ProductionLifecycle')
}

function Invoke-RecordedDotNet([string[]] $Arguments) {
    $log = '{0:D2}-dotnet.log' -f ($steps.Count + 1)
    $started = [DateTimeOffset]::UtcNow.ToString('O')
    & dotnet @Arguments *> (Join-Path $output $log)
    $code = $LASTEXITCODE
    $steps.Add([ordered]@{
        executable = 'dotnet'
        arguments = @($Arguments | ForEach-Object { $_.Replace($output, '$EvidenceDirectory') })
        startedUtc = $started
        endedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        exitCode = $code
        log = $log
    })
    if ($code -ne 0) { throw "dotnet failed with exit $code; inspect $log. No lock reconciliation or live trial is attempted." }
}

[void][IO.Directory]::CreateDirectory($output)
Push-Location $root
try {
    $receipt.sourceRevision = (& git rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source revision.' }
    $receipt.sourceStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source status.' }
    $receipt.sourceDirty = $receipt.sourceStatus.Count -ne 0
    $receipt.sdk = (& dotnet --version)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve existing SDK.' }
    $files = @(Get-ChildItem -LiteralPath $PSScriptRoot -File)
    foreach ($directory in @('scripts', 'payload', 'tests', 'native')) {
        $files += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $directory) -File)
    }
    foreach ($file in @('Native.cs', 'Model.cs', 'NativeProcessHandle.cs', 'NetworkCollection.cs',
            'InvocationPolicy.cs', 'DescendantObservations.cs')) {
        $files += Get-Item -LiteralPath (Join-Path $PSScriptRoot "..\r02-containment-proof\$file")
    }
    foreach ($file in @('global.json', 'Directory.Packages.props', 'Directory.Build.targets', 'eng\Get-BuildVersion.ps1')) {
        $files += Get-Item -LiteralPath (Join-Path $root $file)
    }
    $receipt.sourceFiles = @($files | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            name = [IO.Path]::GetRelativePath($root, $_.FullName)
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })

    foreach ($project in @('tests\W2Tests.csproj', 'payload\Payload.csproj')) {
        $path = Join-Path $relative $project
        $assets = Join-Path (Split-Path $path) 'obj\project.assets.json'
        if (-not (Test-Path -LiteralPath $assets -PathType Leaf)) {
            Invoke-RecordedDotNet @('restore', $path, '--locked-mode', '--verbosity', 'quiet')
        }
    }
    Invoke-RecordedDotNet @('build', (Join-Path $relative 'tests\W2Tests.csproj'),
        '--configuration', 'Release', '--no-restore', '--verbosity', 'quiet')
    Invoke-RecordedDotNet @('test', '--project', (Join-Path $relative 'tests\W2Tests.csproj'),
        '--configuration', 'Release', '--no-build', '--', '--report-trx',
        '--results-directory', (Join-Path $output 'tests'))

    $reports = @(Get-ChildItem -LiteralPath (Join-Path $output 'tests') -Filter '*.trx' -File -Recurse)
    if ($reports.Count -ne 1) { throw 'Expected exactly one new full-suite TRX report.' }
    [xml] $trx = Get-Content -LiteralPath $reports[0].FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $receipt.tests = [ordered]@{
        total = [int]$counters.total
        executed = [int]$counters.executed
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        notExecuted = [int]$counters.notExecuted
        report = [IO.Path]::GetRelativePath($output, $reports[0].FullName)
    }
    if ($receipt.tests.total -le 0 -or $receipt.tests.passed -ne $receipt.tests.total -or
        $receipt.tests.executed -ne $receipt.tests.total -or $receipt.tests.failed -ne 0 -or
        $receipt.tests.notExecuted -ne 0) {
        throw 'Preparation requires a nonempty full suite with no failures or skips.'
    }
    foreach ($variant in @('Declared', 'Undeclared')) {
        Invoke-RecordedDotNet @('build', (Join-Path $relative 'payload\Payload.csproj'),
            '--configuration', 'Release', '--no-restore', '--verbosity', 'quiet',
            "-p:PayloadVariant=$variant", '--output', (Join-Path $output $variant.ToLowerInvariant()))
    }
    $receipt.payloadFiles = @(foreach ($variant in @('declared', 'undeclared')) {
        Get-ChildItem -LiteralPath (Join-Path $output $variant) -File | Sort-Object Name | ForEach-Object {
            [ordered]@{
                name = [IO.Path]::GetRelativePath($output, $_.FullName)
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
    })
    $declared = (Get-FileHash -LiteralPath (Join-Path $output 'declared\W2Payload.dll')).Hash
    $undeclared = (Get-FileHash -LiteralPath (Join-Path $output 'undeclared\W2Payload.dll')).Hash
    if ($declared -eq $undeclared) { throw 'Declared and undeclared payload DLL bytes must differ.' }
    $receipt.status = 'PreparedOnly'
    $receipt.exitCode = 0
}
catch {
    $receipt.failure = $_.Exception.Message.Replace($output, '$EvidenceDirectory').Replace($root, '$Repository')
    Write-Error -Message $receipt.failure -ErrorAction Continue
}
finally {
    Pop-Location
    $receipt.endedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'preparation.json') -Encoding utf8
}
Write-Host "W2 file-only preparation: $($receipt.status). Qualification NotRun; no live worker or native effects."
exit $receipt.exitCode
