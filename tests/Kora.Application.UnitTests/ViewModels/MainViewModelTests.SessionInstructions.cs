using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionScopeCommandsPreserveOriginalChannelAndCompleteDispatchWithoutReasoner(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        BindSessions(f);
        var received = new List<SessionCommandOperation>();
        f.ViewModel.BindSessionInstructionScopes((command, origin, eligible, _) =>
        {
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            eligible().Should().BeTrue();
            received.Add(command.Operation);
            return Task.FromResult("Scope status, no definition text.");
        }, static () => { });
        foreach (var command in new[] { "session instruction apply \"example\"", "session instruction confirm",
            "session instruction status", "session instruction stop" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
            else { await f.RunAsync(command); }
        }
        received.Should().Equal(SessionCommandOperation.InstructionScopeReview, SessionCommandOperation.InstructionScopeConfirm,
            SessionCommandOperation.InstructionScopeStatus, SessionCommandOperation.InstructionScopeStop);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SessionScopeRejectsUnprefixedVoiceAndRetiresOnPrivacyClosure()
    {
        var f = await Fixture.CreateInitializedAsync();
        BindSessions(f);
        var calls = 0; var retires = 0;
        f.ViewModel.BindSessionInstructionScopes((_, _, _, _) =>
        { calls++; return Task.FromResult("scope"); }, () => retires++);
        await f.RaiseActivatedTranscriptAsync("session instruction confirm", 1);
        calls.Should().Be(0);
        f.ViewModel.CloseForObservedPrivacyEvent("locked", true);
        retires.Should().BeGreaterThan(0);
        f.ViewModel.BindSessionInstructionScopes(null, null);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("consent")]
    public async Task SessionScopeVoiceRetainsArrivalCaptureAcrossDispatcherQueue(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        BindSessions(f);
        var calls = 0;
        f.ViewModel.BindSessionInstructionScopes((_, _, _, _) =>
        { calls++; return Task.FromResult("scope"); }, static () => { });
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var consent = Task.CompletedTask;
        f.Dispatcher.BeforeInvoke = () =>
        {
            if (change is "generation") { f.Voice.InvalidateCapture(); }
            else { consent = f.ViewModel.SetVoiceConsentAsync(false); }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, session instruction apply \"example\"", 1);
        await consent;
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeScopeEligibilityPinsNameAndPrivacyAndUnboundCommandsFailExplicitly()
    {
        var f = await Fixture.CreateInitializedAsync();
        BindSessions(f);
        var native = f.ViewModel.CaptureSessionInstructionScopeEligibility();
        native().Should().BeTrue();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        native().Should().BeFalse();
        await f.ViewModel.SetAssistantNameAsync("Kora", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        native().Should().BeFalse("restoring a readable name cannot restore an old source epoch");
        native = f.ViewModel.CaptureSessionInstructionScopeEligibility();
        native().Should().BeTrue();
        await f.RunAsync("session instruction status");
        f.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        f.ViewModel.CloseForObservedPrivacyEvent("locked", true);
        native().Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
