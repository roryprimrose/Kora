[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $MsiPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
Add-Type -Path (Join-Path $packageRoot 'wixtoolset.sdk\7.0.0\tools\net8.0\WixToolset.Dtf.WindowsInstaller.dll')
$database = [WixToolset.Dtf.WindowsInstaller.Database]::new(
    [IO.Path]::GetFullPath($MsiPath), [WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
try {
    $view = $database.OpenView('SELECT `Directory` FROM `Directory` WHERE `Directory` = ''ARPINSTALLLOCATION''')
    try {
        $view.Execute()
        $record = $view.Fetch()
        if ($null -ne $record) {
            $record.Dispose()
            throw 'ARPINSTALLLOCATION is a reserved MSI property, not an installation-directory identifier (ICE99).'
        }
    }
    finally { $view.Dispose() }

    $view = $database.OpenView('SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`')
    try {
        $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) { throw 'MSI requires exactly one install-location metadata setter.' }
        try {
            if ($record.GetString(1) -cne 'SetARPINSTALLLOCATION' -or $record.GetInteger(2) -ne 51 -or
                $record.GetString(3) -cne 'ARPINSTALLLOCATION' -or $record.GetString(4) -cne '[INSTALLFOLDER]') {
                throw 'MSI permits only the immediate type-51 ARPINSTALLLOCATION metadata setter; executable and other custom actions are forbidden.'
            }
        }
        finally { $record.Dispose() }
        $record = $view.Fetch()
        if ($null -ne $record) {
            $record.Dispose()
            throw 'MSI permits no additional custom actions.'
        }
    }
    finally { $view.Dispose() }

    foreach ($table in @('InstallUISequence', 'InstallExecuteSequence')) {
        $view = $database.OpenView(
            "SELECT ``Action``, ``Condition``, ``Sequence`` FROM ``$table`` WHERE ``Action`` = 'CostFinalize' OR ``Action`` = 'SetARPINSTALLLOCATION'")
        try {
            $view.Execute()
            $sequences = @{}
            while ($null -ne ($record = $view.Fetch())) {
                try {
                    if (-not [string]::IsNullOrEmpty($record.GetString(2))) {
                        throw 'MSI install-location metadata costing/setter must be unconditional in both sequences.'
                    }
                    $sequences[$record.GetString(1)] = $record.GetInteger(3)
                }
                finally { $record.Dispose() }
            }
            if ($sequences.Count -ne 2 -or $sequences['CostFinalize'] -le 0 -or
                $sequences['SetARPINSTALLLOCATION'] -ne ($sequences['CostFinalize'] + 1)) {
                throw 'MSI install-location metadata setter must immediately follow CostFinalize in both sequences.'
            }
        }
        finally { $view.Dispose() }
    }
}
finally { $database.Dispose() }
