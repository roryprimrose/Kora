using AwesomeAssertions;

using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("set speech.summary-words to 40")]
    [InlineData("set speech.playback-volume to 30")]
    [InlineData("set responses.default-mode to VisualOnly")]
    public async Task OriginalTypedUndoReviewsConfirmsAndConsumesSameAuthoritativePreference(string setter)
    {
        var f = new Fixture(enablePlaybackVolume: true, enableResponseModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync(setter);
        var count = f.Audit.Events.Count;
        var selection = f.Preferences.LoadSelection();
        var consent = model.HasVoiceConsent;
        var provider = model.SelectedSpeechProvider;
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync("undo the last settings change");
        model.CanConfirmSettingsUndo.Should().BeTrue();
        model.SettingsUndoDescription.Should().Contain("current").And.Contain("restore");
        f.Audit.Events.Should().HaveCount(count);
        await f.RunAsync("confirm settings undo");
        model.SettingsUndoDescription.Should().Contain("undone by a new audited write");
        model.CanReviewSettingsUndo.Should().BeFalse();
        model.CanConfirmSettingsUndo.Should().BeFalse();
        f.Preferences.SummaryLimits.Should().BeNull();
        f.VolumePreferences.Value.Should().BeNull();
        f.OutputPreferences.Mode.Should().BeNull();
        f.Audit.Events.Should().HaveCount(count + 2);
        f.Preferences.LoadSelection().Should().Be(selection);
        model.SelectedSpeechProvider.Should().BeSameAs(provider);
        model.HasVoiceConsent.Should().Be(consent);
        model.IsVoiceEnabled.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("hidden")]
    [InlineData("ownership")]
    [InlineData("privacy")]
    [InlineData("drift")]
    [InlineData("failure")]
    public async Task NativeUndoPreservesReviewLifetimeAndExplicitFailure(string loss)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        model.ThemeMode = ApplicationThemeMode.Dark;
        await model.AppearanceUpdate;
        var live = true;
        Task Native(RegisteredSettingSuggestionOperation operation) => HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => model.ExecuteSettingsUndoAsync(operation, SecurityAuditInitiator.LocalUser,
                TestContext.Current.CancellationToken, () => live));
        await Native(RegisteredSettingSuggestionOperation.Review);
        model.CanConfirmSettingsUndo.Should().BeTrue();
        var writes = f.Audit.Events.Count;
        switch (loss)
        {
            case "cancel": await Native(RegisteredSettingSuggestionOperation.Cancel); break;
            case "hidden": live = false; break;
            case "ownership": model.BindCallOwnershipGate(static () => false); break;
            case "privacy": model.CloseForObservedPrivacyEvent("fixture", true); break;
            case "drift": f.AppearancePreferences.Mode = ApplicationThemeMode.Light; break;
            case "failure": f.AppearancePreferences.SaveException = new IOException("fixture save"); break;
        }
        await Native(RegisteredSettingSuggestionOperation.Confirm);
        model.SettingsUndoDescription.Should().Contain(loss is "failure" ? "unconfirmed" : "not confirmed");
        f.AppearancePreferences.Mode.Should().NotBeNull();
        if (loss is not "failure") { f.Audit.Events.Should().HaveCount(writes); }
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeAppearanceUndoUsesTheExistingSetterAndRestoresUnsavedDefault()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        model.ThemeMode = ApplicationThemeMode.Dark;
        await model.AppearanceUpdate;
        var events = f.Audit.Events.Count;
        await f.RunAsync("undo the last settings change");
        model.SettingsUndoDescription.Should().Contain("native");
        foreach (var operation in new[] { RegisteredSettingSuggestionOperation.Review, RegisteredSettingSuggestionOperation.Confirm })
        {
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () => model.ExecuteSettingsUndoAsync(operation,
                SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken, static () => true));
        }
        f.AppearancePreferences.Mode.Should().BeNull();
        model.ThemeMode.Should().Be(ApplicationThemeMode.System);
        model.SettingsUndoDescription.Should().Contain("undone");
        f.Audit.Events.Should().HaveCount(events + 2);
    }

    [Fact]
    public async Task UndoWithoutAReceiptAndAfterDisposalCannotFallThroughToModel()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("undo the last settings change");
        f.ViewModel.SettingsUndoDescription.Should().Contain("not confirmed");
        await f.RunAsync("cancel settings undo");
        f.ViewModel.SettingsUndoDescription.Should().Contain("cancelled");
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
        await FluentActions.Awaiting(() => HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () =>
            f.ViewModel.ExecuteSettingsUndoAsync(RegisteredSettingSuggestionOperation.Review,
                SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken, static () => true)))
            .Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.CanReviewSettingsUndo.Should().BeFalse();
        f.ViewModel.CanConfirmSettingsUndo.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivatedVoiceUndoUsesFreshSeparateUtterancesWithoutReplay(bool confirm)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync("set speech.summary-words to 40");
        await f.RaiseActivatedTranscriptAsync("Kora, undo the last settings change", 1);
        model.CanConfirmSettingsUndo.Should().BeTrue();
        f.TextToSpeech.ClearSpokenResponse();
        await f.RaiseActivatedTranscriptAsync(confirm ? "Kora, confirm settings undo" : "Kora, cancel settings undo", 1);
        f.Preferences.SummaryLimits.Should().Be(confirm ? null : new SpokenSummaryLimits(3, 40));
        model.CanConfirmSettingsUndo.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unprefixed")]
    [InlineData("call")]
    [InlineData("consent")]
    public async Task VoiceUndoCannotBypassPrefixCallOrConsent(string denial)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync("set speech.summary-words to 40");
        await f.RaiseActivatedTranscriptAsync("Kora, undo the last settings change", 1);
        if (denial is "call") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (denial is "consent") { await model.SetVoiceConsentAsync(false); }
        if (denial is "unprefixed") { await f.RaiseActivatedTranscriptAsync("confirm settings undo", 1); }
        else { await f.RunAsync("confirm settings undo"); }
        f.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task InFlightUndoIsJoinedAndCannotAdmitAnotherNativeOperation()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        model.ThemeMode = ApplicationThemeMode.Dark;
        await model.AppearanceUpdate;
        Task Native(RegisteredSettingSuggestionOperation operation) => HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => model.ExecuteSettingsUndoAsync(operation, SecurityAuditInitiator.LocalUser,
                TestContext.Current.CancellationToken, static () => true));
        await Native(RegisteredSettingSuggestionOperation.Review);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.AppearancePreferences.BeforeThemeSave = () =>
        {
            entered.TrySetResult();
            release.Wait(TestContext.Current.CancellationToken);
        };
        // Reset uses the same authoritative store boundary as Save.
        f.AppearancePreferences.BeforeRegisteredReset = f.AppearancePreferences.BeforeThemeSave;
        var pending = Native(RegisteredSettingSuggestionOperation.Confirm);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            model.CanReviewSettingsUndo.Should().BeFalse();
            model.CanConfirmSettingsUndo.Should().BeFalse();
            await FluentActions.Awaiting(() => Native(RegisteredSettingSuggestionOperation.Review))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        finally { release.Set(); }
        await pending;
        f.AppearancePreferences.Mode.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingApprovalOrLostOwnerCannotDiscloseOrConsumeUndo(bool pendingApproval)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync("set speech.summary-words to 40");
        if (pendingApproval)
        {
            await model.PrepareGrantChangeAsync(new Kora.Core.Commands.GrantChange(
                Kora.Core.Commands.GrantChangeOperation.Add, Kora.Core.Commands.BuiltInAction.LockMachine,
                Kora.Core.Commands.ModelApprovalScope.Session));
        }
        else { model.BindCallOwnershipGate(static () => false); }
        model.CanReviewSettingsUndo.Should().BeFalse();
        var title = model.ResponseTitle;
        var body = model.ResponseBody;
        await f.RunAsync("undo the last settings change");
        f.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        if (pendingApproval)
        {
            model.ResponseTitle.Should().Be(title);
            model.ResponseBody.Should().Be(body);
            model.IsGrantChangePending.Should().BeTrue();
        }
    }

    [Fact]
    public async Task KnownUndoWriteDoesNotPublishLateSuccessAfterOwnerRevocation()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        model.ThemeMode = ApplicationThemeMode.Dark;
        await model.AppearanceUpdate;
        Task Native(RegisteredSettingSuggestionOperation operation) => HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => model.ExecuteSettingsUndoAsync(operation, SecurityAuditInitiator.LocalUser,
                TestContext.Current.CancellationToken, static () => true));
        await Native(RegisteredSettingSuggestionOperation.Review);
        var body = model.ResponseBody;
        f.AppearancePreferences.BeforeRegisteredReset = () => model.BindCallOwnershipGate(static () => false);
        await Native(RegisteredSettingSuggestionOperation.Confirm);
        f.AppearancePreferences.Mode.Should().BeNull();
        model.ResponseBody.Should().Be(body);
        model.CanConfirmSettingsUndo.Should().BeFalse();
    }

    private static IEnumerable<string> WithUndoNotifications(params string[] properties) =>
        new[] { nameof(MainViewModel.SettingsUndoDescription), nameof(MainViewModel.CanReviewSettingsUndo),
            nameof(MainViewModel.CanConfirmSettingsUndo) }.Concat(properties).Concat(
            [nameof(MainViewModel.SettingsUndoDescription), nameof(MainViewModel.CanReviewSettingsUndo),
                nameof(MainViewModel.CanConfirmSettingsUndo)]);
}
