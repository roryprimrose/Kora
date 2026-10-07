. (Join-Path $PSScriptRoot 'SourceCheckout.Common.ps1')
. (Join-Path $PSScriptRoot 'NativeInspection.Common.ps1')

$script:SourceBootstrapVersion = '1.1.0'
$script:SourceRepository = $script:KoraSourceRepository
$script:SourceMaintenance = 'External operator; build-only, no updater or registration.'
$script:SourceActivation = 'Unavailable: requires separate approved protected-deployment and installed gates.'

function Read-SourceJson {
    param([string] $Path)
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable }
    catch { throw "Invalid source receipt/configuration at ${Path}: $($_.Exception.Message)" }
}

function Assert-SourceFields {
    param([System.Collections.IDictionary] $Value, [System.Collections.IDictionary] $Expected)
    if ($null -eq $Value) { throw 'Source identity/ownership mismatch: missing object.' }
    foreach ($key in $Expected.Keys) {
        $actual = $Value[$key]
        $expectedValue = $Expected[$key]
        $validType = if ($expectedValue -is [bool]) { $actual -is [bool] }
            elseif ($expectedValue -is [int]) { $actual -is [int] -or $actual -is [long] }
            else { $actual -is [string] }
        if (!$Value.Contains($key) -or !$validType -or $actual -cne $expectedValue) {
            throw "Source identity/ownership mismatch: $key."
        }
    }
}

function Get-SourceToolFiles {
    param([string] $ToolsRoot = (Join-Path $PSScriptRoot '..'))
    $base = [IO.Path]::GetFullPath($ToolsRoot)
    @(
        foreach ($relative in Get-SourceToolPaths) {
            [ordered]@{ path = $relative.Replace('/', '\'); sha256 = (Get-FileHash -LiteralPath (Join-Path $base $relative)).Hash.ToLowerInvariant() }
        }
    )
}

function Assert-DistributedSourceTools {
    param([string] $Revision, [string] $ToolsRoot = (Join-Path $PSScriptRoot '..'))
    $base = [IO.Path]::GetFullPath($ToolsRoot)
    $manifestPath = Join-Path $base 'source-tools.json'
    if (!(Test-Path -LiteralPath $manifestPath)) {
        if (!(Test-Path -LiteralPath (Join-Path $base 'Kora.slnx') -PathType Leaf)) {
            throw 'Distributed source-tool manifest is missing; no build admitted.'
        }
        return
    }
    Assert-NoLinks $base
    $manifest = Read-SourceJson $manifestPath
    Assert-SourceFields $manifest ([ordered]@{
        schema = 1; bootstrapVersion = $script:SourceBootstrapVersion
        repository = $script:SourceRepository; revision = $Revision
        unsigned = $true; productionAccepted = $false; activation = 'unavailable'
    })
    $tools = @(Get-SourceToolFiles $base)
    if (@($manifest.files).Count -ne $tools.Count) { throw 'Distributed source-tool inventory changed.' }
    foreach ($tool in $tools) {
        $records = @($manifest.files | Where-Object path -CEQ $tool.path.Replace('\', '/'))
        if ($records.Count -ne 1 -or $records[0].sha256 -cne $tool.sha256 -or
            $records[0].bytes -ne (Get-Item -LiteralPath (Join-Path $base $tool.path)).Length) {
            throw 'Distributed source-tool bytes changed; retain for review, do not build.'
        }
    }
}

function Get-SourceInputs {
    param([string] $Checkout)
    $paths = @(& git -C $Checkout ls-files -- '*.json' '*.props' '*.targets' '*.csproj' '*.slnx' '*.yml' '*.config')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory pinned source inputs.' }
    if ($paths -notcontains 'global.json' -or !($paths -like '*packages.lock.json')) {
        throw 'Source must include global.json and dependency locks.'
    }
    @($paths | Sort-Object | ForEach-Object {
        [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $Checkout $_)).Hash.ToLowerInvariant() }
    })
}

function Assert-SourceRoot {
    param([string] $Root)
    if (![IO.Path]::IsPathFullyQualified($Root)) { throw 'Managed root must be an absolute dedicated path.' }
    $Root = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($Root -eq [IO.Path]::GetPathRoot($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)) {
        throw 'A filesystem root is not a dedicated managed root.'
    }
    if ($IsWindows) {
        foreach ($folder in 'ProgramFiles', 'ProgramFilesX86', 'Windows') {
            $protectedRoot = [Environment]::GetFolderPath($folder)
            if ($protectedRoot -and ($Root -eq $protectedRoot -or $Root.StartsWith(
                $protectedRoot + '\', [StringComparison]::OrdinalIgnoreCase))) {
                throw 'Live Program Files/Windows paths are unavailable to build-only source delivery.'
            }
        }
    }
    $ancestor = $Root
    while (!(Test-Path -LiteralPath $ancestor)) { $ancestor = Split-Path -Parent $ancestor }
    Assert-NoLinks $ancestor -AncestorsOnly
    $probe = $ancestor
    while ($probe) {
        if (Test-Path -LiteralPath (Join-Path $probe '.git')) {
            throw 'Managed root must be outside every existing source repository/worktree.'
        }
        $probe = Split-Path -Parent $probe
    }
    if (Test-Path -LiteralPath $Root) {
        Assert-NoLinks $Root
        $ownerPath = Join-Path $Root 'source-owner.json'
        if (!(Test-Path -LiteralPath $ownerPath -PathType Leaf)) {
            throw 'Unowned existing root refused. Choose a new dedicated location; nothing was adopted.'
        }
    }
    return $Root
}

function Get-SourcePrerequisites {
    $results = @(
        foreach ($name in 'git', 'dotnet', 'pwsh') {
            $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            [ordered]@{
                name = $name
                available = ($null -ne $command)
                path = if ($command) { $command.Source } else { $null }
                remediation = switch ($name) {
                    'git' { 'Review/install Git manually from https://git-scm.com/downloads; no automatic install.' }
                    'dotnet' { 'Review/install the exact SDK pinned by the selected global.json from https://dotnet.microsoft.com/download; binary users do not need it.' }
                    'pwsh' { 'Review/install PowerShell 7 from https://learn.microsoft.com/powershell/scripting/install/installing-powershell; no elevation requested.' }
                }
            }
        }
    )
    return $results
}

function Invoke-SourceCommand {
    param([string] $Command, [string[]] $Arguments, [string] $Log)
    & $Command @Arguments 2>&1 | Tee-Object -FilePath $Log -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE. See $Log; partial output retained." }
}

function Invoke-SourceCompile {
    param([string] $Checkout, [string] $Payload, [string] $Stage, [string] $Revision)
    Push-Location $Checkout
    try {
        $configuration = Read-SourceJson (Join-Path $Checkout 'global.json')
        $sdk = (& dotnet --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $sdk -cne $configuration.sdk.version) {
            throw "Exact pinned SDK $($configuration.sdk.version) required; observed '$sdk'. Install manually; no roll-forward build admitted."
        }
        $version = & (Join-Path $PSScriptRoot 'Get-BuildVersion.ps1') -RepositoryPath $Checkout
        if ($version.SourceRevision -cne $Revision -or $version.Publish) { throw 'Local source version acquired invalid identity/publication authority.' }
        $project = Join-Path $Checkout 'src\Kora\Kora.csproj'
        $artifacts = Join-Path $Stage 'artifacts'
        $common = @($project, '--configuration', 'Release', '--runtime', 'win-x64',
            '--artifacts-path', $artifacts, "--property:Version=$($version.Version)",
            '--property:ContinuousIntegrationBuild=true')
        Invoke-SourceCommand 'dotnet' @('restore', $project, '--locked-mode',
            "--property:ArtifactsPath=$artifacts", "--property:Version=$($version.Version)") (Join-Path $Stage 'restore.log')
        Invoke-SourceCommand 'dotnet' (@('build') + $common + @('--no-restore', '--self-contained', 'false')) (Join-Path $Stage 'build.log')
        Invoke-SourceCommand 'dotnet' (@('publish') + $common + @('--no-build', '--no-restore',
            '--self-contained', 'false', '--output', $Payload)) (Join-Path $Stage 'publish.log')
        return [ordered]@{
            sdk = $sdk; version = $version.Version; rid = 'win-x64'; configuration = 'Release'
            selfContained = $false; dependencyRestore = 'locked'; channel = 'local-source'
            origin = 'Local operator build of canonical source; not an official release binary.'
            os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        }
    }
    finally { Pop-Location }
}

function Assert-SourceInspection {
    param([string] $Payload, [string] $InspectionPath, [string] $Revision, [string] $Version)
    $inspection = Read-SourceJson $InspectionPath
    Assert-SourceFields $inspection ([ordered]@{
        schema = 2; inspectorVersion = $script:PublishInspectionVersion; inspectionProfile = $script:PublishInspectionProfile
        repository = 'https://github.com/roryprimrose/Kora'; revision = $Revision; rid = 'win-x64'
    })
    Assert-Payload $Payload $inspection
    foreach ($file in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Microsoft.Data.Sqlite.dll',
        'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'e_sqlite3.dll') {
        if (!(Test-Path -LiteralPath (Join-Path $Payload $file) -PathType Leaf)) { throw "Missing launch-critical payload: $file" }
    }
    $sqlite = @($inspection.nativeAssets | Where-Object { $_.published -ceq 'e_sqlite3.dll' })
    if ($sqlite.Count -ne 1 -or $sqlite[0].package -cnotmatch '^SQLitePCLRaw\.lib\.e_sqlite3/\d+\.\d+\.\d+$') {
        throw 'Standard SQLite must be a pinned declared native payload, not an ambient setup dependency.'
    }
    $resources = @($inspection.peFiles | Where-Object { $_.path -ceq 'Kora.dll' } | ForEach-Object { $_.embeddedResources })
    if ($resources -notcontains '!AvaloniaResources') { throw 'Missing embedded Avalonia resources.' }
    foreach ($name in 'Kora.exe', 'Kora.dll', 'Kora.Core.dll', 'Kora.Application.dll', 'Kora.Tools.dll', 'Kora.Definitions.dll', 'Kora.Windows.dll') {
        $observed = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Payload $name)).ProductVersion
        if ($observed -cne $Version) { throw "Published version mismatch: $name expected $Version, observed $observed." }
    }
    if ($inspection.releaseAcceptance -notlike 'BLOCKED:*') { throw 'Static inspection must not claim release acceptance.' }
}

function Invoke-SourceInspect {
    param([string] $Checkout, [string] $Payload, [string] $Stage, [string] $Revision, $Build)
    & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') `
        -Payload $Payload -Revision $Revision -EvidenceDirectory (Join-Path $Stage 'inspection') `
        -BuildOrigin "$($Build.origin) SDK $($Build.sdk); $($Build.os)"
    Assert-SourceInspection $Payload (Join-Path $Stage 'inspection\payload.json') $Revision $Build.version
    Assert-SourceSqliteLock $Checkout (Read-SourceJson (Join-Path $Stage 'inspection\payload.json'))
}

function Assert-SourceSqliteLock {
    param([string] $Checkout, $Inspection)
    $lock = Read-SourceJson (Join-Path $Checkout 'src\Kora.Windows\packages.lock.json')
    $sqlite = @($Inspection.nativeAssets | Where-Object { $_.published -ceq 'e_sqlite3.dll' })[0]
    $versions = @($lock.dependencies.Values | ForEach-Object {
        if ($_.ContainsKey('SQLitePCLRaw.lib.e_sqlite3')) { $_['SQLitePCLRaw.lib.e_sqlite3'].resolved }
    } | Sort-Object -Unique)
    if ($versions.Count -ne 1 -or $sqlite.package -cne "SQLitePCLRaw.lib.e_sqlite3/$($versions[0])") {
        throw 'SQLite payload does not match the selected revision dependency lock.'
    }
}

function Assert-SourceStage {
    param([string] $Stage, [string] $Root, [string] $Checkout, [string] $Repository,
        [string] $Revision, [string] $Deployment, [switch] $RequireSmoke)
    Assert-NoLinks $Stage
    $receipt = Read-SourceJson (Join-Path $Stage 'source-build.json')
    Assert-SourceFields $receipt ([ordered]@{
        schema = 1; bootstrapVersion = $script:SourceBootstrapVersion; mode = 'managed-source-build-only'
        repository = $Repository; revision = $Revision; root = $Root; checkout = $Checkout
        deployment = $Deployment; channel = 'local-source'; maintenance = $script:SourceMaintenance
        activation = $script:SourceActivation; protected = $false; trustAcknowledged = $true
    })
    Assert-SourceFields $receipt.build ([ordered]@{
        rid = 'win-x64'; configuration = 'Release'; selfContained = $false
        dependencyRestore = 'locked'; channel = 'local-source'
        origin = 'Local operator build of canonical source; not an official release binary.'
    })
    $config = Read-SourceJson (Join-Path $Checkout 'global.json')
    $version = & (Join-Path $PSScriptRoot 'Get-BuildVersion.ps1') -RepositoryPath $Checkout
    if ($receipt.build.sdk -cne $config.sdk.version -or $receipt.build.version -cne $version.Version) {
        throw 'Source build SDK/version identity mismatch.'
    }
    $inputs = @(Get-SourceInputs $Checkout)
    if (($inputs | ConvertTo-Json -Depth 5 -Compress) -cne ($receipt.inputs | ConvertTo-Json -Depth 5 -Compress)) {
        throw 'Pinned source inputs changed.'
    }
    if ((@(Get-SourceToolFiles) | ConvertTo-Json -Depth 5 -Compress) -cne ($receipt.bootstrapFiles | ConvertTo-Json -Depth 5 -Compress)) {
        throw 'Bootstrap tooling hashes changed; old output retained for manual review, not relabelled.'
    }
    Assert-Payload (Join-Path $Stage 'payload') $receipt
    Assert-Payload (Join-Path $Stage 'inspection') $receipt.evidence
    Assert-SourceInspection (Join-Path $Stage 'payload') (Join-Path $Stage 'inspection\payload.json') $Revision $receipt.build.version
    Assert-SourceSqliteLock $Checkout (Read-SourceJson (Join-Path $Stage 'inspection\payload.json'))
    if ($RequireSmoke) {
        Assert-SourceFields $receipt.smoke ([ordered]@{
            status = 'passed'; kind = 'bounded-static-child'; applicationLaunched = $false; timeoutSeconds = 60
        })
    }
    return $receipt
}

function Invoke-SourceSmoke {
    param([string] $Stage, [string] $Root, [string] $Checkout, [string] $Repository,
        [string] $Revision, [string] $Deployment,
        [ValidateRange(1, 60)][int] $TimeoutSeconds = 60,
        [string] $VerifierPath = (Join-Path $PSScriptRoot 'Test-SourceStage.ps1'))
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.WorkingDirectory = $Stage
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File',
        $VerifierPath, '-Stage', $Stage, '-Root', $Root,
        '-Checkout', $Checkout, '-Repository', $Repository, '-Revision', $Revision, '-Deployment', $Deployment)) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        if (!$process.Start()) { throw 'Static verification child did not start.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Static verification child timed out ($TimeoutSeconds seconds); no application launched."
        }
        $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $Stage 'smoke.stdout.log')
        $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $Stage 'smoke.stderr.log')
        if ($process.ExitCode -ne 0) { throw "Static verification child failed: see $Stage\smoke.stderr.log." }
    }
    finally { $process.Dispose() }
    return [ordered]@{ status = 'passed'; kind = 'bounded-static-child'; applicationLaunched = $false; timeoutSeconds = 60 }
}

function Invoke-SourceBootstrap {
    [CmdletBinding()]
    param([string] $Root, [ValidatePattern('^[0-9a-f]{40}$')][string] $Revision,
        [ValidateSet('Preview', 'Build')][string] $Action = 'Preview', [switch] $TrustBuildCode,
        [string] $Repository = $script:SourceRepository)
    # Repository injection is an internal fixture seam; the public entry point fixes canonical origin.
    Assert-DistributedSourceTools $Revision
    $Root = Assert-SourceRoot $Root
    $checkout = Join-Path $Root "checkouts\$Revision"
    $deployment = Join-Path $Root "outputs\$Revision"
    $identity = [ordered]@{
        schema = 1; bootstrapVersion = $script:SourceBootstrapVersion; root = $Root
        repository = $Repository; mode = 'managed-source-build-only'; channel = 'local-source'
        maintenance = $script:SourceMaintenance; activation = $script:SourceActivation; protected = $false
    }
    if (Test-Path -LiteralPath $Root) {
        Assert-SourceFields (Read-SourceJson (Join-Path $Root 'source-owner.json')) $identity
    }
    $prerequisites = @(Get-SourcePrerequisites)
    $plan = [ordered]@{
        bootstrapVersion = $script:SourceBootstrapVersion; action = $Action
        repository = $Repository; revision = $Revision; channel = 'local-source'; root = $Root
        checkout = $checkout; output = $deployment; prerequisites = $prerequisites
        sdk = 'Exact selected-revision global.json version; checked before restore.'
        network = 'Git canonical clone/history; locked NuGet package acquisition and normal configured per-user package cache writes. No mutable script download/execution.'
        effects = 'Dedicated root/checkout, unique staging, logs, locked restore/build/publish, hash/native/resource checks and one bounded static PowerShell child.'
        activation = $script:SourceActivation
        excluded = 'No installer, elevation, live deployment, startup registration, app/audio launch, model acquisition, user-data or security changes.'
    }
    if ($Action -eq 'Preview') { return [pscustomobject]$plan }
    if (!$TrustBuildCode) { throw 'Build requires -TrustBuildCode after reviewing this versioned script, exact source and dependency/build code. Preview made no changes.' }
    foreach ($prerequisite in $prerequisites) {
        if (!$prerequisite.available) { throw "Missing prerequisite $($prerequisite.name). $($prerequisite.remediation)" }
    }
    if (!(Test-Path -LiteralPath $Root)) {
        New-ProofDirectory $Root
        Write-ProofJson $identity (Join-Path $Root 'source-owner.json')
    }
    $lock = [IO.File]::Open((Join-Path $Root 'operator.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        Assert-SourceFields (Read-SourceJson (Join-Path $Root 'source-owner.json')) $identity
        if (Test-Path -LiteralPath $checkout) {
            Assert-ManagedCheckoutIdentity $checkout $Repository $Revision
        }
        else {
            $checkoutParent = Split-Path -Parent $checkout
            [IO.Directory]::CreateDirectory($checkoutParent) | Out-Null
            Assert-NoLinks $checkoutParent
            Invoke-Checked 'git' @('-c', 'core.hooksPath=', '-c', 'core.longpaths=true', 'clone', '--no-checkout', '--', $Repository, $checkout)
            Invoke-Checked 'git' @('-C', $checkout, 'config', 'core.longpaths', 'true')
            Invoke-Checked 'git' @('-C', $checkout, '-c', 'core.hooksPath=', 'checkout', '--detach', $Revision)
            Assert-ManagedCheckoutIdentity $checkout $Repository $Revision
        }
        if (Test-Path -LiteralPath $deployment) {
            Assert-SourceStage $deployment $Root $checkout $Repository $Revision $deployment -RequireSmoke | Out-Null
            return [pscustomobject]@{ status = 'reused'; output = $deployment; revision = $Revision; activation = $script:SourceActivation }
        }
        $stage = Join-Path $Root ('staging\' + $Revision + '-' + [guid]::NewGuid().ToString('N'))
        New-ProofDirectory $stage
        $payload = Join-Path $stage 'payload'
        New-ProofDirectory $payload
        $inputs = @(Get-SourceInputs $checkout)
        try {
            $build = Invoke-SourceCompile $checkout $payload $stage $Revision
            if (@(Get-PayloadFiles $payload).Count -eq 0) { throw 'Empty publish output; previous output preserved.' }
            Invoke-SourceInspect $checkout $payload $stage $Revision $build
            $receipt = [ordered]@{
                schema = 1; bootstrapVersion = $script:SourceBootstrapVersion; mode = 'managed-source-build-only'
                repository = $Repository; revision = $Revision; channel = 'local-source'; root = $Root
                checkout = $checkout; deployment = $deployment; maintenance = $script:SourceMaintenance
                activation = $script:SourceActivation; protected = $false; trustAcknowledged = $true
                build = $build; inputs = $inputs; bootstrapFiles = @(Get-SourceToolFiles); files = @(Get-PayloadFiles $payload)
                evidence = [ordered]@{ files = @(Get-PayloadFiles (Join-Path $stage 'inspection')) }
                smoke = $null
            }
            Write-ProofJson $receipt (Join-Path $stage 'source-build.json')
            $receipt.smoke = Invoke-SourceSmoke $stage $Root $checkout $Repository $Revision $deployment
            Write-ProofJson $receipt (Join-Path $stage 'source-build.json')
            Assert-ManagedCheckoutIdentity $checkout $Repository $Revision
            Assert-SourceStage $stage $Root $checkout $Repository $Revision $deployment -RequireSmoke | Out-Null
            $parent = Split-Path -Parent $deployment
            [IO.Directory]::CreateDirectory($parent) | Out-Null
            Assert-NoLinks $parent
            [IO.Directory]::Move($stage, $deployment)
        }
        catch {
            Write-ProofJson ([ordered]@{ schema = 1; revision = $Revision; status = 'failed'; stage = $stage
                error = $_.Exception.Message; recovery = 'Partial files retained. Review manually; retry uses a new stage, never resets/cleans a checkout or replaces an output.' }) `
                (Join-Path $stage 'failure.json')
            throw
        }
        return [pscustomobject]@{ status = 'verified-build-only'; output = $deployment; revision = $Revision; activation = $script:SourceActivation }
    }
    finally { $lock.Dispose() }
}
