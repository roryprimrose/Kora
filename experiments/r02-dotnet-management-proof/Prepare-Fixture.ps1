[CmdletBinding()]
param([string] $PackageConfigPath = (Join-Path $PSScriptRoot 'NuGet.Config'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
if ((& dotnet --version) -ne '10.0.401') { throw 'Reviewed MG1 toolchain requires exactly .NET SDK 10.0.401.' }
$inputs = Join-Path $PSScriptRoot '.inputs'
$runtime = Join-Path $inputs 'runtime'
$rt1 = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\r02-dotnet-control-proof'))
New-Item -ItemType Directory -Path $inputs,$runtime -Force | Out-Null
$sourceArchive = Join-Path $inputs 'sdk-source.zip'
if (-not (Test-Path -LiteralPath $sourceArchive)) {
    $existingSource = Join-Path $rt1 '.candidate\sdk-source.zip'
    if (Test-Path -LiteralPath $existingSource) { Copy-Item -LiteralPath $existingSource -Destination $sourceArchive }
    else {
        & curl.exe --fail --location --silent --show-error 'https://codeload.github.com/github/copilot-sdk/zip/f8ae645902b74b62cd47aac1fd9b29adaec3aff2' --output $sourceArchive
        if ($LASTEXITCODE -ne 0) { throw 'Pinned SDK source-provenance acquisition failed.' }
    }
}
$archive = Join-Path $inputs 'runtime.tgz'
if (-not (Test-Path -LiteralPath $archive)) {
    $existing = Join-Path $rt1 '.candidate\runtime.tgz'
    if (Test-Path -LiteralPath $existing) { Copy-Item -LiteralPath $existing -Destination $archive }
    else {
        & curl.exe --fail --location --silent --show-error 'https://github.com/github/copilot-cli/releases/download/v1.0.90/github-copilot-1.0.90-win32-x64.tgz' --output $archive
        if ($LASTEXITCODE -ne 0) { throw 'Pinned runtime archive acquisition failed.' }
    }
}
if ((Get-FileHash -LiteralPath $archive).Hash -ine '2b2c23816461f7616eb16c7836af7701d6683367029405d51054c4366d1d41c0') {
    throw 'Runtime archive differs from approved RT1 bytes.'
}
$review = Join-Path $inputs 'runtime-review'
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'runtime.node'))) {
    New-Item -ItemType Directory -Path $review -Force | Out-Null
    & tar -xzf $archive -C $review
    if ($LASTEXITCODE -ne 0) { throw 'Pinned runtime extraction failed.' }
    foreach ($name in @('copilot-runtime.exe','runtime.node')) {
        Copy-Item -LiteralPath (Join-Path $review "package\prebuilds\win32-x64\$name") -Destination $runtime
    }
    Copy-Item -LiteralPath (Join-Path $review 'package\LICENSE.md') -Destination $runtime
}
foreach ($project in @('HostTests.csproj','ManagementProof.csproj')) {
    & dotnet restore (Join-Path $PSScriptRoot $project) --locked-mode --configfile $PackageConfigPath
    if ($LASTEXITCODE -ne 0) { throw "Locked restore failed for $project." }
}
$package = Join-Path $rt1 '.candidate\packages\github.copilot.sdk\1.0.16\github.copilot.sdk.1.0.16.nupkg'
if ((Get-FileHash -LiteralPath $package).Hash -ine 'c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef') {
    throw 'Released package differs from separately approved bytes.'
}
Copy-Item -LiteralPath $package -Destination (Join-Path $inputs 'GitHub.Copilot.SDK.1.0.16.nupkg')
& (Join-Path $PSScriptRoot 'Test-Inputs.ps1')

# Derive fixture pins and reviewed dependency metadata, never patch SDK/native source or historical evidence.
$snapshot = Join-Path $inputs 'rt1-snapshot.zip'
$repository = (& git -C $PSScriptRoot rev-parse --show-toplevel).Replace('/', '\')
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve own repository root.' }
& git -C $repository archive --format=zip "--output=$snapshot" 3e8558fbff07943d39721b230599b02062d90d57 experiments/r02-dotnet-control-proof
if ($LASTEXITCODE -ne 0) { throw 'Immutable RT1 fixture snapshot failed.' }
$regression = Join-Path $PSScriptRoot '.regression'
Expand-Archive -LiteralPath $snapshot -DestinationPath $regression -Force
$copy = Join-Path $regression 'experiments\r02-dotnet-control-proof'
$candidatePath = Join-Path $copy 'Candidate.cs'
$candidate = [IO.File]::ReadAllText($candidatePath)
$candidate = $candidate.Replace('1.0.16-rt1.source.f8ae645.1', '1.0.16').
    Replace('afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6', '6ed0b19fd2f9cf525074830784bb255245f15b8f08a3d6aa78fb116be5ba668b').
    Replace('0b609b73c868099d64e7d5320b927d760d528756ab7c5db0b5692f5dccbf9b3f', 'c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef')
[IO.File]::WriteAllText($candidatePath, $candidate)
$projectPath = Join-Path $copy 'ControlProof.csproj'
[xml] $project = [IO.File]::ReadAllText($projectPath)
foreach ($reference in $project.Project.ItemGroup.PackageReference) {
    $reference.RemoveAttribute('Version')
}
$project.Save($projectPath)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Directory.Packages.props') -Destination (Join-Path $copy 'Directory.Packages.props')
$evidencePath = Join-Path $copy 'Evidence.cs'
[IO.File]::WriteAllText($evidencePath, [IO.File]::ReadAllText($evidencePath).
    Replace('exact-tag-source-build, Windows x64', 'Released NuGet 1.0.16, separate MG1-owned RT1 regression, Windows x64'))
$feed = Join-Path $copy '.candidate\feed'
$native = Join-Path $copy '.candidate\runtime'
New-Item -ItemType Directory -Path $feed,$native -Force | Out-Null
Copy-Item -LiteralPath $package -Destination (Join-Path $feed 'GitHub.Copilot.SDK.1.0.16.nupkg')
foreach ($name in @('copilot-runtime.exe','runtime.node','LICENSE.md')) {
    Copy-Item -LiteralPath (Join-Path $runtime $name) -Destination $native
}
& dotnet restore $projectPath --configfile $PackageConfigPath --packages (Join-Path $rt1 '.candidate\packages') --force-evaluate
if ($LASTEXITCODE -ne 0) { throw 'Derived released-profile RT1 restore failed.' }
Write-Host 'Released MG1 inputs and independent RT1 regression fixture prepared. Original RT1 source/evidence unchanged.'
