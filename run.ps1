#requires -Version 7.0
<#
.SYNOPSIS
Update the current checkout, restore dependencies, build and run Kora in Debug.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Kora requires Windows. Run this script in PowerShell 7 on Windows.'
}

Push-Location -LiteralPath $PSScriptRoot
try {
    & git pull --ff-only
    if ($LASTEXITCODE -ne 0) { throw 'git pull failed. Resolve the branch/upstream or local changes, then retry.' }

    & dotnet restore .\src\Kora\Kora.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }

    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Local .NET tool restore failed.' }

    & dotnet build .\src\Kora\Kora.csproj --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Debug build failed.' }

    & dotnet run --project .\src\Kora\Kora.csproj --configuration Debug --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Kora exited with an error.' }
}
finally {
    Pop-Location
}
