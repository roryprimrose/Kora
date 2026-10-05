[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Root,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $Revision
)
. (Join-Path $PSScriptRoot 'ManagedSource.ps1')
foreach ($command in 'git', 'dotnet') { Get-Command $command -ErrorAction Stop | Out-Null }
$inspector = Join-Path $PSScriptRoot 'Inspect-Publish.ps1'
Invoke-ManagedSource -Root $Root -Repository 'https://github.com/roryprimrose/Kora.git' -Revision $Revision -BuildAndInspect {
    param($checkout, $payload, $stage)
    Push-Location $checkout
    try {
        $sdk = (& dotnet --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $sdk -ne '10.0.401') {
            throw "Install the reviewed SDK 10.0.401; observed $sdk. No automatic SDK installation."
        }
        Invoke-Checked 'dotnet' @(
            'publish', (Join-Path $checkout 'src\Kora\Kora.csproj'), '--configuration', 'Release', '--runtime', 'win-x64',
            '--self-contained', 'false', '--property:RestoreLockedMode=true',
            '--property:ContinuousIntegrationBuild=true', '--output', $payload
        )
        & $inspector -Payload $payload -Revision $Revision -EvidenceDirectory (Join-Path $stage 'inspection') `
            -BuildOrigin "Managed local source build; SDK $sdk; $([Runtime.InteropServices.RuntimeInformation]::OSDescription)"
        Write-ProofJson ([ordered]@{
            sdk = $sdk
            os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
            source = $Revision
            dependencyLocks = @(
                Get-ChildItem -LiteralPath $checkout -Recurse -Filter 'packages.lock.json' |
                    ForEach-Object {
                        [ordered]@{
                            path = [IO.Path]::GetRelativePath($checkout, $_.FullName).Replace('\', '/')
                            sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
                        }
                    }
            )
            smoke = 'Static publish contract only. Runtime launch requires approved Windows lab.'
        }) (Join-Path $stage 'build.json')
    }
    finally { Pop-Location }
}
