using AwesomeAssertions;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

#pragma warning disable GHCP001 // Tests the pinned SDK's actual session-filesystem protocol, with no native filesystem access.
public sealed class CopilotVolatileSessionFilesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exact_byte_budget_failure_is_atomic_and_disposal_prevents_late_storage()
    {
        using var files = new CopilotVolatileSessionFiles(@"Q:\");
        ISessionFsHandler api = files;
        (await api.WriteFileAsync(new() { Path = "events.jsonl", Content = new('x', CopilotVolatileSessionFiles.MaximumBytes) }, Token))
            .Should().BeNull();
        (await api.AppendFileAsync(new() { Path = "events.jsonl", Content = "x" }, Token)).Should().NotBeNull();
        (await api.ReadFileAsync(new() { Path = "events.jsonl" }, Token)).Content.Length.Should().Be(CopilotVolatileSessionFiles.MaximumBytes);
        files.Dispose();
        (await api.ReadFileAsync(new() { Path = "events.jsonl" }, Token)).Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Directory_entry_budget_includes_the_virtual_root()
    {
        using var files = new CopilotVolatileSessionFiles(@"Q:\");
        ISessionFsHandler api = files;
        for (var index = 0; index < CopilotVolatileSessionFiles.MaximumEntries - 1; index++)
        {
            (await api.WriteFileAsync(new() { Path = index.ToString(System.Globalization.CultureInfo.InvariantCulture), Content = "" }, Token))
                .Should().BeNull();
        }
        (await api.WriteFileAsync(new() { Path = "one-too-many", Content = "" }, Token)).Should().NotBeNull();
    }

    [Fact]
    public async Task Windows_case_relative_paths_directories_and_rename_have_real_virtual_semantics()
    {
        using var files = new CopilotVolatileSessionFiles(@"Q:\virtual");
        ISessionFsHandler api = files;
        (await api.WriteFileAsync(new() { Path = @"sub\events.jsonl", Content = "exact-bytes" }, Token)).Should().BeNull();
        (await api.ReadFileAsync(new() { Path = @"Q:\VIRTUAL\SUB\EVENTS.JSONL" }, Token)).Content.Should().Be("exact-bytes");
        (await api.ReaddirAsync(new() { Path = "sub" }, Token)).Entries.Should().Equal("events.jsonl");
        (await api.RenameAsync(new() { Src = @"sub\events.jsonl", Dest = @"sub\retained.jsonl" }, Token)).Should().BeNull();
        (await api.ReadFileAsync(new() { Path = @"sub\retained.jsonl" }, Token)).Content.Should().Be("exact-bytes");
    }

    [Fact]
    public async Task Two_sdk_sessions_never_share_even_the_same_virtual_path()
    {
        using var first = new CopilotVolatileSessionFiles(@"Q:\virtual");
        using var second = new CopilotVolatileSessionFiles(@"Q:\virtual");
        ISessionFsHandler firstApi = first;
        ISessionFsHandler secondApi = second;
        (await firstApi.WriteFileAsync(new() { Path = "events.jsonl", Content = "session-one-private" }, Token)).Should().BeNull();
        (await secondApi.ReadFileAsync(new() { Path = "events.jsonl" }, Token)).Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Sqlite_and_binary_routes_are_explicitly_denied_not_native_fallbacks()
    {
        using var files = new CopilotVolatileSessionFiles(@"Q:\virtual");
        ISessionFsSqliteProvider sql = files;
        ISessionFsBinaryProvider binary = files;
        await Assert.ThrowsAsync<NotSupportedException>(() => sql.QueryAsync(SessionFsSqliteQueryType.Query, "select 1", null, Token));
        await Assert.ThrowsAsync<NotSupportedException>(() => binary.ReadFileBytesAsync("arbitrary", Token));
        await Assert.ThrowsAsync<NotSupportedException>(() => binary.WriteFileBytesAsync("arbitrary", new byte[] { 1 }, null, Token));
    }
}
#pragma warning restore GHCP001
