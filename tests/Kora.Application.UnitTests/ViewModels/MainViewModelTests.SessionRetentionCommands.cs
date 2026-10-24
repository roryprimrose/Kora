using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactRetentionTypedAndCurrentNameVoiceOpenNativeReviewWithoutReasoningOrHoldWrite(bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        SessionRetentionReview? opened = null;
        var store = BindRetentionCommands(fixture, (review, admitted, token) =>
        {
            admitted().Should().BeTrue();
            opened = review;
            return Task.CompletedTask;
        });
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        async Task Run(string command)
        {
            if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
            else { await fixture.RunAsync(command); }
        }
        await Run("session retention status " + id);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command observed.");
        await Run("session retention review " + id + " keep");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command review.");
        using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var reviewId = result.RootElement.GetProperty("retentionReview").GetProperty("id").GetString();
        await Run("session retention confirm " + id + " " + reviewId);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command native-review.");
        opened!.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
        await Run("session retention confirm " + id + " " + reviewId);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
    }

    [Theory]
    [InlineData("unprefixed")]
    [InlineData("old-name")]
    [InlineData("invalid")]
    [InlineData("unbound")]
    [InlineData("protected-call")]
    public async Task ReservedRetentionInputNeverBecomesModelOrQuestionText(string reason)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRetentionCommands(fixture, NeverOpenNativeRetention);
        if (reason is "unbound")
        {
            var unbound = await Fixture.CreateInitializedAsync();
            BindSessions(unbound);
            await unbound.RaiseActivatedTranscriptAsync("Kora, session retention status " + store.Session.Authority.SessionId.Value.ToString("D"), 1);
            unbound.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
            unbound.Reasoner.Requests.Should().BeEmpty();
            return;
        }
        if (reason is "protected-call")
        {
            fixture.CallState.SetState(CallState.Active);
            await fixture.Dispatcher.LastInvocation;
        }
        var input = "session retention review " + store.Session.Authority.SessionId.Value.ToString("D") + " keep";
        if (reason is "invalid") { input = "session retention review selected keep"; }
        await fixture.RaiseActivatedTranscriptAsync((reason is "unprefixed" ? "" : reason is "old-name" ? "Nova, " : "Kora, ") + input, 1);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Session command review.");
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("privacy")]
    [InlineData("invalid")]
    [InlineData("revoked")]
    [InlineData("rebound")]
    public async Task RetentionCommandReviewsAreRetiredByOriginalInputTransitions(string transition)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRetentionCommands(fixture, NeverOpenNativeRetention);
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RunAsync("session retention review " + id + " ordinary");
        using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var reviewId = result.RootElement.GetProperty("retentionReview").GetProperty("id").GetString();
        if (transition is "name")
        {
            fixture.ViewModel.AssistantNameInput = "Nova";
            await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        }
        if (transition is "privacy")
        {
            fixture.ViewModel.CommandText = "session retention review " + id + " keep";
            fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true);
            await fixture.ViewModel.PrivacyClosureTask;
            fixture.ViewModel.CommandText.Should().BeEmpty();
        }
        if (transition is "invalid") { await fixture.RunAsync("session retention nonsense"); }
        if (transition is "revoked") { fixture.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId); }
        if (transition is "rebound") { BindRetentionCommands(fixture, NeverOpenNativeRetention); }
        if (transition is not "privacy")
        {
            await fixture.RunAsync("session retention confirm " + id + " " + reviewId);
            fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        }
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    private static Task NeverOpenNativeRetention(SessionRetentionReview review, Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("Native review must not open.");

    private static SessionCommandStore BindRetentionCommands(Fixture fixture,
        Func<SessionRetentionReview, Func<bool>, CancellationToken, Task> open)
    {
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        fixture.ViewModel.BindSessionCommands(workspace);
        fixture.ViewModel.BindSessionRetentionCommands(new(workspace, store, TimeProvider.System), open);
        return store;
    }

    private sealed partial class SessionCommandStore : ISessionRetentionControlStore
    {
        public ValueTask<long> ValidatePassiveInspectionAsync(CancellationToken token) =>
            throw new InvalidOperationException("Retention commands must not inspect content.");
        public ValueTask<SessionRetentionObservation> ReadRetentionObservationAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            ValueTask.FromResult(new SessionRetentionObservation(new(session, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddDays(1), DateTimeOffset.UnixEpoch.AddDays(30), false, false), Session.Authority.Generation, 0, false));
        public ValueTask<SessionRetentionObservation> SetRetentionHoldAsync(HostRequest request, SessionRetentionObservation expected,
            bool perpetual, Func<bool> admitted, CancellationToken token) =>
            throw new InvalidOperationException("Command opening must not write a hold or impersonate native input.");
    }
}
