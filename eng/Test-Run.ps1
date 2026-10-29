#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$launcher = Join-Path $repository 'run.ps1'
$calls = [Collections.Generic.List[object]]::new()
$failureAt = -1
$throwAt = -1

function Invoke-FakeCommand {
    param([string] $Name, [object[]] $Arguments)
    $calls.Add(@{ Name = $Name; Arguments = @($Arguments); Directory = $PWD.Path })
    if ($calls.Count -eq $throwAt) { throw 'Synthetic command failure.' }
    $global:LASTEXITCODE = if ($calls.Count -eq $failureAt) { 1 } else { 0 }
}

function git { Invoke-FakeCommand 'git' $args }
function dotnet { Invoke-FakeCommand 'dotnet' $args }
function Check {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

$expected = @(
    @{ Name = 'git'; Arguments = @('pull', '--ff-only') },
    @{ Name = 'dotnet'; Arguments = @('restore', '.\src\Kora\Kora.csproj', '--locked-mode') },
    @{ Name = 'dotnet'; Arguments = @('tool', 'restore') },
    @{ Name = 'dotnet'; Arguments = @('build', '.\src\Kora\Kora.csproj', '--configuration', 'Debug', '--no-restore') },
    @{ Name = 'dotnet'; Arguments = @('run', '--project', '.\src\Kora\Kora.csproj', '--configuration', 'Debug', '--no-build', '--no-restore') }
)

$originalLocation = $PWD.Path
$originalExitCode = Get-Variable LASTEXITCODE -Scope Global -ErrorAction Ignore
Push-Location -LiteralPath $PSScriptRoot
try {
    if (-not $IsWindows) {
        $failure = $null
        try { & $launcher } catch { $failure = $_ }
        Check ($null -ne $failure -and $failure.ToString().Contains('Kora requires Windows')) 'Non-Windows launch must explain the platform requirement.'
        Check ($calls.Count -eq 0) 'Non-Windows launch must not pull, restore, build or run.'
        Check ($PWD.Path -eq $PSScriptRoot) 'Platform rejection must preserve the caller location.'
    }
    else {
        & $launcher
        Check ($calls.Count -eq $expected.Count) 'Launcher must execute every stage exactly once.'
        for ($index = 0; $index -lt $expected.Count; $index++) {
            Check ($calls[$index].Name -ceq $expected[$index].Name) "Wrong command at stage $index."
            Check (($calls[$index].Arguments -join '|') -ceq ($expected[$index].Arguments -join '|')) "Wrong arguments at stage $index."
            Check ($calls[$index].Directory -eq $repository) "Stage $index must run in the checkout, not the caller directory."
        }
        Check ($PWD.Path -eq $PSScriptRoot) 'Successful launch must restore the caller location.'

        foreach ($mode in 'exit-code', 'exception') {
            for ($stage = 1; $stage -le $expected.Count; $stage++) {
                $calls.Clear()
                $failureAt = if ($mode -eq 'exit-code') { $stage } else { -1 }
                $throwAt = if ($mode -eq 'exception') { $stage } else { -1 }
                $failure = $null
                try { & $launcher } catch { $failure = $_ }
                Check ($null -ne $failure) "Stage $stage $mode must fail explicitly."
                Check ($calls.Count -eq $stage) "Stage $stage $mode must stop all later stages."
                Check ($PWD.Path -eq $PSScriptRoot) "Stage $stage $mode must restore the caller location."
            }
        }
    }
}
finally {
    Pop-Location
    if ($null -eq $originalExitCode) {
        Remove-Variable LASTEXITCODE -Scope Global -ErrorAction Ignore
    }
    else {
        $global:LASTEXITCODE = $originalExitCode.Value
    }
}
Check ($PWD.Path -eq $originalLocation) 'Test must restore the caller location.'
Write-Host 'Debug launcher contracts passed; no real pull, restore, build or app launch was performed.'
