using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Native_Claude_setup_event_and_shutdown_failure_never_grant_exit_authority()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.ConfigureClaudeAccount();
        var requests = 0;
        f.ViewModel.ClaudeAccountSetupRequested += (_, _) => requests++;
        f.ViewModel.ConfigureClaudeAccount();
        requests.Should().Be(1);
        f.ViewModel.BindClaudeShutdown(() => Task.FromResult(false));
        await f.ViewModel.ExitAsync();
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.ViewModel.ResponseTitle.Should().Be("Exit or restart was not confirmed.");
        f.ViewModel.BindClaudeShutdown(() => throw new IOException("Synthetic owned join failure."));
        await f.ViewModel.ExitAsync();
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Theory]
    [InlineData("active", "")]
    [InlineData("active", "copilot")]
    [InlineData("active", "claude")]
    [InlineData("active", "claude-tools=application.get_version")]
    [InlineData("\"Readable topic\"", "claude")]
    public async Task Readable_typed_and_voice_review_keep_identifiers_internal_and_retain_original_source(string selector, string provider)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        if (selector[0] == '"')
        {
            await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                store.Session.Authority.Generation, 0, new("Readable topic"), () => true, TestContext.Current.CancellationToken);
        }
        const string body = "  Exact original\r\nbody  ";
        await f.RaiseActivatedTranscriptAsync("Kora, session request review " + selector
            + (provider.Length == 0 ? "" : " " + provider) + " -- " + body, 1);
        f.ViewModel.ResponseTitle.Should().Be("Review conversation request.");
        f.ViewModel.ResponseBody.Should().Contain(body).And.Contain("session request confirm")
            .And.NotContain(store.Session.Authority.SessionId.Value.ToString("D"));
        if (provider.StartsWith("claude-tools=", StringComparison.Ordinal))
        { f.ViewModel.ResponseBody.Should().Contain("Claude observation scope: application.get_version"); }
        store.RequestRetentions.Should().Be(0);
        await f.RunAsync("session request confirm");
        store.RequestMessage!.Body.Should().Be(body);
        store.RequestMessage.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        store.RequestMessage.Outcome.Should().Be(Kora.Core.Dependencies.ModelTurnOutcome.Unavailable);
        store.RequestRetentions.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("private-before")]
    [InlineData("private-after")]
    [InlineData("both-selectors")]
    [InlineData("duplicate")]
    [InlineData("ambiguous")]
    [InlineData("inactive")]
    [InlineData("null-name")]
    [InlineData("wrong-name")]
    [InlineData("missing")]
    [InlineData("cursor")]
    [InlineData("overflow")]
    [InlineData("default-page")]
    [InlineData("oversize-page")]
    public async Task Readable_resolution_fails_closed_on_incomplete_ambiguous_or_revoked_inventory(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(store, store, workspace, store, new RequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        var commands = new SessionRequestCommandService(workspace, conversation, store, TimeProvider.System);
        var live = fault is not "private-before";
        var cursor = Guid.NewGuid();
        store.ClaudePageRead = (_, limit) =>
        {
            limit.Should().Be(SessionPage<SessionWorkspaceEntry>.MaximumRecords);
            if (fault is "private-after") { live = false; }
            return fault switch
            {
                "duplicate" => new([store.Session, store.Session], null),
                "ambiguous" => new([store.Session, new(store.Session.Authority with { SessionId = new(Guid.NewGuid()) }, null)], null),
                "inactive" => new([store.Session with { Authority = store.Session.Authority with { IsActive = false } }], null),
                "wrong-name" => new([store.Session with { Metadata = new(store.Session.Authority.SessionId, new(1), new("other")) }], null),
                "missing" => new([], null),
                "cursor" => new([], cursor),
                "overflow" => new([], Guid.NewGuid()),
                "default-page" => new(default, null),
                "oversize-page" => new([.. Enumerable.Repeat(store.Session, 51)], null),
                _ => new([store.Session], null),
            };
        };
        var command = SessionCommand.Parse("session request review active claude -- original", "Kora")!;
        if (fault is "both-selectors") { command = command with { RequestTargetName = new("topic") }; }
        if (fault is "null-name" or "wrong-name")
        { command = command with { RequestActiveTarget = false, RequestTargetName = new("topic") }; }
        await Assert.ThrowsAnyAsync<Exception>(() => commands.ExecuteAsync(command, RequestOrigin.LocalUi, () => live,
            TestContext.Current.CancellationToken));
        store.RequestRetentions.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("session")]
    [InlineData("review")]
    [InlineData("operation")]
    public async Task Pending_confirmation_has_no_implicit_target_or_unrelated_operation_authority(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(store, store, workspace, store, new RequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        var commands = new SessionRequestCommandService(workspace, conversation, store, TimeProvider.System);
        if (fault is not "missing")
        {
            await commands.ExecuteAsync(SessionCommand.Parse("session request review active -- original", "Kora")!,
                RequestOrigin.LocalUi, () => true, TestContext.Current.CancellationToken);
        }
        var command = SessionCommand.Parse("session request confirm", "Kora")!;
        command = fault switch
        {
            "session" => command with { SessionId = Guid.NewGuid() },
            "review" => command with { RequestReviewId = Guid.NewGuid() },
            "operation" => command with { Operation = SessionCommandOperation.List },
            _ => command,
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ExecuteAsync(command, RequestOrigin.LocalUi, () => true,
            TestContext.Current.CancellationToken));
        store.RequestRetentions.Should().Be(0);
    }

    private sealed partial class SessionCommandStore
    {
        internal Func<Guid?, int, SessionPage<SessionWorkspaceEntry>>? ClaudePageRead { get; set; }
    }
}
