[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) "KoraVersionTests-$([guid]::NewGuid().ToString('N'))"
$saved = @{}
foreach ($name in @('GITHUB_ACTIONS', 'GITHUB_EVENT_NAME', 'GITHUB_REF', 'GITHUB_REPOSITORY', 'GITHUB_SHA')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $null)
}

function Invoke-FixtureGit {
    param([string[]] $Arguments)
    & (Get-Command git -CommandType Application).Source -C $fixture @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $($Arguments -join ' ')" }
}
function Resolve { & (Join-Path $PSScriptRoot 'Get-BuildVersion.ps1') -RepositoryPath $fixture }
function Assert {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}
function Reject {
    param([scriptblock] $Operation)
    $rejected = $false
    try { & $Operation | Out-Null }
    catch { $rejected = $true }
    Assert $rejected 'Expected versioning to fail closed.'
}
function Assert-BinaryVersion {
    param([string] $Expected)
    & dotnet build (Join-Path $fixture 'VersionFixture.csproj') --configuration Release --nologo `
        --property:KoraResolveBuildVersion=true "--property:KoraVersionRepositoryPath=$fixture$([IO.Path]::DirectorySeparatorChar)"
    if ($LASTEXITCODE -ne 0) { throw 'Version fixture build failed.' }
    $binary = Join-Path $fixture 'bin\Release\net10.0\VersionFixture.dll'
    Assert ([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).ProductVersion -ceq $Expected) `
        "Compiled binary metadata did not match $Expected."
}

try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'GitVersion.yml'), (Join-Path $root 'dotnet-tools.json'),
        (Join-Path $root 'global.json') -Destination $fixture
    Invoke-FixtureGit -Arguments @('init', '-b', 'main')
    Invoke-FixtureGit -Arguments @('config', 'user.name', 'Version tests')
    Invoke-FixtureGit -Arguments @('config', 'user.email', 'version-tests@example.invalid')
    Invoke-FixtureGit -Arguments @('add', '.')
    Invoke-FixtureGit -Arguments @('commit', '-m', 'Initial version fixture')
    $targets = [Security.SecurityElement]::Escape((Join-Path $root 'Directory.Build.targets'))
    [IO.File]::WriteAllText((Join-Path $fixture 'VersionFixture.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
  </PropertyGroup>
  <Import Project="$targets" />
</Project>
"@)
    $first = Resolve
    Assert ($first.Version -cmatch '^0\.1\.0-beta\d+$') "Incorrect initial main version: $($first.Version)"
    Assert (-not $first.Publish) 'Local main must not publish.'
    Assert-BinaryVersion $first.Version
    Invoke-FixtureGit -Arguments @('commit', '--allow-empty', '-m', 'Second main commit')
    $second = Resolve
    Assert ($second.Version -cmatch '^0\.1\.0-beta\d+$' -and $second.Version -ne $first.Version) 'Main beta increment did not advance.'
    Invoke-FixtureGit -Arguments @('tag', "v$($second.Version)")
    $repeat = Resolve
    Assert ($repeat.Version -eq $second.Version) 'A generated beta release tag changed its own rerun version.'
    Invoke-FixtureGit -Arguments @('commit', '--allow-empty', '-m', 'Third main commit')
    $third = Resolve
    Assert ($third.Version -cmatch '^0\.1\.0-beta\d+$' -and $third.Version -ne $second.Version) 'Beta release tags broke subsequent GitVersion increments.'
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_REPOSITORY = 'roryprimrose/Kora'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_SHA = & git -C $fixture rev-parse HEAD
    $mainCi = Resolve
    Assert ($mainCi.Version -eq $third.Version -and $mainCi.Prerelease -and $mainCi.Publish) 'Main CI did not publish the same beta version.'
    $env:GITHUB_ACTIONS = $null
    Invoke-FixtureGit -Arguments @('tag', 'v0.1.0')
    $stable = Resolve
    Assert ($stable.Version -eq '0.1.0' -and -not $stable.Prerelease) 'Stable main tag did not produce a stable version.'
    Assert-BinaryVersion $stable.Version
    Invoke-FixtureGit -Arguments @('commit', '--allow-empty', '-m', 'Main after stable release')
    Assert ((Resolve).Version -cmatch '^\d+\.\d+\.\d+-beta\d+$') 'Untagged main after stable release is not beta.'
    Invoke-FixtureGit -Arguments @('remote', 'add', 'origin', $fixture)
    Invoke-FixtureGit -Arguments @('update-ref', 'refs/remotes/origin/main', 'HEAD')
    Invoke-FixtureGit -Arguments @('checkout', '--detach', 'v0.1.0')
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_REPOSITORY = 'roryprimrose/Kora'
    $env:GITHUB_REF = 'refs/tags/v0.1.0'
    $env:GITHUB_SHA = & git -C $fixture rev-parse HEAD
    $tagged = Resolve
    Assert ($tagged.Version -eq '0.1.0' -and $tagged.Publish) 'Main tag CI did not publish stable.'
    $env:GITHUB_REPOSITORY = 'fork/Kora'
    Assert (-not (Resolve).Publish) 'Fork acquired publication authority.'
    Invoke-FixtureGit -Arguments @('checkout', '-b', 'feature/versioning')
    $env:GITHUB_REF = 'refs/heads/feature/versioning'
    Assert ((Resolve).Version -eq '0.1.0' -and -not (Resolve).Publish) 'Feature CI is not the proof version.'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_EVENT_NAME = 'pull_request'
    Assert ((Resolve).Version -eq '0.1.0' -and -not (Resolve).Publish) 'PR acquired main version/publication.'
    $env:GITHUB_ACTIONS = $null
    Assert ((Resolve).Version -eq '0.1.0') 'Local feature is not 0.1.0.'
    Assert-BinaryVersion '0.1.0'
    Invoke-FixtureGit -Arguments @('commit', '--allow-empty', '-m', 'Feature-only stable tag')
    Invoke-FixtureGit -Arguments @('tag', 'v9.0.0')
    $env:GITHUB_SHA = & git -C $fixture rev-parse HEAD
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_REF = 'refs/tags/v9.0.0'
    Reject { Resolve }
    $env:GITHUB_REF = 'refs/tags/v0.1.0-beta1'
    Reject { Resolve }
    $env:GITHUB_REF = 'refs/heads/feature/versioning'
    $env:GITHUB_SHA = '0' * 40
    Reject { Resolve }
    Write-Host 'Version tests passed: main beta increments/reruns, stable tags, local features, PRs, forks and off-main rejection.'
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
