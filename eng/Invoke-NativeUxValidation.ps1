#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidateSet('Prepare', 'Launch', 'SignOff')][string] $Stage = 'Prepare',
    [string] $PackageSource,
    [switch] $ApproveInteractiveLaunch
)
. (Join-Path $PSScriptRoot 'NativeUxValidation.Common.ps1')
Assert-NativeUxProfile
$OutputDirectory = Assert-NativeUxOutput $OutputDirectory
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ($Stage -ceq 'Prepare') {
    if ($ApproveInteractiveLaunch) { throw 'Prepare never launches windows; approval belongs only to Stage Launch.' }
    New-ProofDirectory $OutputDirectory
    $source = Get-NativeUxSourceIdentity $repository
    $fixture = Join-Path $repository 'tests\Kora.NativeUxFixture\Kora.NativeUxFixture.csproj'
    $testProject = Join-Path $repository 'tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj'
    $receipt = [ordered]@{ schema = 1; status = 'Running'; nativeAcceptance = 'Pending'
        authority = 'Local research evidence, not security audit, execution permission or release certification'
        startedUtc = [DateTimeOffset]::UtcNow.ToString('O'); source = $source
        environment = [ordered]@{ osVersion = [Environment]::OSVersion.Version.ToString()
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            powerShell = $PSVersionTable.PSVersion.ToString(); profile = 'Non-elevated interactive Windows'
            display = 'Not observed by focus-free preparation' }
        windowsLaunched = $false; tests = $null; payload = @()
    }
    try {
        Push-Location $repository
        try {
            foreach ($project in $fixture, $testProject) {
                if (!(Test-Path -LiteralPath (Join-Path (Split-Path $project) 'obj\project.assets.json'))) {
                    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
                        throw 'Dependencies are missing. Supply your machine-approved feed with -PackageSource; no registry is selected automatically.'
                    }
                    Write-Host "Restoring locked dependencies: $project"
                    Invoke-Checked dotnet @('restore', $project, '--locked-mode', '--source', $PackageSource)
                }
            }
            Write-Host 'Building focused headless integration tests (no native windows).'
            Invoke-Checked dotnet @('build', $testProject, '--configuration', 'Release', '--no-restore')
            Write-Host 'Publishing the isolated native fixture without launching it.'
            Invoke-Checked dotnet @('publish', $fixture, '--configuration', 'Release', '--no-restore',
                '--output', (Join-Path $OutputDirectory 'payload'))
            Write-Host 'Running the six directly applicable headless fixture/accessibility classes.'
            $arguments = @('test', '--project', $testProject, '--configuration', 'Release', '--no-build',
                '--results-directory', (Join-Path $OutputDirectory 'tests'), '--report-trx', '--report-trx-filename', 'focused.trx',
                '--filter-class') + @(Get-NativeUxTestClasses)
            Invoke-Checked dotnet $arguments
        } finally { Pop-Location }
        $receipt.tests = Get-NativeUxTestSummary (Join-Path $OutputDirectory 'tests\focused.trx')
        $after = Get-NativeUxSourceIdentity $repository
        if ($source.buildInputSha256 -cne $after.buildInputSha256 -or $source.revision -cne $after.revision) {
            throw 'Build inputs changed during preparation. Prepare a new bundle; do not use this payload as qualified evidence.'
        }
        $receipt.payload = @(Get-PayloadFiles (Join-Path $OutputDirectory 'payload'))
        $receipt.status = 'Pass'
        Write-ProofJson (Get-NativeUxObservationTemplate) (Join-Path $OutputDirectory 'operator.json')
    }
    catch {
        $receipt.status = 'Failed'
        throw
    }
    finally {
        $receipt.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Write-ProofJson $receipt (Join-Path $OutputDirectory 'automated.json')
    }
    Write-Host "Focus-free preparation passed: $($receipt.tests.passed) tests, no skipped tests, no native launch."
    Write-Host "Evidence: $OutputDirectory"
    Write-Host 'Follow tests\Kora.NativeUxFixture\README.md when ready for an explicitly approved interactive trial.'
    return
}

$prepared = Assert-NativeUxBundle $OutputDirectory
if ($Stage -ceq 'Launch') {
    if (!$ApproveInteractiveLaunch) {
        throw 'Launch opens and focuses native windows. Supply -ApproveInteractiveLaunch only when ready to stop other desktop work.'
    }
    if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'signoff.json')) {
        throw 'This bundle has already been signed off. Prepare a new bundle for a new qualification.'
    }
    $trial = Join-Path $OutputDirectory ('trial-' + [Guid]::NewGuid().ToString('N'))
    New-ProofDirectory $trial
    $scratch = Join-Path $trial 'scratch'
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $receipt = [ordered]@{ schema = 1; status = 'Running'; startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        preparationSha256 = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'automated.json')).Hash.ToLowerInvariant()
        trial = Split-Path $trial -Leaf; processId = $null; exitCode = $null; readyObserved = $false
        scratchCleaned = $false; nativeAcceptance = 'Pending'; forcedTermination = $false }
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo.FileName = Join-Path $OutputDirectory 'payload\Kora.NativeUxFixture.exe'
        $process.StartInfo.WorkingDirectory = Join-Path $OutputDirectory 'payload'
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        foreach ($argument in '--launch-native-fixtures', '--scratch-parent', $scratch) {
            $process.StartInfo.ArgumentList.Add($argument)
        }
        if (!$process.Start()) { throw 'The fixture process did not start.' }
        $receipt.processId = $process.Id
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        Write-Host "Fixture PID $($process.Id). Complete the walkthrough, then use Stop fixture and clean up scratch state."
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $trial 'stdout.local.txt'), $output)
        [IO.File]::WriteAllText((Join-Path $trial 'stderr.local.txt'), $errors)
        $receipt.exitCode = $process.ExitCode
        $receipt.readyObserved = $output.Contains('READY: native fixture initialized.', [StringComparison]::Ordinal)
        $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
        $null = Assert-NativeUxBundle $OutputDirectory
        if ($receipt.exitCode -ne 0 -or !$receipt.readyObserved -or !$receipt.scratchCleaned -or $errors.Length -gt 0) {
            throw "Fixture did not finish cleanly. Inspect the exact trial's local output; native acceptance remains pending."
        }
        $receipt.status = 'Completed'
    }
    catch {
        $receipt.status = 'Failed'
        throw
    }
    finally {
        if ($null -ne $receipt.processId -and !$process.HasExited) {
            $receipt.forcedTermination = $true
            $receipt.status = 'Failed'
            Stop-Process -Id $receipt.processId -ErrorAction Stop
            $process.WaitForExit()
        }
        $process.Dispose()
        $receipt.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Write-ProofJson $receipt (Join-Path $trial 'interactive.json')
    }
    Write-Host 'Native fixture closed cleanly. This is not an observation pass; fill operator.json and run Stage SignOff.'
    return
}

if ($ApproveInteractiveLaunch) { throw 'SignOff does not launch windows.' }
if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'signoff.json')) {
    throw 'Refusing to overwrite an existing sign-off.'
}
$trials = @(Get-ChildItem -LiteralPath $OutputDirectory -Directory -Filter 'trial-*' | Sort-Object Name)
if ($trials.Count -eq 0) { throw 'Sign-off needs a completed interactive trial; headless tests are insufficient.' }
$preparationHash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'automated.json')).Hash.ToLowerInvariant()
$trialHashes = @(
    foreach ($trial in $trials) {
        $path = Join-Path $trial.FullName 'interactive.json'
        $result = Read-NativeUxJson $path
        if ($result.schema -ne 1 -or $result.status -cne 'Completed' -or $result.exitCode -ne 0 -or
            !$result.readyObserved -or !$result.scratchCleaned -or $result.forcedTermination -or
            $result.preparationSha256 -cne $preparationHash -or
            @(Get-ChildItem -LiteralPath (Join-Path $trial.FullName 'scratch') -Force).Count -ne 0) {
            throw 'A failed, incomplete, changed or unclean trial cannot be signed off. Preserve it and prepare a fresh bundle.'
        }
        (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
    }
)
$observations = Read-NativeUxJson (Join-Path $OutputDirectory 'operator.json')
$status = Get-NativeUxSignOffStatus $observations
Write-ProofJson ([ordered]@{ schema = 1; status = $status; recordedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    preparationSha256 = $preparationHash; interactiveReceiptSha256 = $trialHashes
    operatorReceiptSha256 = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'operator.json')).Hash.ToLowerInvariant()
    observations = $observations; qualification = 'Only these recorded synthetic native UX observations on this machine/display/payload. Full R05/R12/R14/A4 and release acceptance remain open.'
}) (Join-Path $OutputDirectory 'signoff.json')
Write-Host "Recorded $status. Blocked or failed observations remain open; no full acceptance gate is closed."
