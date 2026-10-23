#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$targets = Join-Path $PSScriptRoot 'architecture\Architecture.targets'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "kora-architecture-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
    # MSBuild evaluates imports, item updates/removals and conditions; project XML is not the graph.
    $document = [xml] '<Project><ItemGroup /><Target Name="Check"><MSBuild Projects="@(GraphProject)" Targets="KoraCheckArchitecture" BuildInParallel="true" /></Target></Project>'
    $profiles = $document.SelectSingleNode('/Project/ItemGroup')
    foreach ($project in Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.csproj' -Recurse) {
        $evaluation = & dotnet msbuild $project.FullName -nologo '-getProperty:Configurations,Platforms,RuntimeIdentifiers'
        if ($LASTEXITCODE -ne 0) { throw "Cannot evaluate architecture profiles for $($project.FullName)." }
        $properties = ($evaluation | Out-String | ConvertFrom-Json).Properties
        foreach ($configuration in ($properties.Configurations -split ';')) {
            foreach ($platform in ($properties.Platforms -split ';')) {
                    foreach ($rid in (@('') + @($properties.RuntimeIdentifiers -split ';' | Where-Object { $_ } ) | Select-Object -Unique)) {
                        foreach ($os in @('Windows_NT', 'Unix')) {
                            $item = $document.CreateElement('GraphProject')
                            $item.SetAttribute('Include', $project.FullName)
                            $metadata = $document.CreateElement('AdditionalProperties')
                            $metadata.InnerText = "Configuration=$configuration;Platform=$platform;RuntimeIdentifier=$rid;OS=$os;CustomAfterMicrosoftCommonTargets=$targets;CustomAfterMicrosoftCommonCrossTargetingTargets=$targets"
                            $item.AppendChild($metadata) | Out-Null
                            $profiles.AppendChild($item) | Out-Null
                    }
                }
            }
        }
    }
    if ($profiles.ChildNodes.Count -eq 0) { throw 'No production project profiles were evaluated.' }
    $traversal = Join-Path $scratch 'Architecture.proj'
    $document.Save($traversal)
    & dotnet msbuild $traversal -nologo -target:Check
    if ($LASTEXITCODE -ne 0) { throw 'The evaluated production dependency graph violates the architecture contract.' }
    Write-Host "Architecture graph: $($profiles.ChildNodes.Count) evaluated profiles passed."

    foreach ($configuration in @('Debug', 'Release')) {
        & dotnet build (Join-Path $PSScriptRoot 'architecture\Kora.Architecture.csproj') --configuration $configuration --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Architecture analyzer build failed.' }
        foreach ($layer in @('Kora.Application', 'Kora.Definitions')) {
            & dotnet build (Join-Path $root "src\$layer\$layer.csproj") --configuration $configuration --no-restore --no-incremental --nologo "-p:CustomAfterMicrosoftCommonTargets=$targets" '-p:KoraArchitectureAnalyze=true' '-p:RunAnalyzersDuringBuild=true' '-p:RunAnalyzers=true'
            if ($LASTEXITCODE -ne 0) { throw "Portable API boundary check failed for $layer ($configuration)." }
        }
    }
    & dotnet test --project (Join-Path $root 'tests\Kora.Architecture.Tests\Kora.Architecture.Tests.csproj') --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Architecture guard regression fixtures failed.' }
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
