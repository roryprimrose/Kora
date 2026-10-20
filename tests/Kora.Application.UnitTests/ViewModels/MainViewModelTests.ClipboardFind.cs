using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClipboardFindOnlyPreparesExactRetainedSourceWithoutContentInAnyGenericSurface(bool voice)
    {
        var logger = new MemoryPrivacyLogger();
        var fixture = new Fixture(logger: logger);
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, clipboard find", 1); }
        await fixture.ViewModel.SetAssistantNameAsync("Nova");
        fixture.ClipboardReader.Text = "PRIVATE SOURCE e\u0301界🙂 .*? \"quoted\"";
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        const string literal = "  PrIvAtE e\u0301界🙂 .*? \"quoted\"  ";
        var command = "clipboard find " + snapshot.SnapshotId.ToString("D") + " " + literal;
        var callbacks = 0;
        PreparedClipboardFindEventArgs? retired = null;
        var auditCount = fixture.Audit.Events.Count;
        fixture.ViewModel.ClipboardFindPreparationRequested += (_, request) =>
        {
            callbacks++;
            retired = request;
            request.Snapshot.Should().BeSameAs(snapshot);
            request.IsCurrent.Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            request.TryPrepare((query, eligible) =>
            {
                query.Should().Be(literal);
                eligible().Should().BeTrue();
                return true;
            }).Should().BeTrue();
            request.TryPrepare((_, _) => throw new InvalidOperationException("No second consumption")).Should().BeFalse();
        };
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Nova, " + command, 1); }
        else { await fixture.RunAsync(command); }
        callbacks.Should().Be(1);
        retired!.IsCurrent.Should().BeFalse();
        retired.TryPrepare((_, _) => throw new InvalidOperationException("No late preparation")).Should().BeFalse();
        fixture.ViewModel.ClipboardPreview.Should().BeSameAs(snapshot);
        fixture.ClipboardReader.Calls.Should().Be(1);
        fixture.ViewModel.ResponseTitle.Should().Contain("PREPARED / REVIEW NEEDED");
        fixture.ViewModel.ResponseBody.Should().Contain("No Find has run").And.NotContain(literal).And.NotContain(snapshot.Text);
        fixture.ViewModel.Transcript.Should().NotContain(literal).And.NotContain(snapshot.Text).And.NotContain(snapshot.SnapshotId.ToString("D"));
        fixture.TextToSpeech.SpokenText.Should().NotContain(literal);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Audit.Events.Should().HaveCount(auditCount);
        logger.Messages.Should().NotContain(message => message.Contains(literal, StringComparison.Ordinal)
            || message.Contains(snapshot.Text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("wrong-id")]
    [InlineData("malformed")]
    [InlineData("no-consumer")]
    [InlineData("no-window")]
    [InlineData("denied-window")]
    [InlineData("system")]
    [InlineData("unprefixed-voice")]
    [InlineData("voice-owner")]
    [InlineData("owner")]
    [InlineData("unavailable-name")]
    public async Task ClipboardFindFailsClosedWithoutAnExactSourceOriginalAuthorityAndNativeConsumer(string state)
    {
        var fixture = new Fixture();
        if (state is "unprefixed-voice" or "voice-owner")
        { await fixture.RaiseActivatedTranscriptAsync("Kora, clipboard find", 1); }
        if (state is not "none") { await fixture.ViewModel.PreviewClipboardAsync(); }
        var snapshot = fixture.ViewModel.ClipboardPreview;
        var id = state is "wrong-id" or "none" ? Guid.NewGuid() : snapshot!.SnapshotId;
        var command = state is "malformed" ? "clipboard find PRIVATE invalid" : "clipboard find " + id.ToString("D") + " PRIVATE";
        var calls = 0;
        if (state is not "no-consumer")
        {
            fixture.ViewModel.ClipboardFindPreparationRequested += (_, request) =>
            {
                calls++;
                if (state is not "no-window") { request.TryPrepare((_, _) => state is not "denied-window"); }
            };
        }
        if (state is "owner") { fixture.ViewModel.BindClipboardOwnershipGate(() => false); }
        if (state is "unavailable-name")
        {
            fixture.NamePreferences.Name = "Supported Commands";
            await fixture.ViewModel.InitializeAsync();
        }
        if (state is "voice-owner") { fixture.ViewModel.BindVoiceOwnershipGate(() => false); }
        if (state is "system")
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                HostActivityLayer.Application, HostOperation.Request);
            await fixture.RunAsync(command);
        }
        else if (state is "unprefixed-voice" or "voice-owner")
        { await fixture.RaiseActivatedTranscriptAsync((state is "voice-owner" ? "Kora, " : string.Empty) + command, 1); }
        else { await fixture.RunAsync(command); }
        calls.Should().Be(state is "no-window" or "denied-window" ? 1 : 0);
        fixture.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        fixture.ViewModel.Transcript.Should().NotContain("PRIVATE");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ClipboardReader.Calls.Should().Be(state is "none" ? 0 : 1);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("replace")]
    [InlineData("cancel")]
    [InlineData("owner")]
    [InlineData("owner-binding")]
    [InlineData("owner-epoch")]
    [InlineData("owner-rebind")]
    [InlineData("call")]
    [InlineData("lock")]
    [InlineData("privacy")]
    [InlineData("name")]
    [InlineData("input")]
    [InlineData("navigation")]
    [InlineData("dispose")]
    public async Task DeferredClipboardFindCannotSurviveSourceOrOriginalAdmissionEpochChanges(string change)
    {
        var fixture = new Fixture();
        long owner = 1;
        fixture.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var callbacks = 0;
        fixture.ViewModel.ClipboardFindPreparationRequested += (_, request) => { callbacks++; request.TryPrepare((_, _) => true); };
        fixture.Dispatcher.DeferPosts = true;
        await fixture.RunAsync("clipboard find " + snapshot.SnapshotId.ToString("D") + " PRIVATE");
        switch (change)
        {
            case "clear": fixture.ViewModel.ClearClipboardPreview(); break;
            case "replace": await fixture.ViewModel.PreviewClipboardAsync(); break;
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "owner-binding": fixture.ViewModel.BindClipboardOwnershipGate(() => true); break;
            case "owner-epoch": owner++; break;
            case "owner-rebind": fixture.ViewModel.BindCallOwnershipGate(() => true, () => owner); break;
            case "call": await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken); break;
            case "lock": fixture.Session.IsUnlocked = false; break;
            case "privacy": fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with
                { TopologyRevision = fixture.PrivacyObservation.Current.TopologyRevision + 1 }; break;
            case "name": await fixture.ViewModel.SetAssistantNameAsync("Nova"); break;
            case "input": await fixture.RunAsync("clipboard find"); break;
            case "navigation": await fixture.RunAsync("open all work"); break;
            default: fixture.ViewModel.Dispose(); break;
        }
        fixture.Dispatcher.FlushPosts();
        callbacks.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.Transcript.Should().NotContain("PRIVATE");
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("voice")]
    [InlineData("owner-binding")]
    public async Task DeferredClipboardFindRequiresOriginalActivatedVoiceConsentLifetimeAndOwner(string change)
    {
        var fixture = new Fixture();
        await fixture.RaiseActivatedTranscriptAsync("Kora, clipboard find", 1);
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var callbacks = 0;
        fixture.ViewModel.ClipboardFindPreparationRequested += (_, request) => { callbacks++; request.TryPrepare((_, _) => true); };
        fixture.Dispatcher.DeferPosts = true;
        await fixture.RaiseActivatedTranscriptAsync("Kora, clipboard find " + snapshot.SnapshotId.ToString("D") + " PRIVATE", 1);
        if (change is "consent") { await fixture.ViewModel.SetVoiceConsentAsync(false); }
        else if (change is "voice") { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        else { fixture.ViewModel.BindVoiceOwnershipGate(() => true); }
        fixture.Dispatcher.FlushPosts();
        callbacks.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
    }

    [Theory]
    [InlineData("before")]
    [InlineData("inside")]
    [InlineData("unused")]
    [InlineData("after")]
    public async Task ClipboardFindOneUseHandoffExpiresAndRevalidatesAtTheNativeBoundary(string boundary)
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        PreparedClipboardFindEventArgs? captured = null;
        fixture.ViewModel.ClipboardFindPreparationRequested += (_, request) =>
        {
            captured = request;
            if (boundary is "unused") { return; }
            if (boundary is "before") { fixture.ViewModel.ClearClipboardPreview(); }
            if (boundary is "after")
            {
                request.TryPrepare((_, _) => true).Should().BeTrue();
                fixture.ViewModel.ClearClipboardPreview();
                return;
            }
            request.TryPrepare((_, eligible) =>
            {
                fixture.ViewModel.ClearClipboardPreview();
                eligible().Should().BeFalse();
                return true;
            }).Should().BeFalse();
        };
        await fixture.RunAsync("clipboard find " + snapshot.SnapshotId.ToString("D") + " PRIVATE");
        captured!.IsCurrent.Should().BeFalse();
        captured.TryPrepare((_, _) => throw new InvalidOperationException("No late callback")).Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData("question", false)]
    [InlineData("question", true)]
    [InlineData("grant", false)]
    [InlineData("grant", true)]
    [InlineData("action", false)]
    [InlineData("action", true)]
    public async Task ReservedClipboardFindNeverAnswersCancelsOrApprovesPendingDecisions(string pending, bool malformed)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        if (pending is "question")
        {
            fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
            await fixture.RunAsync("choose");
            await fixture.ViewModel.ActiveReasoningTask!;
        }
        else if (pending is "grant")
        {
            await fixture.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
                BuiltInAction.LockMachine, ModelApprovalScope.Always));
        }
        else
        {
            fixture.Reasoner.Action = BuiltInAction.LockMachine;
            await fixture.RunAsync("please lock");
            await fixture.ViewModel.ActiveReasoningTask!;
        }
        var calls = fixture.Reasoner.Requests.Count;
        await fixture.RunAsync(malformed ? "clipboard find INVALID approve once PRIVATE"
            : "clipboard find " + snapshot.SnapshotId.ToString("D") + " One approve once PRIVATE");
        fixture.ViewModel.IsModelQuestionPending.Should().Be(pending is "question");
        fixture.ViewModel.IsGrantChangePending.Should().Be(pending is "grant");
        fixture.ViewModel.IsModelActionApprovalPending.Should().Be(pending is "action");
        fixture.Reasoner.Requests.Should().HaveCount(calls);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.Transcript.Should().NotContain("PRIVATE");
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task DeferredClipboardFindPresentationUsesLinkedContentFreeHostContinuation()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var stopped = new List<Activity>();
        hostListener.ActivityStopped = stopped.Add;
        fixture.Dispatcher.DeferPosts = true;
        fixture.ViewModel.ClipboardFindPreparationRequested += (_, request) => request.TryPrepare((_, _) => true);
        await fixture.RunAsync("clipboard find " + snapshot.SnapshotId.ToString("D") + " PRIVATE");
        fixture.Dispatcher.FlushPosts();
        var presentation = stopped.Single(activity => activity.OperationName is "presentation.update");
        presentation.Links.Should().ContainSingle();
        presentation.Status.Should().Be(ActivityStatusCode.Ok);
        presentation.TagObjects.Should().NotContain(tag => (tag.Value as string ?? string.Empty).Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("owner-rebind")]
    [InlineData("private")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("name")]
    public async Task ClipboardFindVoiceReceiptCapturesSourceAndAuthorityBeforeInitialUiDispatch(string change)
    {
        var fixture = new Fixture();
        await fixture.RaiseActivatedTranscriptAsync("Kora, clipboard find", 1);
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var callbacks = 0;
        fixture.ViewModel.ClipboardFindPreparationRequested += (_, request) => { callbacks++; request.TryPrepare((_, _) => true); };
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.Dispatcher.BeforeInvoke = () =>
        {
            switch (change)
            {
                case "source": fixture.ViewModel.PreviewClipboardAsync().IsCompletedSuccessfully.Should().BeTrue(); break;
                case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => true); break;
                case "owner-rebind": fixture.ViewModel.BindCallOwnershipGate(() => true); break;
                case "private": fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with
                    { TopologyRevision = fixture.PrivacyObservation.Current.TopologyRevision + 1 }; break;
                case "call": fixture.CallState.SetState(Kora.Core.Communication.CallState.Unknown); break;
                case "input": fixture.RunAsync("clipboard find").IsCompletedSuccessfully.Should().BeTrue(); break;
                case "name": fixture.ViewModel.SetAssistantNameAsync("Nova").IsCompletedSuccessfully.Should().BeTrue(); break;
            }
        };
        await fixture.Voice.RaiseTranscriptAsync("Kora, clipboard find " + snapshot.SnapshotId.ToString("D") + " PRIVATE", 1);
        callbacks.Should().Be(0);
        fixture.ViewModel.Transcript.Should().NotContain("PRIVATE");
        fixture.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }
}
