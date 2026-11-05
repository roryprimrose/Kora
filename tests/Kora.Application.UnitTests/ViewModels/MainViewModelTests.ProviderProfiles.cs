using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Providers;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("unset", "No service selected")]
    [InlineData("missing", "Ollama installation is missing")]
    [InlineData("auth", "Account authentication is required")]
    [InlineData("current", "current models advertised")]
    [InlineData("unavailable", "No current models")]
    [InlineData("unconfirmed", "Service status is unconfirmed")]
    public async Task ProviderServiceStatusNamesTheActualRecoveryWithoutSubstitution(string state, string expected)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        if (state is "missing") { f.ProfileCatalogue.Installation = LocalProviderInstallation.Missing; }
        f.ProfileCatalogue.Status = state is "auth" ? ProviderCatalogueStatus.AuthenticationRequired
            : state is "current" ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("get providers.app.profile");
        var selected = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service ==
            (state is "unset" ? ProviderService.Unset : state is "missing" ? ProviderService.Local : ProviderService.Claude));
        if (state is "unconfirmed") { f.AppProviderConfiguration!.HoldUnavailable(); }
        f.ViewModel.SelectedProviderServiceChoice = selected;
        f.ViewModel.ProviderSelectedServiceStatus.Should().Contain(expected);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProviderSuggestionsShowExactValuesAndLateNotificationsDoNotRepopulateDisposedPresentation()
    {
        var missing = new Fixture();
        missing.ViewModel.CanReviewProviderSuggestion.Should().BeFalse();
        missing.ViewModel.ProviderSuggestionStatus.Should().Contain("No current AI service suggestion");
        missing.ViewModel.ProviderSelectedServiceStatus.Should().Contain("No service selected");
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("get providers.app.profile");
        missing.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Local);
        missing.ViewModel.ProviderSelectedServiceStatus.Should().Contain("Service status is unconfirmed");
        f.ViewModel.ProviderSuggestionStatus.Should().Contain("No current AI service suggestion");
        using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        await f.AppProviderConfiguration!.InvokeToolAsync(Kora.Core.Tools.ProviderSettingsTools.Propose,
            """{"service":"Claude"}""", () => true, TestContext.Current.CancellationToken);
        f.ViewModel.ProviderSuggestionStatus.Should().Contain("Unsaved AI service suggestion").And.Contain("No model selected");
        f.AppProviderConfiguration.WithdrawSuggestion(root.Request);
        f.AppProviderConfiguration.Suggested.Should().BeNull();
        await f.AppProviderConfiguration.InvokeToolAsync(Kora.Core.Tools.ProviderSettingsTools.Propose,
            """{"service":"Local","model":"exact-model","reasoning":"managed-effort","context":"managed-window","account":"account","revision":1}""",
            () => true, TestContext.Current.CancellationToken);
        f.ViewModel.ProviderSuggestionStatus.Should().Contain("exact-model").And.Contain("managed-effort");
        await f.ViewModel.ReviewProviderSuggestionCommand.ExecuteAsync();
        f.ViewModel.ProviderFieldPreviewStatus.Should().Contain("exact-model");
        await f.ViewModel.CancelProviderFieldCommand.ExecuteAsync();
        f.Dispatcher.DeferPosts = true;
        await f.AppProviderConfiguration.InvokeToolAsync(Kora.Core.Tools.ProviderSettingsTools.Propose,
            """{"service":"Copilot"}""", () => true, TestContext.Current.CancellationToken);
        f.ViewModel.Dispose();
        var changed = new List<string?>();
        f.ViewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        f.Dispatcher.FlushPosts();
        changed.Should().NotContain(nameof(f.ViewModel.ProviderSuggestionStatus));
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProviderModelBrowsingAndFailedRefreshKeepSavedPreferencesAndExposeRecovery()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("list providers.app.models for Claude");
        f.ViewModel.SelectedProviderServiceChoice!.Service.Should().Be(ProviderService.Claude);
        f.ViewModel.ProviderProfileModels.Should().ContainSingle();
        f.ProfilePreferences.Writes.Should().Be(0);
        f.ProfileCatalogue.BeforeObservation = () => throw new HttpRequestException("Synthetic offline service");
        await f.RunAsync("refresh providers.app.profile for Claude");
        f.ViewModel.ResponseTitle.Should().Be("App provider configuration not confirmed.");
        f.ViewModel.SelectedProviderServiceChoice.Should().BeNull();
        f.ProfilePreferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeAndActivatedProviderSuggestionReviewConfirmAndCancelShareTheAuditedWriter()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        using (Kora.Core.Diagnostics.HostActivity.BeginRoot(Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request))
        {
            await f.AppProviderConfiguration!.InvokeToolAsync(Kora.Core.Tools.ProviderSettingsTools.Propose,
                """{"service":"Claude"}""", () => true, TestContext.Current.CancellationToken);
        }
        f.ViewModel.CanReviewProviderSuggestion.Should().BeTrue();
        f.ProfilePreferences.Writes.Should().Be(0);
        await f.ViewModel.ReviewProviderSuggestionCommand.ExecuteAsync();
        f.ViewModel.ProviderFieldPreviewStatus.Should().Contain("No model tuple");
        f.ViewModel.ProviderFieldConfirmationId.Should().NotBe(Guid.Empty);
        f.ViewModel.CanConfirmProviderField.Should().BeTrue();
        f.ProfilePreferences.Writes.Should().Be(0);
        await f.ViewModel.ConfirmProviderFieldCommand.ExecuteAsync();
        f.ProfilePreferences.Value!.Service.Should().Be(ProviderService.Claude);
        using (Kora.Core.Diagnostics.HostActivity.BeginRoot(Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request))
        {
            await f.AppProviderConfiguration!.InvokeToolAsync(Kora.Core.Tools.ProviderSettingsTools.Propose,
                """{"service":"Copilot"}""", () => true, TestContext.Current.CancellationToken);
        }
        await f.RaiseActivatedTranscriptAsync("Kora, review providers.app.suggestion", 1);
        f.ViewModel.CanConfirmProviderField.Should().BeTrue();
        var confirmation = f.ViewModel.ProviderFieldConfirmationId;
        await f.RunAsync($"cancel providers.app.profile {confirmation:D}");
        f.ProfilePreferences.Value.Service.Should().Be(ProviderService.Claude);
        f.ProfilePreferences.Writes.Should().Be(1);
        await f.ViewModel.DiscardProviderSuggestionCommand.ExecuteAsync();
        f.ViewModel.CanReviewProviderSuggestion.Should().BeFalse();
    }

    [Fact]
    public async Task NativeAndActivatedProviderRefreshTargetsSelectedServiceWithoutChangingSavedSelectionsOrRunningModels()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("set providers.app.default-service to Claude");
        var before = f.ProfilePreferences.Value!.Serialize();
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Copilot);
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        f.ProfileCatalogue.Refreshes.Should().Contain(ProviderService.Copilot);
        f.ViewModel.SelectedProviderServiceChoice!.Service.Should().Be(ProviderService.Copilot);
        await f.RaiseActivatedTranscriptAsync("Kora, refresh providers.app.profile", 1);
        f.ProfileCatalogue.Refreshes.Should().Contain(ProviderService.Claude);
        f.ProfilePreferences.Value.Serialize().Should().Be(before);
        f.ViewModel.ProviderSelectedServiceStatus.Should().Contain("No current models");
        f.ViewModel.ProviderProfileModels.Should().BeEmpty();
        f.ViewModel.CanSaveProviderProfile.Should().BeFalse();
        f.Voice.StartedPhrases.Should().Contain("refresh providers.app.profile");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(LocalProviderInstallation.Installed, ProviderService.Local)]
    [InlineData(LocalProviderInstallation.Unknown, ProviderService.Unset)]
    public async Task NativeTypedAndActivatedAppProviderDefaultsNeverAuthenticateOrInfer(LocalProviderInstallation installation, ProviderService expected)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Installation = installation;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync("Kora, list app provider settings");
        f.ViewModel.ProviderProfileConfigurationStatus.Should().Contain(Kora.Application.Presentation.SettingsText.Service(expected));
        f.ViewModel.ProviderProfileModels.Should().BeEmpty();
        f.ViewModel.CanSaveProviderProfile.Should().BeFalse();
        f.ViewModel.CanSignInProvider.Should().BeFalse();
        await f.RunAsync("set providers.app.default-service to Claude");
        f.ProfilePreferences.Value!.Service.Should().Be(ProviderService.Claude);
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Codex);
        f.ViewModel.CanSaveProviderService.Should().BeTrue();
        await f.ViewModel.SaveProviderServiceCommand.ExecuteAsync();
        f.ProfilePreferences.Value.Service.Should().Be(ProviderService.Codex);
        await f.ViewModel.ProviderSignInCommand.ExecuteAsync();
        f.ViewModel.ResponseBody.Should().Contain("Unavailable: no qualified sign-in adapter");
        await f.ViewModel.ResetProviderServiceCommand.ExecuteAsync();
        f.ProfilePreferences.Value.Service.Should().Be(expected);
        await f.RaiseActivatedTranscriptAsync("Kora, set providers.app.default-service to Copilot", 1);
        f.ProfilePreferences.Value.Service.Should().Be(ProviderService.Copilot);
        f.ProfilePreferences.Pending.Should().BeFalse();
        f.ProviderPreferences.Mode.Should().BeNull();
        f.Voice.StartedPhrases.Should().Contain("set providers.app.default-service to Claude");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeAndExactTypedTupleControlsUseSameCompleteAccountCatalogueWorkflow()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        var model = f.ViewModel.ProviderProfileModels.Single();
        f.ViewModel.SelectedProviderProfileModel = model;
        f.ViewModel.SelectedProviderProfileModel = model;
        f.ViewModel.ProviderReasoningChoices.Should().Equal(model.Reasoning);
        f.ViewModel.ProviderContextChoices.Should().Equal(model.Context);
        f.ViewModel.CanSaveProviderProfile.Should().BeFalse();
        await f.ViewModel.Invoking(item => item.SaveProviderProfileCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.SelectedProviderReasoning = model.Reasoning[0];
        f.ViewModel.CanSaveProviderProfile.Should().BeFalse();
        await f.ViewModel.Invoking(item => item.SaveProviderProfileCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.SelectedProviderContext = model.Context[0];
        f.ViewModel.CanSaveProviderProfile.Should().BeTrue();
        f.ViewModel.SelectedProviderProfileModel = new(model.Id, model.Label, model.Selection, model.Reasoning, model.Context);
        f.ViewModel.SelectedProviderReasoning = model.Reasoning[0];
        f.ViewModel.SelectedProviderContext = model.Context[0];
        f.ViewModel.CanSaveProviderProfile.Should().BeFalse();
        f.ViewModel.SelectedProviderProfileModel = model;
        f.ViewModel.SelectedProviderReasoning = model.Reasoning[0];
        f.ViewModel.SelectedProviderContext = model.Context[0];
        await f.ViewModel.SaveProviderProfileCommand.ExecuteAsync();
        f.ProfilePreferences.Value!.For(ProviderService.Local)!.Model.Should().Be(model.Id);
        await f.RunAsync("set providers.app.profile to exact-model managed-effort managed-window for account at 1");
        f.ViewModel.ResponseBody.Should().Contain("Desired configuration saved only");
        await f.ViewModel.ResetProviderProfileCommand.ExecuteAsync();
        f.ProfilePreferences.Value.For(ProviderService.Local).Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("write")]
    public async Task ExactTupleFailuresNeverSelectSubstitutesOrGenerate(string field)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        await f.ViewModel.InitializeAsync();
        if (field is "write") { f.ProfilePreferences.SaveFailure = new IOException("atomic profile failure"); }
        var model = field is "model" ? "unknown-model" : "exact-model";
        var reasoning = field is "reasoning" ? "unknown-effort" : "managed-effort";
        var context = field is "context" ? "unknown-window" : "managed-window";
        var account = field is "account" ? "other-account" : "account";
        var revision = field is "revision" ? "2" : "1";
        await f.RunAsync($"set providers.app.profile to {model} {reasoning} {context} for {account} at {revision}");
        f.ViewModel.ResponseTitle.Should().Be("App provider configuration not confirmed.");
        f.ProfilePreferences.Value.Should().BeNull();
        f.ViewModel.ProviderProfileConfigurationStatus.Should().Contain("unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingDisposedCancelledAndMalformedAppControlsDoNotWriteOrInfer()
    {
        var missing = new Fixture();
        missing.ViewModel.ProviderServiceChoices.Should().BeEmpty();
        missing.ViewModel.ProviderProfileModels.Should().BeEmpty();
        missing.ViewModel.ProviderReasoningChoices.Should().BeEmpty();
        missing.ViewModel.ProviderContextChoices.Should().BeEmpty();
        missing.ViewModel.ProviderProfileDraftStatus.Should().Contain("No models are currently available");
        missing.ViewModel.SelectedProviderServiceChoice = new(new(ProviderService.Local, []), 1, Guid.NewGuid(),
            new(new(Guid.NewGuid()), new(1), true), Kora.Core.Hosting.RequestOrigin.LocalUi, static () => true);
        missing.ViewModel.ProviderProfileModels.Should().BeEmpty();
        missing.ViewModel.ProviderProfileDraftStatus.Should().Contain("No models are currently available");
        missing.ViewModel.ProviderProfileConfigurationStatus.Should().Contain("unavailable");
        await missing.RunAsync("reset providers.app.default-service");
        missing.Reasoner.Requests.Should().BeEmpty();
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.Invoking(model => model.SaveProviderServiceCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(model => model.SaveProviderProfileCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        await f.RunAsync("set providers.app.default-service to Unknown");
        f.ViewModel.ResponseTitle.Should().Be("App provider scope or exact choice required.");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await f.ViewModel.ExecuteProviderProfileCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.TypedCommand,
            cancellationToken: cancelled.Token);
        f.ViewModel.ProviderProfileConfigurationStatus.Should().Contain("unconfirmed");
        f.ViewModel.Dispose();
        await f.ViewModel.ResetProviderServiceCommand.ExecuteAsync();
        f.ViewModel.CanChangeProviderProfile.Should().BeFalse();
        f.ProfilePreferences.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectActivatedAppServiceWriteRetainsOriginalChannelAndCallRestrictions(bool protectedCall)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        if (protectedCall) { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
        await f.ViewModel.ExecuteProviderProfileCommandAsync(new(AppearanceCommandOperation.Set, ProviderService.Claude),
            SecurityAuditInitiator.VoiceCommand, cancellationToken: TestContext.Current.CancellationToken);
        (f.ProfilePreferences.Value?.Service).Should().Be(protectedCall ? null : ProviderService.Claude);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppProviderResultsPreserveAnAlreadySpeakingResponseAndExplicitFailures(bool fail)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = f.RunAsync("what power action is pending");
        await f.TextToSpeech.SpeakStarted.Task;
        var body = f.ViewModel.ResponseBody;
        if (fail) { f.ProfilePreferences.SaveFailure = new IOException("atomic failure"); }
        f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Claude);
        await f.ViewModel.SaveProviderServiceCommand.ExecuteAsync();
        f.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        f.ViewModel.ResponseBody.Should().Be(body);
        if (fail) { f.ViewModel.Transcript.Should().Contain("not confirmed"); }
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("disposed")]
    [InlineData("presentation")]
    [InlineData("no-subscriber")]
    public async Task AppProfilePresentationRechecksOwnershipAndDisposedWindow(string stage)
    {
        var f = new Fixture(subscribeToWindowActions: stage is not "no-subscriber", enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.ProviderServiceChoices), StringComparison.Ordinal))
            {
                if (stage is "receipt") { f.Session.IsUnlocked = false; }
                if (stage is "disposed") { f.ViewModel.Dispose(); }
            }
            if (stage is "presentation" && string.Equals(args.PropertyName, nameof(f.ViewModel.ResponseBody), StringComparison.Ordinal))
            { f.Session.IsUnlocked = false; }
        };
        await f.RunAsync("get providers.app.profile");
        f.ProfilePreferences.Value.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OversizedCompleteCataloguePresentationFailsExplicitlyRatherThanTruncatingChoices()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.LargeCatalogue = true;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("list app provider settings");
        f.ViewModel.ResponseTitle.Should().Be("App provider configuration not confirmed.");
        f.ViewModel.ResponseBody.Should().Contain("64 KiB");
        f.ProfilePreferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ServiceDraftSwitchReplacesItsExactDependentChoicesWithoutErasingOrSavingOtherServices()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        var model = f.ViewModel.ProviderProfileModels[0];
        f.ViewModel.SelectedProviderProfileModel = model;
        f.ViewModel.SelectedProviderReasoning = model.Reasoning[0];
        f.ViewModel.SelectedProviderContext = model.Context[0];
        f.ViewModel.ProviderProfileDraftStatus.Should().Contain(model.Label).And.Contain(model.Reasoning[0].Label).And.Contain(model.Context[0].Label)
            .And.Contain("Not yet saved").And.NotContain("catalogueRevision");
        f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Claude);
        f.ProfilePreferences.Value.Should().BeNull();
        f.ViewModel.SelectedProviderProfileModel.Should().BeNull();
        f.ViewModel.SelectedProviderReasoning.Should().BeNull();
        f.ViewModel.SelectedProviderContext.Should().BeNull();
        f.ViewModel.CanSaveProviderProfile.Should().BeFalse();
        model = f.ViewModel.ProviderProfileModels[0];
        f.ViewModel.SelectedProviderProfileModel = model;
        f.ViewModel.SelectedProviderReasoning = model.Reasoning[0];
        f.ViewModel.SelectedProviderContext = model.Context[0];
        await f.ViewModel.SaveProviderProfileCommand.ExecuteAsync();
        f.ProfilePreferences.Value!.Service.Should().Be(ProviderService.Claude);
        f.ProfilePreferences.Value.For(ProviderService.Claude).Should().NotBeNull();
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        f.ViewModel.ProviderProfileDraftStatus.Should().Contain("Not yet saved").And.Contain("Claude");
        await f.ViewModel.ResetProviderProfileCommand.ExecuteAsync();
        f.ProfilePreferences.Value.For(ProviderService.Claude).Should().BeNull();
        f.ViewModel.SelectedProviderServiceChoice = null;
        f.ViewModel.CanSaveProviderService.Should().BeFalse();
        f.ViewModel.ProviderProfileDraftStatus.Should().Contain("Service: not selected").And.Contain("model: not selected");
        f.ViewModel.SelectedProviderServiceChoice = new(new(ProviderService.Local, []), 1, Guid.NewGuid(),
            new(new(Guid.NewGuid()), new(1), true), Kora.Core.Hosting.RequestOrigin.LocalUi, static () => true);
        f.ViewModel.CanSaveProviderService.Should().BeFalse();
    }
}
