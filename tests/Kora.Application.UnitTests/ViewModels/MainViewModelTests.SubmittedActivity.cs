using AwesomeAssertions;

using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    [InlineData("progress")]
    public async Task SubmittedActivityHasOneBodyFreeStatusAcrossNativeTypedActivatedAndProgressRoutes(string route)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        var body = f.ViewModel.ResponseBody;
        var input = f.ViewModel.CommandText;
        if (route is "native") { f.ViewModel.ShowSubmittedWorkActivity(); }
        else if (route is "voice") { await f.RaiseActivatedTranscriptAsync("Kora, what are you currently working on", 1); }
        else { await f.RunAsync(route is "progress" ? "show task progress" : "what are you currently working on"); }
        f.ViewModel.SubmittedWorkActivityStatus.Should().Contain("busy 0").And.Contain("held conversations 0")
            .And.Contain("not setup, volatile previews or listening");
        if (route is "native")
        {
            f.ViewModel.ResponseBody.Should().Be(body);
            f.ViewModel.CommandText.Should().Be(input);
        }
        else { f.ViewModel.ResponseBody.Should().Contain("Submitted activity observed"); }
        store.RemainingReads.Should().Be(0, "aggregate ownership never reads conversation bodies or retained history");
        store.RequestRetentions.Should().Be(0);
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("private")]
    [InlineData("disposed")]
    [InlineData("rebound")]
    public async Task SubmittedActivityUnavailableNeverClaimsIdleOrReusesAnOldOwner(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        f.ViewModel.SubmittedWorkActivityStatus.Should().Contain("Submitted activity observed");
        if (fault is "unbound") { await conversation.DisposeAsync(); }
        if (fault is "private") { f.Session.IsUnlocked = false; }
        if (fault is "disposed") { f.ViewModel.Dispose(); }
        if (fault is "rebound")
        {
            f.ViewModel.BindSessionCommands(new(store, new(f.HostStore), store,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance));
        }
        f.ViewModel.RefreshSubmittedWorkActivity();
        f.ViewModel.SubmittedWorkActivityStatus.Should().Contain("unavailable").And.NotContain("busy 0");
    }

    [Fact]
    public async Task SubmittedActivityRefreshPreservesTheExactPendingQuestionAndUnsentCommand()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        var (_, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        var body = f.ViewModel.ResponseBody;
        var command = f.ViewModel.CommandText;
        f.ViewModel.ShowSubmittedWorkActivity();
        f.ViewModel.RefreshSubmittedWorkActivity();
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.CommandText.Should().Be(command);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("binding")]
    [InlineData("disposed")]
    [InlineData("topology")]
    public async Task SubmittedActivityRevalidatesPrivateHostAndBindingAfterOwnerObservation(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, original) = BindRemainingWork(f);
        await using var originalOwner = original;
        var clock = new SubmittedActivityClock(() =>
        {
            if (change is "privacy") { f.Session.IsUnlocked = false; }
            if (change is "topology")
            {
                f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 100 };
            }
            if (change is "binding")
            {
                f.ViewModel.BindSessionCommands(new(store, new(f.HostStore), store,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance));
            }
            if (change is "disposed") { f.ViewModel.Dispose(); }
        });
        await using var conversation = new Kora.Application.Hosting.SessionConversationService(store, store, null!, store,
            null!, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionConversationService>.Instance);
        f.ViewModel.BindRemainingWork(new(null!, conversation, store, clock), conversation);
        f.ViewModel.SubmittedWorkActivityStatus.Should().Contain("unavailable").And.NotContain("busy 0");
    }

    private sealed class SubmittedActivityClock(Action observe) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            observe();
            return DateTimeOffset.UnixEpoch;
        }
    }
}