[CmdletBinding()]
param(
    [string] $FixtureRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $FixtureRoot) { $FixtureRoot = Join-Path $root ".scratch\installer-metadata\$([guid]::NewGuid().ToString('N'))" }
$fixture = [IO.Path]::GetFullPath($FixtureRoot)
& git -C $root check-ignore --quiet -- $fixture
if ($LASTEXITCODE -ne 0) { throw 'Installer metadata fixtures must use an ignored workspace directory.' }
if (Test-Path -LiteralPath $fixture) { throw 'Installer metadata fixtures require a new, dedicated directory.' }
New-Item -ItemType Directory -Path $fixture | Out-Null
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
Add-Type -Path (Join-Path $packageRoot 'wixtoolset.sdk\7.0.0\tools\net8.0\WixToolset.Dtf.WindowsInstaller.dll')
$guard = Join-Path $PSScriptRoot 'Assert-InstallerMetadata.ps1'
$cases = @(
    @{ Name = 'valid'; Sql = @(); Diagnostic = $null },
    @{ Name = 'reserved-directory'; Sql = @("INSERT INTO ``Directory`` (``Directory``) VALUES ('ARPINSTALLLOCATION')"); Diagnostic = 'ICE99' },
    @{ Name = 'missing-setter'; Sql = @('DELETE FROM `CustomAction`'); Diagnostic = 'exactly one' },
    @{ Name = 'extra-action'; Sql = @("INSERT INTO ``CustomAction`` (``Action``, ``Type``, ``Source``, ``Target``) VALUES ('LaunchKora', 50, 'INSTALLFOLDER', 'Kora.exe')"); Diagnostic = 'custom actions' },
    @{ Name = 'executable-action'; Sql = @('UPDATE `CustomAction` SET `Type` = 50'); Diagnostic = 'type-51' },
    @{ Name = 'deferred-action'; Sql = @('UPDATE `CustomAction` SET `Type` = 1075'); Diagnostic = 'type-51' },
    @{ Name = 'script-action'; Sql = @('UPDATE `CustomAction` SET `Type` = 38'); Diagnostic = 'type-51' },
    @{ Name = 'renamed-action'; Sql = @('DELETE FROM `CustomAction`',
        "INSERT INTO ``CustomAction`` (``Action``, ``Type``, ``Source``, ``Target``) VALUES ('OtherSetter', 51, 'ARPINSTALLLOCATION', '[INSTALLFOLDER]')"); Diagnostic = 'type-51' },
    @{ Name = 'other-property'; Sql = @("UPDATE ``CustomAction`` SET ``Source`` = 'OTHER'"); Diagnostic = 'type-51' },
    @{ Name = 'other-target'; Sql = @("UPDATE ``CustomAction`` SET ``Target`` = '[OTHER]'"); Diagnostic = 'type-51' },
    @{ Name = 'literal-target'; Sql = @("UPDATE ``CustomAction`` SET ``Target`` = 'C:\Kora'"); Diagnostic = 'type-51' },
    @{ Name = 'missing-ui'; Sql = @("DELETE FROM ``InstallUISequence`` WHERE ``Action`` = 'SetARPINSTALLLOCATION'"); Diagnostic = 'immediately follow' },
    @{ Name = 'missing-execute'; Sql = @("DELETE FROM ``InstallExecuteSequence`` WHERE ``Action`` = 'SetARPINSTALLLOCATION'"); Diagnostic = 'immediately follow' },
    @{ Name = 'missing-costing'; Sql = @("DELETE FROM ``InstallExecuteSequence`` WHERE ``Action`` = 'CostFinalize'"); Diagnostic = 'immediately follow' },
    @{ Name = 'early-ui'; Sql = @("UPDATE ``InstallUISequence`` SET ``Sequence`` = 999 WHERE ``Action`` = 'SetARPINSTALLLOCATION'"); Diagnostic = 'immediately follow' },
    @{ Name = 'late-execute'; Sql = @("UPDATE ``InstallExecuteSequence`` SET ``Sequence`` = 1002 WHERE ``Action`` = 'SetARPINSTALLLOCATION'"); Diagnostic = 'immediately follow' },
    @{ Name = 'conditional-ui'; Sql = @("UPDATE ``InstallUISequence`` SET ``Condition`` = 'NOT Installed' WHERE ``Action`` = 'SetARPINSTALLLOCATION'"); Diagnostic = 'unconditional' },
    @{ Name = 'conditional-execute'; Sql = @("UPDATE ``InstallExecuteSequence`` SET ``Condition`` = 'KORA_START_AT_LOGIN' WHERE ``Action`` = 'SetARPINSTALLLOCATION'"); Diagnostic = 'unconditional' }
)
foreach ($case in $cases) {
    $path = Join-Path $fixture "$($case.Name).msi"
    $database = [WixToolset.Dtf.WindowsInstaller.Database]::new(
        $path, [WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::CreateDirect)
    try {
        $sql = @(
            'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL PRIMARY KEY `Directory`)',
            'CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` CHAR(255) PRIMARY KEY `Action`)',
            "INSERT INTO ``Directory`` (``Directory``) VALUES ('INSTALLFOLDER')",
            "INSERT INTO ``CustomAction`` (``Action``, ``Type``, ``Source``, ``Target``) VALUES ('SetARPINSTALLLOCATION', 51, 'ARPINSTALLLOCATION', '[INSTALLFOLDER]')"
        )
        foreach ($table in @('InstallUISequence', 'InstallExecuteSequence')) {
            $sql += "CREATE TABLE ``$table`` (``Action`` CHAR(72) NOT NULL, ``Condition`` CHAR(255), ``Sequence`` SHORT PRIMARY KEY ``Action``)"
            $sql += "INSERT INTO ``$table`` (``Action``, ``Sequence``) VALUES ('CostFinalize', 1000)"
            $sql += "INSERT INTO ``$table`` (``Action``, ``Sequence``) VALUES ('SetARPINSTALLLOCATION', 1001)"
        }
        foreach ($statement in ($sql + $case.Sql)) {
            Write-Verbose "$($case.Name): $statement"
            $view = $database.OpenView($statement)
            try { $view.Execute() }
            finally { $view.Dispose() }
        }
        $database.Commit()
    }
    finally { $database.Dispose() }
    $failure = $null
    try { & $guard -MsiPath $path }
    catch { $failure = $_ }
    if ($null -eq $case.Diagnostic) {
        if ($null -ne $failure) { throw $failure }
    }
    elseif ($null -eq $failure -or
        -not $failure.ToString().Contains($case.Diagnostic, [StringComparison]::Ordinal)) {
        throw "Metadata contract '$($case.Name)' did not fail with '$($case.Diagnostic)': $failure"
    }
}
Write-Output "Installer metadata contracts passed: $($cases.Count) read-only database cases; no package/session action executed. Fixtures: $fixture"
