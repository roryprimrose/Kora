[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $CandidatePath,
    [Parameter(Mandatory)][string] $ApplicationPayloadPath,
    [Parameter(Mandatory)][string] $BootstrapperPayloadPath,
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $ProductVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$candidate = [IO.Path]::GetFullPath($CandidatePath)
# WiX's .NET Framework extraction host can still hit MAX_PATH with notice filenames.
$inspection = Join-Path ([IO.Path]::GetDirectoryName($candidate)) "i-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
$wix = Join-Path $packageRoot 'wixtoolset.sdk\7.0.0\tools\net472\x64\wix.exe'
$msi = Join-Path $candidate "Kora-$ProductVersion-win-x64.msi"
$bundle = Join-Path $candidate "Kora-Setup-$Version-win-x64.exe"
$ba = Join-Path $inspection 'ba'
$chain = Join-Path $inspection 'chain'
New-Item -ItemType Directory -Path $inspection -Force | Out-Null

& $wix burn extract -acceptEula wix7 $bundle -oba $ba -o $chain
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the built Burn bundle.' }
$manifest = [xml](Get-Content -LiteralPath (Join-Path $ba 'manifest.xml') -Raw)
$burnNamespace = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$burnNamespace.AddNamespace('b', $manifest.DocumentElement.NamespaceURI)
$chainPackages = @($manifest.SelectNodes('/b:BurnManifest/b:Chain/*', $burnNamespace))
foreach ($identity in @(
    @{ Variable = 'KoraDisplayVersion'; Expected = $Version },
    @{ Variable = 'KoraProductVersion'; Expected = $ProductVersion }
)) {
    $variable = $manifest.SelectSingleNode("/b:BurnManifest/b:Variable[@Id='$($identity.Variable)']", $burnNamespace)
    if ($null -eq $variable -or $variable.Value -cne $identity.Expected) {
        throw 'Burn UI/path versions do not match the shared build version.'
    }
}
foreach ($binary in @((Join-Path $ApplicationPayloadPath 'Kora.dll'), (Join-Path $BootstrapperPayloadPath 'Kora.Setup.dll'))) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).ProductVersion -cne $Version) {
        throw 'Application and bootstrapper informational versions must exactly match the shared build version.'
    }
}
foreach ($identity in @(
    @{ Variable = 'KoraApplicationExeHash'; File = 'Kora.exe' },
    @{ Variable = 'KoraApplicationAssemblyHash'; File = 'Kora.dll' }
)) {
    $variable = $manifest.SelectSingleNode("/b:BurnManifest/b:Variable[@Id='$($identity.Variable)']", $burnNamespace)
    $expected = (Get-FileHash -LiteralPath (Join-Path $ApplicationPayloadPath $identity.File) -Algorithm SHA256).Hash
    if ($null -eq $variable -or $variable.Value -cne $expected) {
        throw 'Completion launch must pin the exact published application, not a staging path or ambient executable.'
    }
}
if (($chainPackages.Id -join ',') -cne 'DesktopRuntime,VCRuntime,KoraMsi') {
    throw 'Required runtimes must precede Kora in the native Burn chain.'
}
$koraMsiPackage = $manifest.SelectSingleNode("/b:BurnManifest/b:Chain/b:MsiPackage[@Id='KoraMsi']", $burnNamespace)
if ($koraMsiPackage.Scope -cne 'perUserOrMachine') {
    throw 'Burn must retain Kora as a configurable-scope MSI defaulting to per-user.'
}
$startupProperty = $koraMsiPackage.SelectSingleNode("b:MsiProperty[@Id='KORA_START_AT_LOGIN' or @Name='KORA_START_AT_LOGIN']", $burnNamespace)
if ($null -eq $startupProperty -or $startupProperty.Value -cne '[KoraStartAtLogin]') {
    throw 'Burn must pass the approved startup choice to the MSI explicitly.'
}
$authoring = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\installer\Kora.Bundle\Bundle.wxs') -Raw)
foreach ($id in @('DesktopRuntime', 'VCRuntime')) {
    $package = $manifest.SelectSingleNode("/b:BurnManifest/b:Chain/b:ExePackage[@Id='$id']", $burnNamespace)
    if ($package.Permanent -cne 'yes' -or $package.Vital -cne 'yes' -or $package.Scope -cne 'perMachine' -or
        $package.InstallArguments -cne '/install /quiet /norestart' -or $package.Cache -cne 'remove') {
        throw "Required prerequisite scope/retention/consent contract is invalid: $id"
    }
    $sourcePackage = $authoring.SelectSingleNode("//*[local-name()='ExePackage' and @Id='$id']")
    if ($package.DetectCondition -cne $sourcePackage.DetectCondition) {
        throw "Prerequisite detection condition changed during binding: $id"
    }
    $payloadId = $package.SelectSingleNode('b:PayloadRef', $burnNamespace).Id
    $payload = $manifest.SelectSingleNode("/b:BurnManifest/b:Payload[@Id='$payloadId']", $burnNamespace)
    $sourcePayload = $sourcePackage.FirstChild
    if ($payload.DownloadUrl -cne $sourcePayload.DownloadUrl -or $payload.Hash -cne $sourcePayload.Hash -or
        $payload.FileSize -cne $sourcePayload.Size -or $payload.Packaging -cne 'external') {
        throw "Prerequisite download identity changed during binding: $id"
    }
}
$embeddedMsi = Join-Path $chain "WixAttachedContainer\Kora-$ProductVersion-win-x64.msi"
if ((Get-FileHash -LiteralPath $msi).Hash -cne (Get-FileHash -LiteralPath $embeddedMsi).Hash) {
    throw 'Burn did not embed the exact built MSI.'
}

$bootstrapper = [IO.Path]::GetFullPath($BootstrapperPayloadPath)
foreach ($file in Get-ChildItem -LiteralPath $bootstrapper -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($bootstrapper, $file.FullName)
    $embeddedFile = Join-Path $ba $relative
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -cne (Get-FileHash -LiteralPath $embeddedFile).Hash) {
        throw "Burn bootstrapper payload digest mismatch: $relative"
    }
}
foreach ($file in @('Kora.Setup.exe', 'mbanative.dll', 'WixToolset.BootstrapperApplicationApi.dll',
    'coreclr.dll', 'Kora.Setup.runtimeconfig.json', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ba $file))) { throw "Burn UI dependency missing: $file" }
}

$decompiledPath = Join-Path $inspection 'Package.wxs'
& $wix msi decompile -acceptEula wix7 $msi -o $decompiledPath
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the built MSI.' }
$package = [xml](Get-Content -LiteralPath $decompiledPath -Raw)
$namespaces = [Xml.XmlNamespaceManager]::new($package.NameTable)
$namespaces.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')
if ($package.SelectSingleNode('/w:Wix/w:Package', $namespaces).Version -cne $ProductVersion) {
    throw 'MSI product version differs from the selected version.'
}
if ($package.SelectSingleNode('/w:Wix/w:Package', $namespaces).Scope -cne 'perUserOrMachine') {
    throw 'MSI must support both installation contexts and default to per-user.'
}
if (-not $package.SelectSingleNode("//w:StandardDirectory[@Id='ProgramFiles64Folder']/w:Directory[@Name='Kora']/w:Directory[@Id='INSTALLFOLDER']", $namespaces)) {
    throw 'MSI must use the context-redirected x64 Programs directory for user/machine installs.'
}
if (-not $package.SelectSingleNode("//w:Shortcut[@Id='KoraShortcut' and @Advertise='yes' and @Directory='KoraStartMenu']", $namespaces) -or
    -not $package.SelectSingleNode("//w:StandardDirectory[@Id='ProgramMenuFolder']/w:Directory[@Id='KoraStartMenu']", $namespaces)) {
    throw 'MSI must create an advertised shortcut in the context-redirected Start menu.'
}
if ($package.SelectSingleNode("//w:Shortcut[@Id='KoraShortcut']", $namespaces).Icon -cne 'KoraIcon.exe' -or
    $package.SelectSingleNode("//w:Property[@Id='ARPPRODUCTICON']", $namespaces).Value -cne 'KoraIcon.exe' -or
    $null -eq $package.SelectSingleNode("//w:Icon[@Id='KoraIcon.exe']", $namespaces)) {
    throw 'MSI shortcut and product icons must use an icon-table name matching the executable extension.'
}
if ($package.SelectSingleNode("//w:Directory[@Id='INSTALLFOLDER']", $namespaces).Name -cne $ProductVersion) {
    throw 'MSI version directory differs from the selected product version.'
}
if ($package.SelectNodes('//w:CustomAction', $namespaces).Count -ne 0) {
    throw 'MSI must not launch Kora with the installer token or contain custom actions.'
}
$startupComponent = $package.SelectSingleNode("//w:Component[@Id='KoraStartupComponent']", $namespaces)
$startupRegistry = $startupComponent.SelectSingleNode('w:RegistryValue', $namespaces)
if ($startupComponent.Condition -cne 'KORA_START_AT_LOGIN = 1' -or $startupComponent.Transitive -cne 'yes' -or
    [Guid]::Parse($startupComponent.Guid) -ne [Guid]::Parse('B6FBBAD7-AC0F-475A-953D-2E588331FEA9') -or
    $startupRegistry.Root -cne 'HKMU' -or
    $startupRegistry.Key -cne 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -or
    $startupRegistry.Name -cne 'Kora' -or $startupRegistry.Value -cne '"[INSTALLFOLDER]Kora.exe"' -or
    $startupRegistry.Type -cne 'string' -or $startupRegistry.KeyPath -cne 'yes') {
    throw 'Startup must be a conditional, scope-matched, MSI-owned quoted installed-executable value.'
}
if ($package.SelectSingleNode("//w:Property[@Id='KORA_START_AT_LOGIN']", $namespaces).Value -cne '0') {
    throw 'Bare MSI must not silently authorize startup registration.'
}
$platformSearch = $package.SelectSingleNode("//w:Property[@Id='KORA_WINDOWS_BUILD']/w:RegistrySearch", $namespaces)
if ($null -eq $platformSearch -or $platformSearch.Root -cne 'HKLM' -or
    $platformSearch.Key -cne 'SOFTWARE\Microsoft\Windows NT\CurrentVersion' -or
    $platformSearch.Name -cne 'CurrentBuildNumber' -or $platformSearch.Type -cne 'raw' -or
    $platformSearch.Bitness -cne 'always64') {
    throw 'MSI must obtain the real Windows build from the native 64-bit OS registry.'
}
$platformCondition = $package.SelectSingleNode('//w:Launch', $namespaces).Condition
if ($platformCondition -cne 'REMOVE = "ALL" OR (VersionNT64 AND KORA_WINDOWS_BUILD >= 22000)') {
    throw 'MSI must enforce Windows 11 x64 for installation while permitting removal.'
}
Add-Type -Path (Join-Path $packageRoot 'wixtoolset.sdk\7.0.0\tools\net8.0\WixToolset.Dtf.WindowsInstaller.dll')
$database = [WixToolset.Dtf.WindowsInstaller.Database]::new($msi, [WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
try {
    $view = $database.OpenView('SELECT `Language` FROM `File` WHERE `File` = ''KoraBootstrapSqlite''')
    try {
        $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) { throw 'MSI must include the explicit bootstrap SQLite native file.' }
        try {
            if ($record.GetString(1) -cne '0') {
                throw 'Bootstrap SQLite must retain language-neutral MSI metadata.'
            }
        }
        finally { $record.Dispose() }
    }
    finally { $view.Dispose() }
}
finally { $database.Dispose() }
$platformSession = [WixToolset.Dtf.WindowsInstaller.Installer]::OpenPackage($msi, $true)
try {
    # AppSearch only reads metadata; no install, execute or custom actions are run.
    $platformSession.DoAction('AppSearch')
    $buildNumber = 0
    if (-not [int]::TryParse($platformSession['KORA_WINDOWS_BUILD'], [ref] $buildNumber)) {
        throw 'Native MSI AppSearch did not return a valid OS build.'
    }
    $expectedSupport = [OperatingSystem]::IsWindowsVersionAtLeast(10, 0, 22000)
    if ($platformSession.EvaluateCondition($platformCondition) -ne $expectedSupport) {
        throw 'Native MSI platform condition disagrees with the actual Windows version.'
    }
    $platformSession['REMOVE'] = ''
    $platformSession['VersionNT64'] = '603'
    foreach ($case in @(
        @{ Build = ''; Allowed = $false },
        @{ Build = 'unknown'; Allowed = $false },
        @{ Build = '9600'; Allowed = $false },
        @{ Build = '21999'; Allowed = $false },
        @{ Build = '22000'; Allowed = $true },
        @{ Build = '26300'; Allowed = $true }
    )) {
        $platformSession['KORA_WINDOWS_BUILD'] = $case.Build
        if ($platformSession.EvaluateCondition($platformCondition) -ne $case.Allowed) {
            throw "Native MSI platform gate failed for build '$($case.Build)'."
        }
    }
    $platformSession['VersionNT64'] = ''
    if ($platformSession.EvaluateCondition($platformCondition)) {
        throw 'MSI platform gate admitted a non-64-bit OS.'
    }
    $platformSession['KORA_WINDOWS_BUILD'] = ''
    $platformSession['REMOVE'] = 'ALL'
    if (-not $platformSession.EvaluateCondition($platformCondition)) {
        throw 'MSI platform gate blocked recovery removal.'
    }
    foreach ($case in @(@{ Choice = '0'; Allowed = $false }, @{ Choice = '1'; Allowed = $true },
        @{ Choice = ''; Allowed = $false }, @{ Choice = 'unknown'; Allowed = $false })) {
        $platformSession['KORA_START_AT_LOGIN'] = $case.Choice
        if ($platformSession.EvaluateCondition($startupComponent.Condition) -ne $case.Allowed) {
            throw "Native MSI startup condition admitted an invalid choice '$($case.Choice)'."
        }
    }
}
finally { $platformSession.Dispose() }
foreach ($perUser in @('1', '')) {
    foreach ($choice in @('0', '1')) {
        $costSession = [WixToolset.Dtf.WindowsInstaller.Installer]::OpenPackage($msi, $true)
        try {
            $costSession['ALLUSERS'] = '2'
            $costSession['MSIINSTALLPERUSER'] = $perUser
            $costSession['KORA_START_AT_LOGIN'] = $choice
            foreach ($action in @('AppSearch', 'CostInitialize', 'FileCost', 'CostFinalize')) {
                $costSession.DoAction($action)
            }
            $state = $costSession.Components['KoraStartupComponent'].RequestState
            if (($choice -eq '1' -and $state -ne [WixToolset.Dtf.WindowsInstaller.InstallState]::Local) -or
                ($choice -eq '0' -and $state -eq [WixToolset.Dtf.WindowsInstaller.InstallState]::Local)) {
                throw 'Native MSI costing did not honor the explicit startup choice.'
            }
            $command = $costSession.Format($startupRegistry.Value)
            $executable = $costSession.Format('[#KoraExecutable]')
            if ($command -cne "`"$executable`"" -or
                -not $command.EndsWith("\Kora\$ProductVersion\Kora.exe`"", [StringComparison]::Ordinal)) {
                throw 'Startup command did not resolve to the exact quoted installed application in the selected scope.'
            }
        }
        finally { $costSession.Dispose() }
    }
}
$actualFiles = @($package.SelectNodes('//w:File', $namespaces) | ForEach-Object {
    $segments = [Collections.Generic.List[string]]::new()
    $segments.Add($_.Name)
    $parent = $_.ParentNode
    while ($null -ne $parent -and $parent.LocalName -ne 'Package') {
        if ($parent.LocalName -eq 'Directory') {
            if ($parent.Id -eq 'INSTALLFOLDER') { break }
            $segments.Insert(0, $parent.Name)
        }
        $parent = $parent.ParentNode
    }
    [string]::Join('\', $segments)
} | Sort-Object)
$application = [IO.Path]::GetFullPath($ApplicationPayloadPath)
$expectedFiles = @(Get-ChildItem -LiteralPath $application -Recurse -File |
    Where-Object FullName -NE (Join-Path $application 'payload-manifest.json') | ForEach-Object {
        [IO.Path]::GetRelativePath($application, $_.FullName)
    } | Sort-Object)
if ($actualFiles.Count -ne $expectedFiles.Count) { throw 'MSI application file count differs from the complete payload.' }
for ($index = 0; $index -lt $actualFiles.Count; $index++) {
    if ($actualFiles[$index] -cne $expectedFiles[$index]) {
        throw "MSI application layout differs from the payload: $($expectedFiles[$index])"
    }
}
Write-Host "Verified dual-scope MSI/Burn, read-only platform/startup conditions and costing, contextual Start menu, runtime acquisition/cache contracts, exact MSI/UI digests and $($actualFiles.Count) MSI application paths. No package installed."
