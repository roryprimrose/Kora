[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string] $Version,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $ProductVersion,
    [string] $ApplicationPayloadPath,
    [switch] $SkipMsiValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'WiX MSI/Burn packaging requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Version) {
    $resolved = & (Join-Path $PSScriptRoot 'Get-BuildVersion.ps1') -RepositoryPath $root
    $Version = $resolved.Version
}
if (-not $ProductVersion) { $ProductVersion = ($Version -split '[-+]')[0] }
$numericVersion = [version] $ProductVersion
if ($numericVersion.Major -gt 255 -or $numericVersion.Minor -gt 255 -or $numericVersion.Build -gt 65535) {
    throw 'ProductVersion exceeds Windows Installer limits (255.255.65535).'
}
if (($Version -split '[-+]')[0] -ne $ProductVersion) {
    throw 'Version and ProductVersion must have the same major.minor.patch.'
}
$sourceRevision = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source revision.' }
$sourceChanges = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source worktree state.' }

function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

# Reject transferred-payload tampering before staging, restoring tools or invoking a compiler.
if ($ApplicationPayloadPath) {
    $ApplicationPayloadPath = [IO.Path]::GetFullPath($ApplicationPayloadPath)
    if (-not (Test-Path -LiteralPath $ApplicationPayloadPath -PathType Container)) {
        throw "Application payload does not exist: $ApplicationPayloadPath"
    }
    & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath $ApplicationPayloadPath `
        -Version $Version -SourceRevision $sourceRevision
}

# Every attempt owns a fresh staging tree; failure never overwrites an earlier candidate.
$staging = Join-Path $root "artifacts\installer\build-$([guid]::NewGuid().ToString('N'))"
$application = Join-Path $staging 'application'
$bootstrapper = Join-Path $staging 'bootstrapper'
$output = Join-Path $staging 'output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($SkipMsiValidation) {
    Write-Warning 'MSI ICE validation is explicitly skipped for this local proof. This is not validated release output.'
}
if ($SkipMsiValidation -and $env:GITHUB_ACTIONS -eq 'true') {
    throw 'CI must run full MSI ICE validation.'
}

if ($ApplicationPayloadPath) {
    $application = $ApplicationPayloadPath
}
else {
    Invoke-DotNet -Arguments @('restore', (Join-Path $root 'Kora.slnx'), '--locked-mode')
    Invoke-DotNet -Arguments @('tool', 'restore')
    & (Join-Path $PSScriptRoot 'Test-DependencyLicenses.ps1')
    Invoke-DotNet -Arguments @('publish', (Join-Path $root 'src\Kora\Kora.csproj'),
        '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false',
        '--property:RestoreLockedMode=true', "--property:Version=$Version", '--output', $application)
    foreach ($directory in @('licenses', 'package-notices')) {
        Copy-Item -LiteralPath (Join-Path $root "artifacts\license-compliance\$directory") `
            -Destination (Join-Path $application $directory) -Recurse
    }
}

foreach ($file in @('Kora.exe', 'Kora.dll', 'Kora.deps.json', 'Kora.runtimeconfig.json',
    'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $application $file) -PathType Leaf)) {
        throw "Required application payload file is missing: $file"
    }
}
foreach ($directory in @('licenses', 'package-notices')) {
    if (-not (Test-Path -LiteralPath (Join-Path $application $directory) -PathType Container)) {
        throw "Required application license directory is missing: $directory"
    }
}
$assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $application 'Kora.dll')).ProductVersion
if (($assemblyVersion -split '\+')[0] -ne ($Version -split '\+')[0]) {
    throw "Application version $assemblyVersion does not match setup version $Version."
}
if ($ApplicationPayloadPath) {
    & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath $application `
        -Version $Version -SourceRevision $sourceRevision
}
else {
    & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath $application `
        -Version $Version -SourceRevision $sourceRevision -WriteManifest
}

Invoke-DotNet -Arguments @('publish', (Join-Path $root 'installer\Kora.Setup\Kora.Setup.csproj'),
    '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true',
    '--property:RestoreLockedMode=true', "--property:Version=$Version", '--output', $bootstrapper)
foreach ($directory in @('licenses', 'package-notices')) {
    Copy-Item -LiteralPath (Join-Path $application $directory) `
        -Destination (Join-Path $bootstrapper $directory) -Recurse
}

$payloadSource = Join-Path $staging 'BootstrapperPayloads.wxs'
$xmlSettings = [Xml.XmlWriterSettings]::new()
$xmlSettings.Indent = $true
$writer = [Xml.XmlWriter]::Create($payloadSource, $xmlSettings)
try {
    $namespace = 'http://wixtoolset.org/schemas/v4/wxs'
    $writer.WriteStartElement('Wix', $namespace)
    $writer.WriteStartElement('Fragment', $namespace)
    $writer.WriteStartElement('PayloadGroup', $namespace)
    $writer.WriteAttributeString('Id', 'BootstrapperDependencies')
    foreach ($file in Get-ChildItem -LiteralPath $bootstrapper -Recurse -File | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($bootstrapper, $file.FullName)
        if ($relative -eq 'Kora.Setup.exe') { continue }
        $writer.WriteStartElement('Payload', $namespace)
        $writer.WriteAttributeString('SourceFile', $file.FullName)
        $writer.WriteAttributeString('Name', $relative)
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()
}
finally { $writer.Dispose() }

$msiProject = Join-Path $root 'installer\Kora.Msi\Kora.Msi.wixproj'
$bundleProject = Join-Path $root 'installer\Kora.Bundle\Kora.Bundle.wixproj'
Invoke-DotNet -Arguments @('restore', $msiProject, '--locked-mode')
Invoke-DotNet -Arguments @('build', $msiProject, '--configuration', 'Release', '--no-restore',
    "--property:Version=$Version", "--property:ProductVersion=$ProductVersion", "--property:ApplicationPayload=$application",
    "--property:SuppressValidation=$($SkipMsiValidation.IsPresent.ToString().ToLowerInvariant())",
    "--property:OutputPath=$output\")
$msi = Join-Path $output "Kora-$ProductVersion-win-x64.msi"
Invoke-DotNet -Arguments @('restore', $bundleProject, '--locked-mode')
$applicationExeHash = (Get-FileHash -LiteralPath (Join-Path $application 'Kora.exe') -Algorithm SHA256).Hash
$applicationAssemblyHash = (Get-FileHash -LiteralPath (Join-Path $application 'Kora.dll') -Algorithm SHA256).Hash
Invoke-DotNet -Arguments @('build', $bundleProject, '--configuration', 'Release', '--no-restore',
    "--property:Version=$Version", "--property:ProductVersion=$ProductVersion",
    "--property:BootstrapperPayload=$bootstrapper", "--property:MsiPath=$msi",
    "--property:BootstrapperPayloadsSource=$payloadSource", "--property:OutputPath=$output\",
    "--property:ApplicationExeHash=$applicationExeHash", "--property:ApplicationAssemblyHash=$applicationAssemblyHash")
$bundle = Join-Path $output "Kora-Setup-$Version-win-x64.exe"
foreach ($file in @($msi, $bundle)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Packaging did not produce $file." }
}
& (Join-Path $PSScriptRoot 'Test-Installer.ps1') -CandidatePath $output `
    -ApplicationPayloadPath $application -BootstrapperPayloadPath $bootstrapper `
    -Version $Version -ProductVersion $ProductVersion
Copy-Item -LiteralPath (Join-Path $application 'payload-manifest.json') -Destination $output

$receipt = [ordered] @{
    version = $Version
    productVersion = $ProductVersion
    sourceRevision = $sourceRevision
    sourceDirty = -not [string]::IsNullOrEmpty(($sourceChanges -join "`n"))
    buildOrigin = if ($env:GITHUB_ACTIONS -eq 'true') { 'github-actions' } else { 'local' }
    wixVersion = '7.0.0'
    unsigned = $true
    msiIceValidation = if ($SkipMsiValidation) { 'skipped-explicitly' } else { 'passed' }
    packageInspection = 'passed'
    files = @($msi, $bundle) | ForEach-Object {
        @{ name = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    }
}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'installer-build.json')
if ($env:GITHUB_OUTPUT) {
    "installer-path=$output" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
}
Write-Host "Unsigned installer candidate: $bundle"
Write-Host 'No package was installed, application launched, or release published.'
