using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("activated")]
    public async Task DeadlineAndConcurrencyKeepIndependentPreferencesAndSeparateConfirmation(string route)
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service, enableConversationExecution: true);
        using var model = f.ViewModel;
        model.BindOrdinaryDeadlineNativeLifetime(static () => true);

        if (route is "typed") { await f.RunAsync("review conversations.execution-maximum to 1"); }
        else if (route is "activated")
        { await f.RaiseActivatedTranscriptAsync("Kora, review conversations.execution-maximum to 1", 1); }
        else
        {
            model.SelectedConversationExecutionLimit = model.ConversationExecutionLimits[0];
            await model.ReviewConversationExecutionCommand.ExecuteAsync();
        }
        model.CanConfirmConversationExecution.Should().BeTrue();
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        settings.Preferences.Load().Should().BeNull();

        if (route is "typed") { await f.RunAsync("set work.active-deadline-minutes to 1"); }
        else if (route is "activated")
        { await f.RaiseActivatedTranscriptAsync("Kora, set work.active-deadline-minutes to 1", 1); }
        else
        {
            await model.RefreshOrdinaryDeadlineCommand.ExecuteAsync();
            model.SelectedOrdinaryDeadlineMinutes = 1;
            await model.SaveOrdinaryDeadlineCommand.ExecuteAsync();
        }
        settings.Service.Capture().Minutes.Should().Be(1);
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        model.CanConfirmConversationExecution.Should().BeTrue("saving a deadline does not confirm another setting");

        if (route is "typed") { await f.RunAsync("confirm conversations.execution-maximum"); }
        else if (route is "activated")
        { await f.RaiseActivatedTranscriptAsync("Kora, confirm conversations.execution-maximum", 1); }
        else { await model.ConfirmConversationExecutionCommand.ExecuteAsync(); }
        f.ConversationExecutionPreferences.Value.Should().Be(new ConversationExecutionLimit(1));
        f.ConversationExecutionPreferences.Writes.Should().Be(1);
        settings.Service.Capture().Minutes.Should().Be(1);

        await f.RunAsync("reset work.active-deadline-minutes");
        settings.Preferences.Load().Should().BeNull();
        f.ConversationExecutionPreferences.Value.Should().Be(new ConversationExecutionLimit(1));
        f.ConversationExecutionPreferences.Writes.Should().Be(1);
        model.CanConfirmConversationExecution.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(route is "activated" ? 3 : 0,
            "only the three explicit activated transcripts may start fake recognition");
        f.Audit.Events.Should().Contain(entry => entry.ActionId == "configuration.conversation-execution-maximum"
            && entry.Outcome == SecurityAuditOutcome.Succeeded);
        settings.Audit.Events.Should().Contain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task DeadlineAndLogonStartupControlsKeepIndependentPreferencesAndSeparateStartupConfirmation()
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service, logonStartupConfiguration: startup.Service);
        using var model = f.ViewModel;
        f.VoiceConsent.Consent = false;
        await model.InitializeAsync();
        model.BindOrdinaryDeadlineNativeLifetime(static () => true);
        model.BindLogonStartupNativeLifetime(static () => true);
        await model.RefreshOrdinaryDeadlineCommand.ExecuteAsync();
        model.SelectedOrdinaryDeadlineMinutes = 1;
        await model.SaveOrdinaryDeadlineCommand.ExecuteAsync();
        settings.Service.Capture().Minutes.Should().Be(1);
        startup.Preferences.Writes.Should().Be(0);
        startup.Registration.Writes.Should().Be(0);

        await model.EnableLogonStartupCommand.ExecuteAsync();
        model.LogonStartupReviewText.Should().Contain("No change yet");
        startup.Registration.Writes.Should().Be(0);
        await model.ConfirmLogonStartupCommand.ExecuteAsync();
        startup.Preferences.Value.Should().BeTrue();
        startup.Registration.Writes.Should().Be(1);
        settings.Service.Capture().Minutes.Should().Be(1);

        await f.RunAsync("reset work.active-deadline-minutes");
        settings.Preferences.Load().Should().BeNull();
        startup.Preferences.Value.Should().BeTrue();
        startup.Registration.Writes.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartedMicrophone.Should().BeNull();
        model.HasVoiceConsent.Should().BeFalse();
    }

    [Fact]
    public async Task NativeDeadlineRefreshSaveReadbackResetCompletesOperationWithoutModelOrQueueSetting()
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service);
        using var model = f.ViewModel;
        await model.InitializeAsync();
        model.CanChangeOrdinaryDeadlineNative.Should().BeFalse();
        await model.RefreshOrdinaryDeadlineCommand.ExecuteAsync();
        model.Transcript.Should().Contain("unlocked host");
        model.BindOrdinaryDeadlineNativeLifetime(() => true);
        model.CanChangeOrdinaryDeadlineNative.Should().BeTrue();
        await model.RefreshOrdinaryDeadlineCommand.ExecuteAsync();
        model.SavedOrdinaryDeadlineMinutes.Should().Be(5);
        model.IsOrdinaryDeadlineDraftChanged.Should().BeFalse();
        model.SelectedOrdinaryDeadlineMinutes = 1;
        model.IsOrdinaryDeadlineDraftChanged.Should().BeTrue();
        model.SelectedOrdinaryDeadlineMinutes = 1;
        await model.SaveOrdinaryDeadlineCommand.ExecuteAsync();
        settings.Service.Capture().Should().Be(new OrdinaryTurnDeadline(1));
        model.SavedOrdinaryDeadlineMinutes.Should().Be(1);
        model.IsOrdinaryDeadlineDraftChanged.Should().BeFalse();
        model.OrdinaryDeadlineStatus.Should().Contain("(saved)").And.Contain("Running turns are unchanged");
        await model.ResetOrdinaryDeadlineCommand.ExecuteAsync();
        settings.Preferences.Load().Should().BeNull();
        model.SavedOrdinaryDeadlineMinutes.Should().Be(5);
        f.Reasoner.Requests.Should().BeEmpty();
        model.BindOrdinaryDeadlineNativeLifetime(() => false);
        model.SavedOrdinaryDeadlineMinutes.Should().BeNull();
        model.IsOrdinaryDeadlineDraftChanged.Should().BeFalse();
    }

    [Theory]
    [InlineData("get work.active-deadline-minutes", 5)]
    [InlineData("set work.active-deadline-minutes to 10", 10)]
    [InlineData("reset work.active-deadline-minutes", 5)]
    public async Task TypedDeadlineRoutesUseTheSameConfirmedPreference(string input, int minutes)
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service);
        using var model = f.ViewModel;
        await f.RunAsync(input);
        model.SavedOrdinaryDeadlineMinutes.Should().Be(minutes);
        settings.Service.Capture().Minutes.Should().Be(minutes);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("invalid")]
    [InlineData("unrefreshed")]
    [InlineData("denied")]
    [InlineData("read")]
    [InlineData("hidden")]
    public async Task NativeDeadlineFailuresCannotReportOrActivateSuccess(string fault)
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service);
        using var model = f.ViewModel;
        await model.InitializeAsync();
        var visible = true;
        model.BindOrdinaryDeadlineNativeLifetime(() => visible);
        if (fault is not "unrefreshed") { await model.RefreshOrdinaryDeadlineCommand.ExecuteAsync(); }
        model.SelectedOrdinaryDeadlineMinutes = fault is "invalid" ? 61 : 1;
        if (fault is "stale") { settings.Preferences.Save(new(10)); }
        if (fault is "denied")
        {
            settings.Audit.Before = _ => model.BindOrdinaryDeadlineNativeLifetime(() => false);
        }
        if (fault is "hidden") { settings.Audit.Before = _ => visible = false; }
        if (fault is "read") { settings.Store.Before = (_, _) => throw new IOException("injected read failure"); }
        await model.SaveOrdinaryDeadlineCommand.ExecuteAsync();
        model.OrdinaryDeadlineStatus.Should().Contain("unavailable");
        model.SavedOrdinaryDeadlineMinutes.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidTypedDeadlineShowsClarificationAndActivatedVoiceUsesSameSave()
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service);
        using var model = f.ViewModel;
        await model.InitializeAsync();
        await model.ExecuteOrdinaryDeadlineCommandAsync(new(AppearanceCommandOperation.Clarify, Error: "choose minutes"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        model.ResponseTitle.Should().Be("Clarify future task deadline.");
        await f.RaiseActivatedTranscriptAsync("Kora, set work.active-deadline-minutes to 1", 1);
        settings.Service.Capture().Minutes.Should().Be(1);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("locked")]
    [InlineData("busy")]
    [InlineData("question")]
    [InlineData("prefix")]
    public async Task DeadlineControlsPreserveUnavailablePrivacyReentrancyQuestionAndPrefixBoundaries(string fault)
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: fault is "absent" ? null : settings.Service);
        using var model = f.ViewModel;
        if (fault is "prefix") { f.NamePreferences.Name = "Supported Commands"; }
        if (fault is "question")
        {
            f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
            f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        }
        await model.InitializeAsync();
        model.BindOrdinaryDeadlineNativeLifetime(() => true);
        if (fault is "locked") { f.Session.IsUnlocked = false; }
        if (fault is "busy")
        {
            settings.Control.AfterOperation = () => model.CanChangeOrdinaryDeadlineNative.Should().BeFalse();
        }
        if (fault is "question")
        {
            await f.RunAsync("choose");
            await model.ActiveReasoningTask!;
            model.IsResponseInteractionPending.Should().BeTrue();
        }
        if (fault is "prefix")
        {
            await f.RunAsync("Kora, get work.active-deadline-minutes");
            model.Transcript.Should().Contain("prefix routing is unavailable");
            await f.RunAsync("get work.active-deadline-minutes");
            model.SavedOrdinaryDeadlineMinutes.Should().Be(5);
        }
        else
        {
            await model.RefreshOrdinaryDeadlineCommand.ExecuteAsync();
            if (fault is not "busy") { model.Transcript.Should().Contain("unlocked host"); }
        }
        if (fault is "question") { await model.CancelModelQuestionAsync(); }
    }

    [Fact]
    public async Task MissingSetterValueCannotSaveAnImplicitDefault()
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service);
        using var model = f.ViewModel;
        await model.InitializeAsync();
        await model.ExecuteOrdinaryDeadlineCommandAsync(new(AppearanceCommandOperation.Set),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        model.SavedOrdinaryDeadlineMinutes.Should().BeNull();
        settings.Preferences.Load().Should().BeNull();
    }

    [Fact]
    public async Task PrivacyChangeAfterDiscoveryReadbackCannotPublishAConfirmedValue()
    {
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: settings.Service);
        using var model = f.ViewModel;
        await model.InitializeAsync();
        var reads = 0;
        settings.Store.Before = (operation, name) =>
        {
            if (operation is "read" && name.Contains("minutes", StringComparison.Ordinal) && ++reads == 2)
            { f.Session.IsUnlocked = false; }
        };
        await model.ExecuteOrdinaryDeadlineCommandAsync(new(AppearanceCommandOperation.Get),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        model.SavedOrdinaryDeadlineMinutes.Should().BeNull();
        model.OrdinaryDeadlineStatus.Should().Contain("unavailable");
    }
}
