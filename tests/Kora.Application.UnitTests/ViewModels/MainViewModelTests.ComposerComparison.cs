using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string CompareInput = "session draft compare 12345678-1234-1234-1234-123456abcdef";

    [Fact]
    public async Task CompareAssistantNameCollisionNeverTurnsLiteralFamilyIntoVoiceActivation()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to Session");
        f.ViewModel.AssistantName.Should().Be("Session");
        var calls = 0;
        f.ViewModel.BindComposerComparisonNavigation(new ComparisonNavigation
        {
            CaptureLaunch = (_, _, admitted) => { calls++; return new(admitted, () => Task.FromResult(true)); },
        });
        await f.RaiseActivatedTranscriptAsync(CompareInput, 1);
        calls.Should().Be(0);
        await f.RaiseActivatedTranscriptAsync("Session, " + CompareInput, 1);
        calls.Should().Be(1);
        await f.RunAsync(CompareInput);
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompareVoiceGenerationLossCannotBeRetaggedAsNativeConsent(bool firstDispatch)
    {
        var f = new Fixture();
        await f.RaiseActivatedTranscriptAsync("Kora, what can you do", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var calls = 0;
        f.ViewModel.BindComposerComparisonNavigation(new ComparisonNavigation
        {
            CaptureLaunch = (_, _, admitted) => { calls++; return new(admitted, () => Task.FromResult(true)); },
        });
        f.Dispatcher.BeforeInvoke = firstDispatch
            ? f.Voice.AdvanceGeneration
            : () => f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration;
        await f.Voice.RaiseTranscriptAsync("Kora, " + CompareInput, 1);
        calls.Should().Be(firstDispatch ? 0 : 1);
        f.ViewModel.Transcript.Should().Contain("Compare unavailable");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("protected")]
    [InlineData("name")]
    [InlineData("external")]
    [InlineData("initiator")]
    public async Task CompareCannotAcquireInitialHostAdmission(string boundary)
    {
        var f = new Fixture();
        if (boundary is "name") { f.NamePreferences.Name = "Supported Commands"; }
        await f.ViewModel.InitializeAsync();
        if (boundary is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (boundary is "protected")
        {
            f.CallState.SetState(Kora.Core.Communication.CallState.Active);
            await f.Dispatcher.LastInvocation;
        }
        var captures = 0;
        f.ViewModel.BindComposerComparisonNavigation(new ComparisonNavigation
        {
            CaptureLaunch = (_, _, _) => { captures++; return null; },
        });
        if (boundary is "external")
        {
            await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, () => f.RunAsync(CompareInput));
        }
        else if (boundary is "initiator")
        {
            var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
                "HandleTranscriptAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await (Task)routing.Invoke(f.ViewModel, [CompareInput, 1f, SecurityAuditInitiator.LocalUser, null, null, null])!;
        }
        else { await f.RunAsync(CompareInput); }
        captures.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("Compare unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompareQueuedDispatcherRevalidatesSourceAndOriginalInput(bool input)
    {
        var f = await Fixture.CreateInitializedAsync();
        var live = true;
        var opens = 0;
        f.ViewModel.BindComposerComparisonNavigation(new ComparisonNavigation
        {
            CaptureLaunch = (_, _, _) => new(() => live, () => { opens++; return Task.FromResult(true); }),
        });
        f.Dispatcher.BeforeInvoke = () =>
        {
            if (input) { f.ViewModel.CommandText = "changed"; }
            else { live = false; }
        };
        await f.RunAsync(CompareInput);
        opens.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("Compare unavailable");
    }

    private sealed class ComparisonNavigation : IComposerComparisonNavigation
    {
        internal Func<HostId<SessionIdentity>, RequestOrigin, Func<bool>, ComposerComparisonLaunch?> CaptureLaunch { get; set; } =
            (_, _, _) => null;
        public ComposerComparisonLaunch? Capture(HostId<SessionIdentity> session, RequestOrigin origin, Func<bool> admitted) =>
            CaptureLaunch(session, origin, admitted);
    }

    [Theory]
    [InlineData("Nova")]
    [InlineData("What Time")]
    public async Task CompareTypedAndActivatedVoiceCaptureBeforeFeedbackAndPublishOnlyNativeAcknowledgement(string assistantName)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to " + assistantName);
        var captures = 0;
        var opens = 0;
        var origins = new List<RequestOrigin>();
        var native = new ComparisonNavigation
        {
            CaptureLaunch = (session, origin, admitted) =>
            {
                session.Value.Should().Be(Guid.Parse(CompareInput.Split(' ')[3]));
                admitted().Should().BeTrue();
                origins.Add(origin);
                captures++;
                return new(admitted, () => { opens++; return Task.FromResult(true); });
            },
        };
        f.ViewModel.BindComposerComparisonNavigation(native);
        await f.RunAsync(CompareInput);
        f.ViewModel.Transcript.Should().Contain("native view only");
        await f.RaiseActivatedTranscriptAsync(assistantName + ", " + CompareInput, 1);
        captures.Should().Be(2);
        opens.Should().Be(2);
        origins.Should().Equal(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        await f.RaiseActivatedTranscriptAsync(CompareInput, 1);
        captures.Should().Be(2);
        f.ViewModel.Transcript.Should().Contain("Compare unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("no-source")]
    [InlineData("stale")]
    [InlineData("focus")]
    [InlineData("focus-revoked")]
    [InlineData("capture-revoked")]
    [InlineData("throw")]
    public async Task CompareUnavailableNeverFallsThroughOrReportsSuccess(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        var admitted = true;
        var native = new ComparisonNavigation
        {
            CaptureLaunch = (_, _, _) =>
            {
                if (boundary is "throw") { throw new InvalidOperationException("native unavailable"); }
                if (boundary is "no-source") { return null; }
                if (boundary is "capture-revoked") { f.ViewModel.CommandText = "changed"; }
                return new(() => admitted && boundary is not "stale", () =>
                {
                    if (boundary is "focus-revoked") { admitted = false; }
                    return Task.FromResult(boundary is not "focus");
                });
            },
        };
        if (boundary is not "unbound") { f.ViewModel.BindComposerComparisonNavigation(native); }
        await f.RunAsync(CompareInput);
        f.ViewModel.Transcript.Should().Contain("Compare unavailable").And.Contain("explicitly read exact saved revision first");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareReservedMalformedAndHelpPreservePendingQuestionAndSecurityDecisions()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var requests = f.Reasoner.Requests.Count;
        foreach (var input in new[] { "session draft compare", "session draft compare!", "session draft compare\0",
            CompareInput + " extra", "session draft compare help" })
        {
            await f.RunAsync(input);
            f.ViewModel.IsModelQuestionPending.Should().BeTrue();
            f.Reasoner.Requests.Count.Should().Be(requests);
        }
        f.ViewModel.Transcript.Should().Be(SessionDraftCompareCommand.Syntax + ". " + SessionDraftCompareCommand.Boundary);
        await f.ViewModel.CancelModelQuestionAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await f.RunAsync(CompareInput);
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("edit-back")]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("name")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("presenter")]
    public async Task CompareOriginalAdmissionRetiresWithoutAcquiringNewAuthority(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        Func<bool>? source = null;
        f.ViewModel.BindComposerComparisonNavigation(new ComparisonNavigation
        {
            CaptureLaunch = (_, _, admitted) =>
            {
                source = admitted;
                return new(admitted, () => Task.FromResult(true));
            },
        });
        await f.RunAsync(CompareInput);
        source!().Should().BeTrue();
        switch (boundary)
        {
            case "input": await f.RunAsync("session draft compare help"); break;
            case "edit-back":
                f.ViewModel.CommandText = "different";
                f.ViewModel.CommandText = CompareInput;
                break;
            case "owner": owner++; break;
            case "binding": f.ViewModel.BindCallOwnershipGate(static () => true, () => owner); break;
            case "name": await f.RunAsync("set assistant name to Nova"); break;
            case "privacy": f.Session.IsUnlocked = false; break;
            case "call":
                f.CallState.SetState(Kora.Core.Communication.CallState.Active);
                await f.Dispatcher.LastInvocation;
                break;
            case "dispose": f.ViewModel.Dispose(); break;
            case "presenter": f.ViewModel.BindComposerComparisonNavigation(null); break;
        }
        source().Should().BeFalse();
    }
}
