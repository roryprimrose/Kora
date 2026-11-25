using AwesomeAssertions;
using Kora.Core.Mcp;
using Kora.Windows.Mcp;
using Kora.Core.Dependencies;
using McpProcessAuthorization = Kora.Core.Mcp.McpConnectionIdentity.McpProcessAuthorization;

namespace Kora.Windows.IntegrationTests;

public sealed class McpOwnedProcessLeaseTests
{
    [Fact]
    public async Task Preparation_holds_binary_and_cwd_against_mutation_and_does_not_start_without_launch_authority()
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "mcp-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "server.exe");
        var cwd = Path.Combine(root, "workspace");
        File.WriteAllBytes(executable, [1, 2, 3, 4]);
        Directory.CreateDirectory(cwd);
        try
        {
            var identity = new McpConnectionIdentity(new McpStdioEndpoint(executable, ["--stdio"], cwd,
                new Dictionary<string, string>(StringComparer.Ordinal)));
            await using var preparation = await WindowsMcpProcessLease.PrepareAsync(identity, new Paths(root),
                TimeProvider.System, TestContext.Current.CancellationToken);
            FluentActions.Invoking(() => File.WriteAllBytes(executable, [5, 6])).Should().Throw<IOException>();
            FluentActions.Invoking(() => Directory.Move(cwd, cwd + "-moved")).Should().Throw<IOException>();
            var launches = 0;
            var cleanups = 0;
            var authority = new McpProcessAuthorization(identity, preparation.NativeIdentity,
                _ => { launches++; throw new InvalidOperationException("Native launch consent withdrawn."); },
                _ => { cleanups++; return Task.CompletedTask; });
            await FluentActions.Awaiting(() => preparation.ConnectAsync(authority, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            launches.Should().Be(1);
            cleanups.Should().Be(0);
            Directory.GetDirectories(root).Should().Equal(cwd);
            File.ReadAllBytes(executable).Should().Equal(1, 2, 3, 4);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Start_profile_preserves_literal_arguments_and_only_explicit_environment_plus_private_temp()
    {
        var profile = new McpStdioEndpoint(@"C:\Installed\server.exe", ["--stdio", "two words", "& not a shell"],
            @"C:\Workspace", new Dictionary<string, string>(StringComparer.Ordinal) { ["LANG"] = "en-US" });
        var start = WindowsMcpProcessLease.CreateStartInfo(profile, @"C:\KoraPrivate\ProcessTemp");
        start.FileName.Should().Be(profile.Executable);
        start.WorkingDirectory.Should().Be(profile.WorkingDirectory);
        start.ArgumentList.Should().Equal(profile.Arguments);
        start.Arguments.Should().BeEmpty();
        start.UseShellExecute.Should().BeFalse();
        start.RedirectStandardInput.Should().BeTrue();
        start.RedirectStandardOutput.Should().BeTrue();
        start.RedirectStandardError.Should().BeTrue();
        start.Environment.Should().HaveCount(3);
        start.Environment["LANG"].Should().Be("en-US");
        start.Environment["TEMP"].Should().Be(@"C:\KoraPrivate\ProcessTemp");
        start.Environment["TMP"].Should().Be(@"C:\KoraPrivate\ProcessTemp");
        start.Environment.Should().NotContainKey("PATH").And.NotContainKey("USERPROFILE")
            .And.NotContainKey("HTTP_PROXY").And.NotContainKey("HTTPS_PROXY");
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
