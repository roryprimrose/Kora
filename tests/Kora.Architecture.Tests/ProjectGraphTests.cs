using System.Diagnostics;
using System.Security;
using AwesomeAssertions;

namespace Kora.Architecture.Tests;

public sealed class ProjectGraphTests
{
    [Theory]
    [InlineData("public interface IWindowsSession { bool IsLocked { get; } }", true)]
    [InlineData("public class C { public void Run() { if (System.OperatingSystem.IsWindows()) System.Console.Beep(800, 100); } }", false)]
    [InlineData("using N = System.Runtime.InteropServices.NativeLibrary; public class C { public nint Load() => N.Load(\"fixture-not-executed\"); }", false)]
    public async Task RealCompilerLoadsTheGuardAndFailsForbiddenPortableCode(string code, bool allowed)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "kora-architecture-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var project = Path.Combine(scratch, "Kora.Core.csproj");
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <KoraArchitectureAnalyze>true</KoraArchitectureAnalyze>
                    <RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>
                  </PropertyGroup>
                </Project>
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(scratch, "Fixture.cs"), code, TestContext.Current.CancellationToken);
            var result = await RunMsBuildAsync(project, "Release", "", "Unix", compile: true);
            if (allowed)
            {
                result.ExitCode.Should().Be(0, result.Output);
            }
            else
            {
                result.ExitCode.Should().NotBe(0, result.Output);
                result.Output.Should().Contain("KORAARCH002");
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Theory]
    [InlineData("Debug", "", "Unix", true)]
    [InlineData("Release", "", "Unix", false)]
    [InlineData("Debug", "win-x64", "Unix", false)]
    [InlineData("Debug", "", "Windows_NT", false)]
    public async Task EvaluatedImportsAndConditionalReferencesAreEnforced(
        string configuration, string runtimeIdentifier, string os, bool allowed)
    {
        var root = FindRepositoryRoot();
        var scratch = Path.Combine(Path.GetTempPath(), "kora-architecture-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var project = Path.Combine(scratch, "Kora.Application.csproj");
            var source = SecurityElement.Escape(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(Path.Combine(scratch, "references.props"), $$"""
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="{{source}}/Kora.Core/./Kora.Core.csproj" />
                    <ProjectReference Include="{{source}}/Kora.Tools/Kora.Tools.csproj" />
                    <ProjectReference Include="{{source}}/Kora.Windows/Kora.Windows.csproj"
                                      Condition="'$(Configuration)' == 'Release' or '$(RuntimeIdentifier)' == 'win-x64' or '$(OS)' == 'Windows_NT'" />
                  </ItemGroup>
                </Project>
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <Import Project="references.props" />
                </Project>
                """, TestContext.Current.CancellationToken);
            var result = await RunMsBuildAsync(project, configuration, runtimeIdentifier, os);
            if (allowed)
            {
                result.ExitCode.Should().Be(0, result.Output);
            }
            else
            {
                result.ExitCode.Should().NotBe(0, result.Output);
                result.Output.Should().Contain("KORAARCH001").And.Contain("Kora.Windows");
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Theory]
    [InlineData("Kora.Core", "Kora.Tools")]
    [InlineData("Kora.Tools", "Kora.Application")]
    [InlineData("Kora.Definitions", "Kora.Windows")]
    [InlineData("Kora.Windows", "Kora.Application")]
    [InlineData("Kora", "Kora.Tools")]
    [InlineData("Kora.NewLayer", "Kora.Core")]
    [InlineData("Kora.Core", "")]
    public async Task ForbiddenLayersAndPlatformTargetsFail(string layer, string dependency)
    {
        var root = FindRepositoryRoot();
        var scratch = Path.Combine(Path.GetTempPath(), "kora-architecture-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var reference = SecurityElement.Escape(Path.Combine(root, "src", dependency, dependency + ".csproj"));
            var project = Path.Combine(scratch, layer + ".csproj");
            var framework = dependency.Length == 0 ? "net10.0-windows" : "net10.0";
            var item = dependency.Length == 0 ? "" : $"""<ProjectReference Include="{reference}" />""";
            await File.WriteAllTextAsync(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>{{framework}}</TargetFramework><EnableWindowsTargeting>true</EnableWindowsTargeting></PropertyGroup>
                  <ItemGroup>{{item}}</ItemGroup>
                </Project>
                """, TestContext.Current.CancellationToken);
            var result = await RunMsBuildAsync(project, "Debug", "", "Unix");
            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain("KORAARCH001");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalAndMultipleFrameworksCannotHidePlatformTargets(bool multipleFrameworks)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "kora-architecture-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var project = Path.Combine(scratch, "Kora.Core.csproj");
            var frameworks = multipleFrameworks
                ? "<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>"
                : "<TargetFramework>net10.0</TargetFramework><TargetFramework Condition=\"'$(Configuration)' == 'Release'\">net10.0-windows</TargetFramework>";
            await File.WriteAllTextAsync(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>{{frameworks}}<EnableWindowsTargeting>true</EnableWindowsTargeting></PropertyGroup>
                </Project>
                """, TestContext.Current.CancellationToken);
            var result = await RunMsBuildAsync(project, "Release", "", "Unix");
            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain("KORAARCH001").And.Contain("platform-neutral");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Architecture fixtures require the source checkout.");
    }

    private static async Task<(int ExitCode, string Output)> RunMsBuildAsync(
        string project, string configuration, string runtimeIdentifier, string os, bool compile = false)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var targets = Path.Combine(FindRepositoryRoot(), "eng", "architecture", "Architecture.targets");
        foreach (var argument in new[]
        {
            "msbuild", project, "-nologo", compile ? "-restore" : "-target:KoraCheckArchitecture",
            "-p:Configuration=" + configuration, "-p:RuntimeIdentifier=" + runtimeIdentifier, "-p:OS=" + os,
            "-p:CustomAfterMicrosoftCommonTargets=" + targets,
            "-p:CustomAfterMicrosoftCommonCrossTargetingTargets=" + targets,
            "-p:RunAnalyzersDuringBuild=true", "-p:RunAnalyzers=true",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start MSBuild.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
