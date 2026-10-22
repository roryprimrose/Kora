using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string PendingNavigation = "open pending question aaaaaaaa-1111-2222-3333-444444444444 bbbbbbbb-1111-2222-3333-444444444444";

    [Fact]
    public async Task PendingQuestionNavigationFocusesOnlyCapturedTargetAndPreservesResponsesAndComposer()
    {
        var f = await Fixture.CreateInitializedAsync();
        var native = new PendingNavigationFake();
        f.ViewModel.BindPendingQuestionNavigation(native);
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var transcript = f.ViewModel.Transcript;
        await f.RunAsync(PendingNavigation);
        native.FocusCalls.Should().Be(1);
        native.Admission!().Should().BeFalse();
        native.Session.Should().Be(Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444"));
        native.Task.Should().Be(Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444"));
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Exact pending question focused.");
        f.ViewModel.CommandText.Should().Be(PendingNavigation);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Be(transcript);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pending question help")]
    [InlineData("open pending question")]
    [InlineData("open pending question invalid invalid")]
    public async Task PendingQuestionHelpAndMalformedCommandsDoNotReadThePresenterOrAnswer(string text)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var transcript = f.ViewModel.Transcript;
        var requests = f.Reasoner.Requests.Count;
        var native = new PendingNavigationFake();
        f.ViewModel.BindPendingQuestionNavigation(native);
        await f.RunAsync(text);
        native.Captures.Should().Be(0);
        f.ViewModel.PendingQuestionNavigationStatus.Should().Be(PendingQuestionCommand.Help);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Be(transcript);
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("no-target")]
    [InlineData("no-ack")]
    [InlineData("native-error")]
    [InlineData("capture-error")]
    [InlineData("dispatcher-error")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("edit")]
    [InlineData("rebind")]
    [InlineData("dispose")]
    public async Task PendingQuestionNavigationFailsClosedWithoutReplacingGenericResponse(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var native = new PendingNavigationFake
        {
            Available = !string.Equals(loss, "no-target", StringComparison.Ordinal),
            Acknowledge = !string.Equals(loss, "no-ack", StringComparison.Ordinal),
            Failure = string.Equals(loss, "native-error", StringComparison.Ordinal) ? new IOException("unavailable") : null,
            CaptureFailure = string.Equals(loss, "capture-error", StringComparison.Ordinal) ? new IOException("unavailable") : null,
        };
        if (!string.Equals(loss, "unbound", StringComparison.Ordinal)) { f.ViewModel.BindPendingQuestionNavigation(native); }
        if (string.Equals(loss, "dispatcher-error", StringComparison.Ordinal)) { f.Dispatcher.InvokeException = new InvalidOperationException("unavailable"); }
        var body = f.ViewModel.ResponseBody;
        var transcript = f.ViewModel.Transcript;
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (loss)
            {
                case "private": f.Session.IsUnlocked = false; break;
                case "owner": owner++; break;
                case "call": f.CallState.SetState(CallState.Active); break;
                case "edit": f.ViewModel.CommandText = "independent edit"; break;
                case "rebind": f.ViewModel.BindPendingQuestionNavigation(null); break;
                case "dispose": f.ViewModel.Dispose(); break;
            }
        };
        await f.RunAsync(PendingNavigation);
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable.");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Be(transcript);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Nova")]
    [InlineData("What Time")]
    public async Task PendingQuestionNavigationUsesCurrentActivatedVoiceWithoutTouchingDirtyComposer(string assistantName)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to " + assistantName);
        var native = new PendingNavigationFake();
        f.ViewModel.BindPendingQuestionNavigation(native);
        f.ViewModel.CommandText = "unfinished independent answer";
        await f.RaiseActivatedTranscriptAsync(assistantName + ", " + PendingNavigation, 1);
        native.FocusCalls.Should().Be(1);
        f.ViewModel.CommandText.Should().Be("unfinished independent answer");
        await f.RaiseActivatedTranscriptAsync(PendingNavigation, 1);
        native.FocusCalls.Should().Be(1);
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable.");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PendingQuestionNavigationReportsRevocationDuringNativeAcknowledgement()
    {
        var f = await Fixture.CreateInitializedAsync();
        var native = new PendingNavigationFake { DuringFocus = () => f.ViewModel.CommandText = "changed" };
        f.ViewModel.BindPendingQuestionNavigation(native);
        await f.RunAsync(PendingNavigation);
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable.");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("composer")]
    [InlineData("binding")]
    public async Task PendingQuestionNavigationRejectsReentrantCaptureWithoutRecapturingAuthority(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        PendingNavigationFake? native = null;
        native = new()
        {
            DuringCapture = () =>
            {
                switch (change)
                {
                    case "owner": owner++; break;
                    case "composer": f.ViewModel.CommandText = "independent edit"; break;
                    case "binding":
                        f.ViewModel.BindPendingQuestionNavigation(null);
                        f.ViewModel.BindPendingQuestionNavigation(native);
                        break;
                }
            },
        };
        f.ViewModel.BindPendingQuestionNavigation(native);
        await f.RunAsync(PendingNavigation);
        native.FocusCalls.Should().Be(0);
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable.");
    }

    [Fact]
    public async Task PendingQuestionNavigationCapturesComposerBeforeTheOriginalVoiceQueue()
    {
        var f = await Fixture.CreateInitializedAsync();
        var native = new PendingNavigationFake();
        f.ViewModel.BindPendingQuestionNavigation(native);
        f.ViewModel.CommandText = "original dirty composer";
        f.Dispatcher.BeforeInvoke = () => f.ViewModel.CommandText = "new dirty composer";
        await f.RaiseActivatedTranscriptAsync("Kora, " + PendingNavigation, 1);
        native.FocusCalls.Should().Be(0);
        f.ViewModel.CommandText.Should().Be("new dirty composer");
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable.");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.TypedCommand, RequestOrigin.HostSystem)]
    public async Task PendingQuestionNavigationRejectsNonOriginalCommandAuthority(SecurityAuditInitiator initiator, RequestOrigin origin)
    {
        var f = await Fixture.CreateInitializedAsync();
        var native = new PendingNavigationFake();
        f.ViewModel.BindPendingQuestionNavigation(native);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        await InvokeTranscriptForTest(f.ViewModel, PendingNavigation, initiator);
        native.Captures.Should().Be(0);
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable.");
    }

    [Fact]
    public async Task PendingQuestionNavigationRejectsStaleVoiceBeforeGenericFeedbackOrQuestionCapture()
    {
        var f = await Fixture.CreateInitializedAsync();
        var native = new PendingNavigationFake();
        f.ViewModel.BindPendingQuestionNavigation(native);
        var body = f.ViewModel.ResponseBody;
        f.Dispatcher.BeforeInvoke = () => f.Voice.InvalidateCapture();
        await f.RaiseActivatedTranscriptAsync("Kora, " + PendingNavigation, 1);
        native.FocusCalls.Should().Be(0);
        f.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable:");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PendingQuestionNavigationRefusesUnavailableSavedNameAndProtectsPendingGrantReview()
    {
        var invalid = new Fixture();
        invalid.NamePreferences.Name = "\0";
        await invalid.ViewModel.InitializeAsync();
        invalid.ViewModel.BindPendingQuestionNavigation(new PendingNavigationFake());
        await invalid.RunAsync(PendingNavigation);
        invalid.ViewModel.PendingQuestionNavigationStatus.Should().StartWith("Pending question navigation unavailable.");

        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.LockMachine, ModelApprovalScope.Always));
        var body = f.ViewModel.ResponseBody;
        var transcript = f.ViewModel.Transcript;
        f.ViewModel.BindPendingQuestionNavigation(new PendingNavigationFake());
        await f.RunAsync(PendingNavigation);
        await f.RunAsync("open pending question invalid invalid");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Be(transcript);
        f.Session.LockCalls.Should().Be(0);
    }

    private sealed class PendingNavigationFake : IPendingQuestionNavigation, IPendingQuestionFocus
    {
        internal int Captures { get; private set; }
        internal int FocusCalls { get; private set; }
        internal Guid Session { get; private set; }
        internal Guid Task { get; private set; }
        internal bool Available { get; init; } = true;
        internal bool Acknowledge { get; init; } = true;
        internal Exception? Failure { get; init; }
        internal Exception? CaptureFailure { get; init; }
        internal Action? DuringFocus { get; init; }
        internal Action? DuringCapture { get; init; }
        internal Func<bool>? Admission { get; private set; }
        public IPendingQuestionFocus? CaptureFocus(Guid sessionId, Guid taskId)
        {
            Captures++;
            if (CaptureFailure is not null) { throw CaptureFailure; }
            DuringCapture?.Invoke();
            Session = sessionId;
            Task = taskId;
            return Available ? this : null;
        }
        public Task<bool> FocusAsync(Func<bool> isAdmitted)
        {
            isAdmitted().Should().BeTrue();
            Admission = isAdmitted;
            FocusCalls++;
            if (Failure is not null) { throw Failure; }
            DuringFocus?.Invoke();
            return System.Threading.Tasks.Task.FromResult(Acknowledge);
        }
    }
}
