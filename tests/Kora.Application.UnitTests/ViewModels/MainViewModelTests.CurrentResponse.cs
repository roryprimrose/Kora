using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComplexModelAnswerOffersItsExactFullSourceWhileCompactUiAndSpeechUseTheBrief(bool decline)
    {
        var f = new Fixture();
        f.Probe.Status = new Kora.Core.Dependencies.DependencyStatus("local.inference", "Local inference",
            Kora.Core.Dependencies.DependencyReadiness.Ready, "Synthetic ready provider");
        const string full = "Brief response:\nThe plan has two stages.\n\nDetailed response:\n# Full plan\n\n```mermaid\ngraph TD\n A-->B\n```";
        f.Reasoner.Response = new(full, null);
        await f.ViewModel.InitializeAsync();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        await f.RunAsync("prepare a detailed plan");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.ResponseBody.Should().Contain("The plan has two stages.").And.Contain(AnswerPresentation.Offer)
            .And.NotContain("graph TD");
        f.ViewModel.IsDetailsOfferPending.Should().BeTrue();
        f.ViewModel.WorkspaceSurfaceRequested += _ => Task.CompletedTask;
        await f.RunAsync("hide details");
        f.ViewModel.IsDetailsOfferPending.Should().BeTrue("navigation is not acceptance or source replacement");
        if (decline)
        {
            f.ViewModel.DeclineCurrentDetailsOffer();
            f.ViewModel.IsDetailsOfferPending.Should().BeFalse();
            native.Calls.Should().Be(0);
            await f.RunAsync("open current response details");
        }
        else { await f.RunAsync("show me the detailed results"); }
        native.Content!.Source.Should().Be(full);
        native.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().ContainSingle();
        await f.RunAsync("what version are you running");
        f.ViewModel.IsDetailsOfferPending.Should().BeFalse("an unrelated ordinary response must retire the old offer");
        f.ViewModel.RetireCurrentResponseSpeech();
        f.ViewModel.IsDetailsOfferPending.Should().BeFalse();
        f.ViewModel.DeclineCurrentDetailsOffer();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentResponseFailureDiagnosticsRespectEnabledLogging(bool enabled)
    {
        var capture = new Network.EnabledLogger<MainViewModel>();
        ILogger logger = enabled ? capture : NullLogger.Instance;

        CurrentResponseActionLog.CurrentResponseActionFailed(logger, nameof(IOException));

        capture.Messages.Should().HaveCount(enabled ? 1 : 0);
        if (enabled)
        {
            capture.Messages.Single().Should().Contain(nameof(IOException));
        }
    }

    [Theory]
    [InlineData("dismiss current response", "Kora", CurrentResponseAction.Dismiss)]
    [InlineData("Kora, open current response details!", "Kora", CurrentResponseAction.Details)]
    [InlineData("Nova current response help?", "Nova", CurrentResponseAction.Help)]
    [InlineData("Kora dismiss current response.", "Kora", CurrentResponseAction.Dismiss)]
    [InlineData("CURRENT RESPONSE HELP", "Kora", CurrentResponseAction.Help)]
    public void CurrentResponseGrammarAcceptsOnlyExactCurrentName(string input, string name, CurrentResponseAction action) =>
        CurrentResponseCommand.Parse(input, name)!.Action.Should().Be(action);

    [Theory]
    [InlineData("dismiss current response and approve once", true)]
    [InlineData("open current response details from clipboard", true)]
    [InlineData("dismiss\ncurrent response", true)]
    [InlineData("open\0current response details", true)]
    [InlineData("current response", true)]
    [InlineData("open current", true)]
    [InlineData("dismiss current response " + "extra", true)]
    [InlineData("please dismiss current response", false)]
    [InlineData("Nova, dismiss current response", false)]
    [InlineData("Kora", false)]
    [InlineData("Korax dismiss current response", false)]
    [InlineData("Kora.dismiss current response", false)]
    public void CurrentResponseGrammarReservesMalformedFamilies(string input, bool reserved)
    {
        var command = CurrentResponseCommand.Parse(input, "Kora");
        (command is not null).Should().Be(reserved);
        command?.Action.Should().BeNull();
        CurrentResponseCommand.Parse("dismiss current response " + new string('x', 1024), "Kora")!.Action.Should().BeNull();
    }

    [Theory]
    [InlineData(false, "typed", "Kora")]
    [InlineData(true, "typed", "Kora")]
    [InlineData(false, "voice", "Kora")]
    [InlineData(true, "voice", "Kora")]
    [InlineData(false, "native", "Kora")]
    [InlineData(true, "native", "Kora")]
    [InlineData(false, "voice", "What Time")]
    [InlineData(true, "voice", "What Time")]
    public async Task CurrentResponseRoutesConvergeWithoutModelSpeechCopyOrSettings(bool dismiss, string lane, string assistantName)
    {
        var f = await CreateReadableResponseAsync();
        await f.RunAsync("set assistant name to " + assistantName);
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var calls = f.TextToSpeech.SpeakCalls;
        var phrase = dismiss ? "dismiss current response" : "open current response details";
        if (string.Equals(lane, "native", StringComparison.Ordinal))
        {
            if (dismiss) { await f.ViewModel.DismissCurrentResponseAsync(); }
            else { await f.ViewModel.OpenCurrentResponseDetailsAsync(); }
        }
        else if (string.Equals(lane, "voice", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync(assistantName + ", " + phrase, 1); }
        else { await f.RunAsync(phrase); }
        native.Calls.Should().Be(1);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Be(dismiss ? "Current response dismissed." : "Exact current response details opened.");
        if (!dismiss)
        {
            native.Content!.Source.Should().Be(title + Environment.NewLine + body);
            native.Content.Origin.Should().Be(DetailContentOrigin.FinalizedResponse);
            native.Content.SessionSource.Should().NotBeNull();
        }
        f.TextToSpeech.SpeakCalls.Should().Be(calls);
        f.Reasoner.Requests.Should().BeEmpty();
        var audits = f.Audit.Events.Where(a => a.ActionId.StartsWith("response.", StringComparison.Ordinal)).ToArray();
        audits.Should().HaveCount(2);
        audits[0].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        audits[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        audits[0].TargetId.Should().Be(audits[1].TargetId);
        audits[0].Initiator.Should().Be(string.Equals(lane, "voice", StringComparison.Ordinal)
            ? SecurityAuditInitiator.VoiceCommand : string.Equals(lane, "typed", StringComparison.Ordinal)
                ? SecurityAuditInitiator.TypedCommand : SecurityAuditInitiator.LocalUser);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    public async Task CurrentResponsePreservesMissingAndPendingDecisionSources(string pending)
    {
        var f = CreateSummaryFixture(Kora.Core.Voice.SpeechProviderIds.Windows);
        await f.ViewModel.InitializeAsync();
        if (string.Equals(pending, "question", StringComparison.Ordinal)) { f.Reasoner.Question = new("Which?", ["A", "B"]); }
        if (string.Equals(pending, "approval", StringComparison.Ordinal)) { f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine; }
        if (f.Reasoner.Question is not null || f.Reasoner.Action is not null)
        {
            await f.RunAsync("choose something");
            await f.ViewModel.ActiveReasoningTask!;
        }
        if (string.Equals(pending, "grant", StringComparison.Ordinal))
        {
            await f.ViewModel.PrepareGrantChangeAsync(new(Kora.Core.Commands.GrantChangeOperation.Add,
                Kora.Core.Commands.BuiltInAction.LockMachine, Kora.Core.Commands.ModelApprovalScope.Always));
        }
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        var body = f.ViewModel.ResponseBody;
        var question = f.ViewModel.IsModelQuestionPending;
        var approval = f.ViewModel.IsModelActionApprovalPending;
        var grant = f.ViewModel.IsGrantChangePending;
        foreach (var phrase in new[] { "dismiss current response", "open current response details", "open current response details and approve once" })
        { await f.RunAsync(phrase); }
        native.Calls.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsModelQuestionPending.Should().Be(question);
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsGrantChangePending.Should().Be(grant);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("replaced")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("input-edit")]
    [InlineData("binding")]
    [InlineData("lifetime")]
    [InlineData("decision")]
    [InlineData("disposed")]
    [InlineData("voice")]
    [InlineData("name")]
    public async Task CurrentResponseRevalidatesQueuedSourceAndEveryAdmission(string change)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        var owner = 1L;
        if (string.Equals(change, "owner-revision", StringComparison.Ordinal))
        {
            f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
            await f.RunAsync("what power action is pending");
        }
        f.Audit.BeforeWrite = audit =>
        {
            if (!audit.ActionId.StartsWith("response.", StringComparison.Ordinal) || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.Audit.BeforeWrite = null;
            switch (change)
            {
                case "retired": f.ViewModel.RetireCurrentResponseSpeech(); break;
                case "replaced":
                    typeof(MainViewModel).GetMethod("ShowInformation", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(f.ViewModel, ["Replacement", "Never retarget to this", false]); break;
                case "privacy": f.Session.IsUnlocked = false; break;
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "owner-revision": owner++; break;
                case "call": f.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
                case "input": typeof(MainViewModel).GetField("workspaceSurfaceInputRevision", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(f.ViewModel, 500L); break;
                case "input-edit": f.ViewModel.CommandText = "changed input"; break;
                case "binding": f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel)); break;
                case "lifetime": native.Alive = false; break;
                case "decision": f.ViewModel.BindResponseReadDecisionGate(static () => false); break;
                case "disposed": f.ViewModel.Dispose(); break;
                case "voice": f.Voice.AdvanceGeneration(); break;
                case "name":
                    f.NamePreferences.Name = "Nova";
                    ((Kora.Application.Configuration.AssistantNameConfigurationService)typeof(MainViewModel)
                        .GetField("assistantNameConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.ViewModel)!).Reload();
                    break;
            }
        };
        if (string.Equals(change, "voice", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync("Kora, open current response details", 1); }
        else { await f.RunAsync("open current response details"); }
        native.Calls.Should().Be(0);
        f.ViewModel.Transcript.Should().NotBe("Exact current response details opened.");
        f.Audit.Events.Should().NotContain(a => a.ActionId == "response.details" && a.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("negative")]
    [InlineData("cancelled")]
    [InlineData("native-failed")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("lost-after-ack")]
    public async Task CurrentResponseNeverClaimsUnconfirmedNativeSuccess(string failure)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel) { Acknowledge = !string.Equals(failure, "negative", StringComparison.Ordinal) };
        if (!string.Equals(failure, "unbound", StringComparison.Ordinal)) { f.ViewModel.BindCurrentResponsePresentation(native); }
        if (string.Equals(failure, "cancelled", StringComparison.Ordinal)) { native.Failure = new OperationCanceledException(); }
        if (string.Equals(failure, "native-failed", StringComparison.Ordinal)) { native.Failure = new InvalidOperationException(); }
        f.Audit.BeforeWrite = audit =>
        {
            if (!audit.ActionId.StartsWith("response.", StringComparison.Ordinal)) { return; }
            if (string.Equals(failure, "requested-audit", StringComparison.Ordinal)) { throw new IOException(); }
            if (audit.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            if (string.Equals(failure, "terminal-audit", StringComparison.Ordinal)) { throw new IOException(); }
            if (string.Equals(failure, "lost-after-ack", StringComparison.Ordinal)) { f.ViewModel.RetireCurrentResponseSpeech(); }
        };
        await f.RunAsync("open current response details");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CurrentResponseHelpAndRenamedGrammarAreExactAndNonDestructive()
    {
        var f = await CreateReadableResponseAsync();
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("current response help");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Contain(CurrentResponseCommand.Syntax);
        await f.RunAsync("set assistant.name to Nova");
        await f.RunAsync("what power action is pending");
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        await f.RaiseActivatedTranscriptAsync("Nova, open current response details", 1);
        native.Calls.Should().Be(1);
        f.Voice.StartedPhrases.Should().Contain("Nova dismiss current response");
        await f.RunAsync("what can you do");
        f.ViewModel.ResponseBody.Should().Contain(CurrentResponseCommand.Syntax);
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("grant-editor")]
    [InlineData("response-action")]
    [InlineData("decision")]
    [InlineData("clipboard")]
    [InlineData("provider-preview")]
    [InlineData("audit-preview")]
    [InlineData("diagnostic-preview")]
    [InlineData("session-preview")]
    public async Task CurrentResponseRefusesSecurityPreviewsWithoutChangingThem(string preview)
    {
        var f = new Fixture(enableProviderProfiles: true, enableAuditRetention: true, enableAuditImpact: true,
            enableDiagnosticRetention: true, enableDiagnosticImpact: true,
            enableSessionRetention: true, enableSessionRetentionPlanner: true);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("what power action is pending");
        switch (preview)
        {
            case "busy": SetResponseProperty(f.ViewModel, "IsBusy", true); break;
            case "grant-editor": SetResponseProperty(f.ViewModel, "IsGrantEditorVisible", true); break;
            case "response-action": SetResponseProperty(f.ViewModel, "ResponseActions",
                new ResponseAction[] { new(ResponseActionKind.OpenVoiceSettings, "Review") }); break;
            case "decision": f.ViewModel.BindResponseReadDecisionGate(static () => false); break;
            case "clipboard":
                f.ClipboardReader.Text = "private clipboard source";
                await f.ViewModel.PreviewClipboardAsync(); break;
            case "provider-preview":
                typeof(MainViewModel).GetField("providerFieldPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(f.ViewModel, new Kora.Application.Configuration.ProviderProfileChoice(
                        new(Kora.Core.Providers.ProviderService.Local, []), 1, Guid.NewGuid(),
                        new(new(Guid.NewGuid()), new(1), true), RequestOrigin.LocalUi, static () => true)); break;
            case "audit-preview":
                f.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
                f.ViewModel.ProposedAuditImpactDays = 30;
                await f.ViewModel.PreviewAuditImpactCommand.ExecuteAsync(); break;
            case "diagnostic-preview":
                f.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
                f.ViewModel.ProposedDiagnosticImpactDays = 1;
                await f.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync(); break;
            case "session-preview":
                f.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
                f.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
                await f.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
                f.ViewModel.ProposedSessionArchiveDays = 2;
                f.ViewModel.ProposedSessionDeleteDays = 3;
                await f.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync(); break;
        }
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        var body = f.ViewModel.ResponseBody;
        await f.ViewModel.DismissCurrentResponseAsync();
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        native.Calls.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CurrentResponseRefusesFileAndFolderSourcesButAnEmptyBoundServiceIsNotABlocker(bool folder, bool confirmed)
    {
        var f = await CreateReadableResponseAsync();
        Kora.Core.Context.ILocalFileInspector inspector = folder ? new FolderInspector() : new FakeFileInspector();
        using var preview = new Kora.Tools.Files.LocalFilePreview(inspector, f.Audit, TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Tools.Files.LocalFilePreview>.Instance);
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        native.Calls.Should().Be(1);
        await f.RunAsync(folder ? "preview folder" : "preview file");
        if (confirmed)
        { await f.ViewModel.ConfirmFilePreviewAsync(folder ? f.ViewModel.FolderReview!.ReviewId : f.ViewModel.FileReview!.ReviewId); }
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("open current response details");
        await f.RunAsync("dismiss current response");
        native.Calls.Should().Be(1);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.ClearFilePreview();
    }

    [Theory]
    [InlineData("missing-host")]
    [InlineData("oversized")]
    [InlineData("invalid-unicode")]
    public async Task CurrentResponseDetailsRejectsMissingIdentityAndInvalidNativeEnvelope(string invalid)
    {
        var f = await CreateReadableResponseAsync();
        var body = invalid switch { "oversized" => new string('x', 300_000), "invalid-unicode" => "\ud800", _ => "Original without ambient identity" };
        void Show() => typeof(MainViewModel).GetMethod("ShowInformation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.ViewModel, ["Original", body, false]);
        if (string.Equals(invalid, "missing-host", StringComparison.Ordinal)) { Show(); }
        else
        {
            using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
            Show();
        }
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        await f.RunAsync("open current response details");
        native.Calls.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.ViewModel.ResponseBody.Should().Be(body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentResponseDismissalRequiresNativeRetirementAndSameHostThroughAcknowledgement(bool changeHost)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel) { RetireOnDismiss = changeHost };
        if (changeHost) { native.AfterNative = () => f.Session.IsUnlocked = false; }
        f.ViewModel.BindCurrentResponsePresentation(native);
        await f.RunAsync("dismiss current response");
        native.Calls.Should().Be(1);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.Audit.Events.Should().NotContain(a => a.ActionId == "response.dismiss" && a.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("voice-prefix")]
    [InlineData("foreign-origin")]
    [InlineData("name-unavailable")]
    public async Task CurrentResponseRequiresOriginalInputAndAvailableCurrentName(string invalid)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        if (string.Equals(invalid, "voice-prefix", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync("open current response details", 1); }
        else if (string.Equals(invalid, "foreign-origin", StringComparison.Ordinal))
        {
            using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
            await f.RunAsync("open current response details");
        }
        else
        {
            f.NamePreferences.Name = "???";
            ((Kora.Application.Configuration.AssistantNameConfigurationService)typeof(MainViewModel)
                .GetField("assistantNameConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.ViewModel)!).Reload();
            await f.RunAsync("open current response details");
        }
        native.Calls.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("was not accepted");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    private static void SetResponseProperty<T>(MainViewModel main, string name, T value) =>
        typeof(MainViewModel).GetProperty(name)!.SetValue(main, value);

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.VoiceCommand, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.TypedCommand, RequestOrigin.ActivatedVoice)]
    public async Task CurrentResponsePrivateRoutingCannotForgeOriginalUserProvenance(SecurityAuditInitiator initiator, RequestOrigin origin)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(origin),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        await InvokeTranscriptForTest(f.ViewModel, "open current response details", initiator);
        native.Calls.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("was not accepted");
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task CurrentResponseNativeMethodsCannotLaunderForeignOrigins(RequestOrigin origin)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(origin),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        await f.ViewModel.DismissCurrentResponseAsync();
        native.Calls.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("not an original native UI request");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentResponseNativeMethodsRequireALiveExistingScopeWhenPresent(bool stopped)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        if (stopped) { activity.Activity!.Stop(); }
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        native.Calls.Should().Be(stopped ? 0 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentResponseNativeQueueLinksOriginalHostContextAndTerminatesTruthfully(bool cancelled)
    {
        var f = await CreateReadableResponseAsync();
        var native = new ResponsePresenter(f.ViewModel) { Acknowledge = !cancelled };
        f.ViewModel.BindCurrentResponsePresentation(native);
        var presentations = new List<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.OperationName, "presentation.update", StringComparison.Ordinal))
                { presentations.Add(activity); }
            },
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        HostRequest? request = null;
        System.Diagnostics.ActivityContext cause = default;
        f.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, "response.details", StringComparison.Ordinal)
                && audit.Outcome == SecurityAuditOutcome.Requested)
            {
                var host = Kora.Core.Diagnostics.HostActivity.RequireCurrent();
                request = host.Request;
                cause = host.Activity!.Context;
            }
        };
        await f.RunAsync("open current response details");
        var activity = presentations.Should().ContainSingle().Which;
        activity.ParentSpanId.Should().Be(default(System.Diagnostics.ActivitySpanId));
        activity.Links.Should().ContainSingle().Which.Context.Should().Be(cause);
        activity.GetTagItem("kora.session.id").Should().Be(request!.SessionId.Value);
        activity.GetTagItem("kora.task.id").Should().Be(request.TaskId.Value);
        activity.Status.Should().Be(cancelled ? System.Diagnostics.ActivityStatusCode.Error : System.Diagnostics.ActivityStatusCode.Ok);
        activity.GetTagItem("kora.outcome").Should().Be(cancelled ? "Cancelled" : "Completed");
    }

    private sealed class ResponsePresenter(MainViewModel main) : ICurrentResponsePresentation
    {
        public bool Alive { get; set; } = true;
        public bool Acknowledge { get; set; } = true;
        public Exception? Failure { get; set; }
        public bool RetireOnDismiss { get; set; } = true;
        public Action? AfterNative { get; set; }
        public Action? BeforeOpen { get; set; }
        public int Calls { get; private set; }
        public AdmittedDetailContent? Content { get; private set; }
        public Func<bool> CaptureLifetime() => () => Alive;
        public bool Dismiss(Func<bool> admitted)
        {
            Calls++;
            admitted().Should().BeTrue();
            if (Failure is { } failure) { throw failure; }
            if (Acknowledge && RetireOnDismiss) { main.RetireCurrentResponseSpeech(); }
            AfterNative?.Invoke();
            return Acknowledge;
        }
        public bool OpenDetails(AdmittedDetailContent content, Func<bool> admitted)
        {
            BeforeOpen?.Invoke();
            Calls++;
            admitted().Should().BeTrue();
            if (Failure is { } failure) { throw failure; }
            Content = content;
            return Acknowledge;
        }
    }
}
