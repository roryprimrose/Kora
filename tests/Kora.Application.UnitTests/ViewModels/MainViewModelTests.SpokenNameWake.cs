using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(", ")]
    [InlineData(" ")]
    public async Task TrustedWakeNameRouteReviewsLongestActiveNameAfterJoinAndConfirmsOriginalVoiceBody(string separator)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = "Nova";
        var admission = new RequestAdmission();
        var store = BindRequestCommands(f, admission);
        await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            store.Session.Authority.Generation, 0, new("Build plan"), () => true, TestContext.Current.CancellationToken);
        store.ClaudePageRead = (_, _) => new([
            store.Session with { Authority = store.Session.Authority with { SessionId = new(Guid.NewGuid()) },
                Metadata = store.Session.Metadata! with { Name = new("Build") } }, store.Session], null);
        var fallback = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            fallback++;
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.HoldClose = true;
        const string body = "  Original e\u0301\r\nbody -- data  ";
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("in Build plan" + separator + body, 1, first.Binding.Generation));
        await first.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            f.ViewModel.ResponseTitle.Should().NotBe("Review conversation request.");
            admission.DefaultCalls.Should().Be(0);
        }
        finally { first.ReleaseClose.TrySetResult(); }
        await stage.NextAsync();
        f.ViewModel.ResponseTitle.Should().Be("Review conversation request.");
        f.ViewModel.ResponseBody.Should().Contain("Conversation: Build plan").And.Contain(body.Trim())
            .And.Contain("ActivatedVoice").And.Contain("60 seconds");
        first.IsQuiescent.Should().BeTrue();
        store.RequestRetentions.Should().Be(0);
        admission.DefaultCalls.Should().Be(0);
        fallback.Should().Be(0);
        await f.RunAsync("session request confirm");
        store.RequestMessage!.Body.Should().Be(body);
        store.RequestMessage.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        store.RequestMessage.Request.SessionId.Should().Be(store.Session.Authority.SessionId);
        store.RequestRetentions.Should().Be(1);
        admission.DefaultCalls.Should().Be(1);
        await f.RunAsync("session request confirm");
        store.RequestRetentions.Should().Be(1);
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("invalid")]
    public async Task TrustedWakeNameRefusalNeverFallsBackOrRetainsARequest(string fault)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        var admission = new RequestAdmission();
        var store = BindRequestCommands(f, admission);
        await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            store.Session.Authority.Generation, 0, new("Build"), () => true, TestContext.Current.CancellationToken);
        store.ClaudePageRead = (_, _) => fault switch
        {
            "ambiguous" => new([store.Session, store.Session with { Authority = store.Session.Authority with { SessionId = new(Guid.NewGuid()) } }], null),
            "missing" => new([], null),
            "inactive" => new([store.Session with { Authority = store.Session.Authority with { IsActive = false } }], null),
            _ => new([store.Session], null)
        };
        var fallback = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            fallback++;
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new(fault is "invalid" ? "in " : "in Build, original", 1, first.Binding.Generation));
        await stage.NextAsync();
        f.ViewModel.ResponseTitle.Should().NotBe("Review conversation request.");
        await f.RunAsync("session request confirm");
        fallback.Should().Be(0);
        store.RequestRetentions.Should().Be(0);
        admission.DefaultCalls.Should().Be(0);
        admission.CopilotCalls.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("expiry")]
    [InlineData("consent")]
    public async Task TrustedWakeNameReviewCannotRenewItsOriginalAuthority(string change)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        var admission = new RequestAdmission();
        var store = new SessionCommandStore(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var clock = new SpokenNameWakeClock();
        await using var conversation = new SessionConversationService(store, store, workspace, store, admission,
            clock, NullLogger<SessionConversationService>.Instance);
        f.ViewModel.BindSessionCommands(workspace, new(workspace, conversation, store, clock));
        await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            store.Session.Authority.Generation, 0, new("Build"), () => true, TestContext.Current.CancellationToken);
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("in Build, original", 1, first.Binding.Generation));
        await stage.NextAsync();
        f.ViewModel.ResponseTitle.Should().Be("Review conversation request.");
        if (change is "metadata")
        {
            await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                store.Session.Authority.Generation, 1, new("Changed"), () => true, TestContext.Current.CancellationToken);
        }
        if (change is "expiry") { clock.Now = clock.Now.AddSeconds(60); }
        if (change is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        await f.RunAsync("session request confirm");
        store.RequestRetentions.Should().Be(0);
        admission.DefaultCalls.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        await f.ViewModel.DisposeAsync();
    }

    private sealed class SpokenNameWakeClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
