[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string] $SourceRevision,

    [Parameter()]
    [string] $ModuleDirectory = 'tests\Kora.Windows.IntegrationTests\bin\Release\net10.0-windows10.0.19041.0\win-x64',

    [Parameter(Mandatory)]
    [string] $OutputArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$modules = [IO.Path]::GetFullPath($ModuleDirectory, $root)
$archive = [IO.Path]::GetFullPath($OutputArchive, $root)
$receiptPath = Join-Path $modules 'kora-test-build.json'
$catalogPath = Join-Path $modules 'kora-test-catalog.json'
if (Test-Path -LiteralPath $archive) { throw 'The Windows test archive already exists; use a fresh output.' }
if (-not (Test-Path -LiteralPath (Join-Path $modules 'Kora.Windows.IntegrationTests.dll') -PathType Leaf)) {
    throw 'The built Windows test assembly is missing.'
}
$dotnet = (Get-Command dotnet -CommandType Application, ExternalScript -ErrorAction Stop | Select-Object -First 1).Source
$catalog = @(& $dotnet test --root-directory $modules --test-modules 'Kora.Windows.IntegrationTests.dll' --list-tests)
if ($LASTEXITCODE -ne 0) { throw 'Unfiltered Windows test artifact discovery failed.' }
$methods = @{}
$testNames = @{}
foreach ($line in $catalog) {
    if ($line -cmatch '^  (?<method>Kora\.Windows\.IntegrationTests\.[A-Za-z0-9_.]+)(?:\(|\s|$)') {
        $name = $Matches['method']
        if (-not $methods.ContainsKey($name)) { $methods[$name] = 0 }
        $methods[$name]++
        $displayName = $line.Substring(2)
        if (-not $testNames.ContainsKey($displayName)) { $testNames[$displayName] = 0 }
        $testNames[$displayName]++
    }
}
$totals = @($catalog | Select-String '^Discovered (\d+) tests' |
    ForEach-Object { [int] $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
$total = ($methods.Values | Measure-Object -Sum).Sum
if ($totals.Count -ne 1 -or $total -le 0 -or $total -ne $totals[0]) {
    throw 'The unfiltered Windows test catalog is missing, empty or incomplete.'
}
[pscustomobject]@{
    Version = 1
    SourceRevision = $SourceRevision
    Total = $total
    Methods = @($methods.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{ Name = $_; Cases = $methods[$_] } })
    Tests = @($testNames.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{ Name = $_; Cases = $testNames[$_] } })
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $catalogPath -Encoding utf8
$files = @(Get-ChildItem -LiteralPath $modules -Recurse -File | Where-Object FullName -CNE $receiptPath |
    Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            Path = [IO.Path]::GetRelativePath($modules, $_.FullName)
            Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
[pscustomobject]@{ Version = 1; SourceRevision = $SourceRevision; Files = $files } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding utf8
New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
Copy-Item -LiteralPath $catalogPath -Destination (Join-Path (Split-Path -Parent $archive) 'kora-test-catalog.json')
[IO.Compression.ZipFile]::CreateFromDirectory($modules, $archive)
Write-Host "Published exact-source Windows test archive: $total cases, $($files.Count) files."
