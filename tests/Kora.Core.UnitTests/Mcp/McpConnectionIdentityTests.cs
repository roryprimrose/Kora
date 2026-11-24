using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Customizations;
using Kora.Core.Mcp;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpConnectionIdentityTests
{
    [Fact]
    public void Literal_profile_round_trip_has_stable_transport_identity_without_an_account()
    {
        const string definition = """{"command":"C:\\Installed\\server.exe","args":["--stdio","literal space"],"cwd":"C:\\Workspace","env":{"LANG":"en-US"}}""";
        var first = new McpConnectionIdentity(McpDefinitionDocument.ValidateStdioServer(Encoding.UTF8.GetBytes(definition)));
        var second = new McpConnectionIdentity(McpDefinitionDocument.ValidateStdioServer(Encoding.UTF8.GetBytes(definition)));
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Account.Should().BeNull();
        first.Stdio!.Arguments.Should().Equal("--stdio", "literal space");
        first.NativeDestination.Should().Contain(@"C:\Installed\server.exe").And.Contain("literal space");
        first.ToString().Should().NotContain("server.exe");
        first.Should().NotBe(new McpConnectionIdentity(new McpStdioEndpoint(@"C:\Installed\server.exe",
            ["--changed"], @"C:\Workspace", new Dictionary<string, string>(StringComparer.Ordinal))));
    }

    [Fact]
    public async Task Legacy_brokers_preserve_HTTPS_delegation_but_cannot_launch_a_local_profile()
    {
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        McpConnectionIdentity https = account;
        var implementation = new HttpsOnlyBroker();
        IMcpConnectionBroker broker = implementation;
        await FluentActions.Awaiting(() => broker.ConnectAsync(https, null, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Delegated route reached.");
        implementation.Selected.Should().BeSameAs(account);
        https.NativeDestination.Should().Be("https://example.com/mcp");
        var profile = new McpStdioEndpoint(@"C:\Installed\server.exe", [], @"C:\Workspace",
            new Dictionary<string, string>(StringComparer.Ordinal));
        var local = new McpConnectionIdentity(profile);
        var authorityUsed = false;
        var authority = new McpConnectionIdentity.McpProcessAuthorization(local, "held-native-identity",
            _ => { authorityUsed = true; return Task.CompletedTask; },
            _ => { authorityUsed = true; return Task.CompletedTask; });
        implementation.Selected = null;
        await FluentActions.Awaiting(() => broker.ConnectAsync(local, authority, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => broker.PrepareStdioAsync(local, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        implementation.Selected.Should().BeNull();
        authorityUsed.Should().BeFalse();
        authority.ToString().Should().NotContain("held-native-identity").And.NotContain("server.exe");
        profile.ToString().Should().NotContain("server.exe");
        https.Equals(local).Should().BeFalse();
        https.Equals((McpConnectionIdentity?)null).Should().BeFalse();
        FluentActions.Invoking(() => new McpConnectionIdentity((McpAccountReference)null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new McpConnectionIdentity((McpStdioEndpoint)null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("""{"command":"server.exe","args":[],"cwd":"C:\\Workspace","env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\run.cmd","args":[],"cwd":"C:\\Workspace","env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":{"PATH":"C:\\Tools"}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":{"PROXY":"https://example.com"}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":{},"headers":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":{},"env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[5],"cwd":"C:\\Workspace","env":{}}""")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"command":1,"args":[],"cwd":"C:\\Workspace","env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":1,"env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","cwd":"C:\\Workspace","env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":{},"cwd":"C:\\Workspace","env":{}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace"}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":[]}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":{"LANG":1}}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":{},"type":1}""")]
    [InlineData("""{"command":"C:\\Installed\\server.exe","args":[],"cwd":"C:\\Workspace","env":{},"type":"http"}""")]
    [InlineData("{")]
    [InlineData("")]
    public void Invalid_or_ambient_profile_is_rejected_before_native_preparation(string definition) =>
        FluentActions.Invoking(() => McpDefinitionDocument.ValidateStdioServer(Encoding.UTF8.GetBytes(definition)))
            .Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("")]
    [InlineData(@"\\server\share\server.exe")]
    [InlineData("C:")]
    [InlineData(@"1:\Installed\server.exe")]
    [InlineData("C:/Installed/server.exe")]
    [InlineData(@"C:\Installed/../server.exe")]
    [InlineData(@"C:\Installed\..\server.exe")]
    [InlineData(@"C:\Installed\.\server.exe")]
    [InlineData(@"C:\Installed\\server.exe")]
    [InlineData(@"C:\Installed\server.exe:stream")]
    [InlineData(@"C:\%TEMP%\server.exe")]
    [InlineData(@"C:\Installed \server.exe")]
    [InlineData(@"C:\Installed.\server.exe")]
    [InlineData("C:\\Installed\n\\server.exe")]
    [InlineData(@"C:\Installed\*.exe")]
    [InlineData(@"C:\Installed\server?.exe")]
    public void Ambiguous_native_paths_are_refused_before_any_launch(string executable) =>
        FluentActions.Invoking(() => new McpStdioEndpoint(executable, [], @"C:\Workspace",
            new Dictionary<string, string>(StringComparer.Ordinal))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("argument-count")]
    [InlineData("argument-length")]
    [InlineData("argument-total")]
    [InlineData("argument-control")]
    [InlineData("environment-count")]
    [InlineData("environment-empty")]
    [InlineData("environment-length")]
    [InlineData("environment-control")]
    [InlineData("path-length")]
    public void Oversized_or_control_bearing_profiles_are_refused_as_a_whole(string fault)
    {
        var executable = fault is "path-length" ? @"C:\" + new string('a', 2048) + ".exe" : @"C:\Installed\server.exe";
        string[] args = fault switch
        {
            "argument-count" => Enumerable.Repeat("x", 65).ToArray(),
            "argument-length" => [new string('x', 2049)],
            "argument-total" => Enumerable.Repeat(new string('x', 2048), 5).ToArray(),
            "argument-control" => ["line\nbreak"],
            _ => [],
        };
        var env = fault is "environment-count"
            ? Enumerable.Range(0, 17).ToDictionary(index => "key" + index, _ => "value", StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        if (fault.StartsWith("environment-", StringComparison.Ordinal) && fault is not "environment-count")
        {
            env["LANG"] = fault switch
            {
                "environment-empty" => "",
                "environment-length" => new string('x', 2049),
                _ => "en\nUS",
            };
        }
        FluentActions.Invoking(() => new McpStdioEndpoint(executable, args, @"C:\Workspace", env))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Explicit_environment_and_type_are_preserved_but_non_utf8_and_oversized_entries_are_refused()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SystemRoot"] = @"C:\Windows", ["WINDIR"] = @"C:\Windows",
            ["DOTNET_ROOT"] = @"C:\Dotnet", ["DOTNET_ROOT_X64"] = @"C:\Dotnet",
            ["LANG"] = "en-US", ["LC_ALL"] = "en-US",
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "stdio", command = @"C:\Installed\server.exe", args = Array.Empty<string>(),
            cwd = @"C:\Workspace", env = environment,
        });
        var profile = McpDefinitionDocument.ValidateStdioServer(bytes);
        profile.Environment.Should().BeEquivalentTo(environment);
        profile.NativeText.Should().Contain("NOT sandboxed").And.Contain("no inheritance");
        FluentActions.Invoking(() => McpDefinitionDocument.ValidateStdioServer(new byte[65537]))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => McpDefinitionDocument.ValidateStdioServer([0xff]))
            .Should().Throw<InvalidDataException>();
    }

    private sealed class HttpsOnlyBroker : IMcpConnectionBroker
    {
        internal McpAccountReference? Selected { get; set; }
        public Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token) =>
            throw new InvalidOperationException("No account discovery was authorized.");
        public Task<IMcpConnection> ConnectAsync(McpAccountReference account, CancellationToken token)
        {
            Selected = account;
            throw new InvalidOperationException("Delegated route reached.");
        }
    }
}
