using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ResponseWindowCommandsPreserveCompanionsPositionTimeoutAndExistingResponseWithoutSpeechOrReveal()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var position = new ResponseWindowPosition(-200, 70);
        await fixture.ViewModel.SetResponseWindowPositionAsync(position);
        fixture.ViewModel.SetResponseTimeoutSeconds(17).Should().BeTrue();
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.Audit.Events.Clear();

        foreach (var text in new[]
        {
            "keep responses visible", "stop keeping response window on top",
            "get response-window.always-show", "status response window topmost",
        })
        {
            await fixture.RunAsync("Kora, " + text);
        }
        fixture.ViewModel.Transcript.Should().Contain("response-window.topmost = false");
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(new ResponseWindowSettings(true, false, position));
        await fixture.RunAsync("reset response-window.always-show");
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(new ResponseWindowSettings(false, false, position));
        await fixture.RunAsync("reset response window topmost");
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(new ResponseWindowSettings(false, true, position));
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(17);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.WindowActions.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().HaveCount(8).And.OnlyContain(item =>
            item.ActionId == "configuration.response-window" && item.Initiator == SecurityAuditInitiator.TypedCommand);
    }

    [Fact]
    public async Task ResponseWindowInspectionAndNoopArePassiveAndDistinguishMissingDefaults()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var events = fixture.Audit.Events.Count;
        fixture.WindowActions.Clear();
        await fixture.RunAsync("list response window settings");
        fixture.ViewModel.Transcript.Should().Contain("declared defaults; no saved preference")
            .And.Contain("response-window.always-show = false").And.Contain("response-window.topmost = true");
        await fixture.RunAsync("get response-window.topmost");
        fixture.ViewModel.Transcript.Should().Contain("declared defaults; no saved preference");
        await fixture.RunAsync("reset response-window.topmost");
        fixture.ViewModel.Transcript.Should().Contain("unchanged; no write or effect");
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
        fixture.Audit.Events.Should().HaveCount(events);
        fixture.WindowActions.Should().BeEmpty();
        HostActivity.Current.Should().BeNull();
    }

    [Theory]
    [InlineData("get response-window.topmost")]
    [InlineData("set response-window.topmost to false")]
    [InlineData("set response-window.topmost to maybe")]
    public async Task ResponseWindowCommandsNeverAnswerCancelOrReplaceAPendingQuestion(string command)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new LocalModelQuestion("Choose a color?", ["Blue", "Green"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose a color");
        await fixture.ViewModel.ActiveReasoningTask!;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var question = fixture.ViewModel.ModelQuestionChoices.ToArray();
        var audits = fixture.Audit.Events.Count;
        fixture.WindowActions.Clear();
        await fixture.RunAsync(command);
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ModelQuestionChoices.Should().Equal(question);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.Audit.Events.Should().HaveCount(audits);
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
        fixture.WindowActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseWindowVoiceCommandsUseCurrentNameAndTheOriginalAdmittedContext(bool topmost)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var observed = new List<HostRequest>();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, "configuration.response-window", StringComparison.Ordinal))
            {
                observed.Add(HostActivity.RequireCurrent().Request);
            }
        };
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await fixture.RaiseActivatedTranscriptAsync(topmost
            ? "Nova, stop keeping response window on top" : "Nova, keep responses visible", 0.95f);
        observed.Should().HaveCount(2).And.OnlyContain(item => item.Origin == RequestOrigin.ActivatedVoice);
        observed[1].Should().BeSameAs(observed[0]);
        observed[0].Should().NotBeSameAs(request, "a newly activated callback owns its own host request");
        fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.response-window", StringComparison.Ordinal))
            .Should().HaveCount(2).And.OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        fixture.Voice.StartedPhrases.Should().Contain("Nova keep responses visible")
            .And.NotContain("Kora keep responses visible");
        fixture.Reasoner.Requests.Should().BeEmpty();
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task ResponseWindowDispatchedCommandRetainsOriginalVoiceContextAcrossNativeSetterAndAudit()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var observed = new List<HostRequest>();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, "configuration.response-window", StringComparison.Ordinal))
            {
                observed.Add(HostActivity.RequireCurrent().Request);
            }
        };
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await fixture.ViewModel.ExecuteResponseWindowCommandAsync(
            new(AppearanceCommandOperation.Set, ResponseWindowOption.AlwaysShow, true),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        observed.Should().HaveCount(2).And.OnlyContain(item => item == request);
        HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task ResponseWindowCommandsRejectUnavailableNameAndLostOwnerWithoutPersistence()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("Kora, keep responses visible");
        fixture.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        fixture.ViewModel.BindCallOwnershipGate(() => false);
        await fixture.ViewModel.ExecuteResponseWindowCommandAsync(
            new(AppearanceCommandOperation.Get, ResponseWindowOption.Topmost), SecurityAuditInitiator.TypedCommand,
            TestContext.Current.CancellationToken);
        fixture.ViewModel.Transcript.Should().Contain("owning unlocked host");
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseWindowSavedStateMustBeKnownAndMatchTheNativeState(bool malformed)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (malformed) { fixture.AppearancePreferences.LoadException = new InvalidDataException("Invalid window state"); }
        else { fixture.AppearancePreferences.ResponseWindowSettings = new(true, false, new(9, 10)); }
        await fixture.RunAsync("set response-window.topmost to false");
        fixture.ViewModel.Transcript.Should().Contain("not confirmed");
        (await fixture.ViewModel.SetResponseAlwaysVisibleAsync(true)).Should().BeFalse();
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeFalse();
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.response-window");
    }

    [Fact]
    public async Task ResponseWindowCommandsCannotWriteDefaultsAfterFailedInitializationOrCancellation()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.LoadException = new InvalidDataException("Invalid saved preferences");
        await fixture.ViewModel.InitializeAsync();
        fixture.AppearancePreferences.LoadException = null;
        await fixture.RunAsync("keep responses visible");
        fixture.ViewModel.Transcript.Should().Contain("unconfirmed");
        (await fixture.ViewModel.SetResponseAlwaysVisibleAsync(true)).Should().BeFalse();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await fixture.ViewModel.ExecuteResponseWindowCommandAsync(
            new(AppearanceCommandOperation.Set, ResponseWindowOption.Topmost, false),
            SecurityAuditInitiator.TypedCommand, cancelled.Token);
        fixture.ViewModel.Transcript.Should().Contain("not confirmed");
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseWindowSaveFailureOrUncertainAuditNeverReportsSuccess(bool terminalAuditFailure)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.ClearSpokenResponse();
        if (terminalAuditFailure)
        {
            fixture.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, "configuration.response-window", StringComparison.Ordinal)
                    && audit.Outcome == SecurityAuditOutcome.Succeeded)
                {
                    throw new IOException("Terminal receipt failed");
                }
            };
        }
        else { fixture.AppearancePreferences.SaveException = new IOException("Window save failed"); }
        await fixture.RunAsync("keep responses visible");
        fixture.ViewModel.Transcript.Should().Contain("not confirmed").And.NotContain("; saved");
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.WindowActions.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        if (terminalAuditFailure)
        {
            fixture.AppearancePreferences.SavedResponseWindowSettings!.AlwaysShow.Should().BeTrue();
            await fixture.RunAsync("get response-window.always-show");
            fixture.ViewModel.Transcript.Should().Contain("uncertain");
        }
        else { fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseWindowPendingWriteRejectsCompetingCommandAndRetiresOriginalAdmission(bool revokeAfterWrite)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.response-window", StringComparison.Ordinal)
                || audit.Outcome != (revokeAfterWrite ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Requested)) { return; }
            entered.SetResult();
            release.Wait(token);
        };
        var first = fixture.RunAsync("keep responses visible");
        try
        {
            await entered.Task.WaitAsync(token);
            await fixture.ViewModel.ExecuteResponseWindowCommandAsync(
                new(AppearanceCommandOperation.Set, ResponseWindowOption.Topmost, false), SecurityAuditInitiator.TypedCommand, token);
            fixture.ViewModel.Transcript.Should().Contain("unavailable");
            fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 123 };
        }
        finally { release.Set(); }
        await first;
        fixture.ViewModel.Transcript.Should().Contain("not confirmed");
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeFalse();
        fixture.ViewModel.IsResponseWindowTopmost.Should().BeTrue();
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(
            revokeAfterWrite ? new ResponseWindowSettings(true, true, null) : null);
    }

    [Theory]
    [InlineData("get response-window.topmost")]
    [InlineData("reset response-window.always-show")]
    [InlineData("set response-window.topmost to maybe")]
    public async Task ResponseWindowCommandsPreservePendingSecurityReviewAndGrantEditor(string command)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock this computer please");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        SetPresentationProperty(fixture.ViewModel, nameof(Kora.Application.ViewModels.MainViewModel.IsGrantEditorVisible), true);
        var body = fixture.ViewModel.ResponseBody;
        var preparation = typeof(Kora.Application.ViewModels.MainViewModel)
            .GetField("approvalPreparationRevision", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var revision = preparation.GetValue(fixture.ViewModel);
        fixture.WindowActions.Clear();
        await fixture.RunAsync(command);
        preparation.GetValue(fixture.ViewModel).Should().Be(revision);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.IsGrantEditorVisible.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.WindowActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseWindowNaturalOffChangesOnlyAddressedOption(bool alwaysShow)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.SetResponseAlwaysVisibleAsync(true);
        await fixture.ViewModel.SetResponseWindowTopmostAsync(true);
        await fixture.RunAsync(alwaysShow ? "allow responses to hide" : "stop keeping response window on top");
        fixture.ViewModel.IsResponseAlwaysVisible.Should().Be(!alwaysShow);
        fixture.ViewModel.IsResponseWindowTopmost.Should().Be(alwaysShow);
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("owner")]
    [InlineData("call")]
    public async Task ResponseWindowNativeCommandCannotPersistAfterOriginalHostAdmissionRetires(string transition)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.response-window", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            switch (transition)
            {
                case "lock": fixture.Session.IsUnlocked = false; break;
                case "owner": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
                default: fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
            }
        };
        await fixture.RunAsync("keep responses visible");
        await fixture.ViewModel.CallClosureTask;
        fixture.ViewModel.Transcript.Should().Contain("not confirmed");
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeFalse();
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.response-window"
            && item.Outcome == SecurityAuditOutcome.Failed);
    }
}
