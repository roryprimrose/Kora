using AwesomeAssertions;
using System.Reflection;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string DetailOfferAnswer = "Brief response:\nA bounded plan.\n\nDetailed response:\n# Exact plan\n\n```text\nretain every byte\n```";

    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    public async Task DetailPresentationAndConversationExecutionKeepIndependentReviewsAndConfirmedWrites(string route)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(enableConversationExecution: true, detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        main.BindDetailPresentationNativeLifetime(static () => true);
        var voiceConsent = main.HasVoiceConsent;
        var epoch = settings.Service.CaptureOfferEpoch();
        main.CanControlDetailPresentationNative.Should().BeTrue();
        main.CanChangeConversationExecution.Should().BeTrue();
        main.SelectedConversationExecutionLimit = main.ConversationExecutionLimits[0];
        Task Run(string phrase, Func<Task> native) => route switch
        {
            "native" => native(),
            "voice" => f.RaiseActivatedTranscriptAsync("Kora, " + phrase, 1),
            _ => f.RunAsync(phrase)
        };
        await Run("stop asking about details", main.LinkOnlyDetailPresentationCommand.ExecuteAsync);
        var detailReview = main.DetailPresentationReviewText;
        await Run("review conversations.execution-maximum to 1", main.ReviewConversationExecutionCommand.ExecuteAsync);
        main.DetailPresentationReviewText.Should().Be(detailReview);
        settings.Preferences.Writes.Should().Be(0);
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        await Run("confirm conversations.execution-maximum", main.ConfirmConversationExecutionCommand.ExecuteAsync);
        f.ConversationExecutionPreferences.Value!.Value.Should().Be(1);
        f.ConversationExecutionPreferences.Writes.Should().Be(1);
        settings.Preferences.Writes.Should().Be(0);
        settings.Service.CaptureOfferEpoch().Should().Be(epoch);
        main.DetailPresentationReviewText.Should().Be(detailReview);
        await Run("confirm detail presentation change", main.ConfirmDetailPresentationCommand.ExecuteAsync);
        settings.Preferences.Value.Should().Be(DetailPresentationMode.LinkOnly);
        settings.Preferences.Writes.Should().Be(1);
        settings.Service.IsOfferCurrent(epoch).Should().BeFalse();
        f.ConversationExecutionPreferences.Writes.Should().Be(1);
        await Run("review conversations.execution-maximum to 2", main.ReviewConversationExecutionCommand.ExecuteAsync);
        await Run("reset responses.detail-presentation", main.ResetDetailPresentationCommand.ExecuteAsync);
        await Run("confirm detail presentation change", main.ConfirmDetailPresentationCommand.ExecuteAsync);
        settings.Preferences.Value.Should().Be(DetailPresentationMode.Offer);
        settings.Preferences.Writes.Should().Be(2);
        settings.Service.IsOfferCurrent(epoch).Should().BeFalse("reset must not revive the earlier epoch");
        main.CanConfirmConversationExecution.Should().BeTrue();
        await Run("cancel conversations.execution-maximum", main.CancelConversationExecutionCommand.ExecuteAsync);
        main.CanConfirmConversationExecution.Should().BeFalse();
        f.ConversationExecutionPreferences.Value!.Value.Should().Be(1);
        f.ConversationExecutionPreferences.Writes.Should().Be(1);

        f.TextToSpeech.SpeakCalls.Should().Be(0);
        main.HasVoiceConsent.Should().Be(voiceConsent);
    }

    [Fact]
    public async Task DetailPresentationUncomposedAndCorruptControlsRemainExplicitlyUnavailable()
    {
        var uncomposed = new Fixture();
        using var main = uncomposed.ViewModel;
        main.DetailPresentationStatus.Should().Be("Detail preference controls are not composed.");
        main.CanControlDetailPresentation.Should().BeFalse();
        main.CanControlDetailPresentationNative.Should().BeFalse();
        await main.InspectDetailPresentationCommand.ExecuteAsync();
        main.Transcript.Should().Contain("unavailable");
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        settings.Preferences.LoadFailure = new InvalidDataException("Synthetic corrupt detail preference");
        var corrupt = new Fixture(detailPresentationConfiguration: settings.Service);
        using var corruptMain = corrupt.ViewModel;
        corruptMain.DetailPresentationStatus.Should().Contain("unavailable");
        settings.Service.CaptureOfferEpoch().Should().BeNull();
        settings.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task DetailPresentationInspectCancelAndInvalidInputNeverWriteOrInvokeAProvider()
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        await main.InspectDetailPresentationCommand.ExecuteAsync();
        main.Transcript.Should().Contain("unavailable", "the native Settings lifetime is not visible");
        main.BindDetailPresentationNativeLifetime(static () => true);
        await main.InspectDetailPresentationCommand.ExecuteAsync();
        main.Transcript.Should().Contain("Offer (unsaved default)");
        await f.RunAsync("always open detailed results please");
        main.Transcript.Should().Contain("input not accepted").And.Contain("OpenAutomatically");
        await main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
        await main.CancelDetailPresentationCommand.ExecuteAsync();
        main.DetailPresentationReviewText.Should().Be("No detail preference change is awaiting confirmation.");
        settings.Preferences.Writes.Should().Be(0);

        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailPresentationRequiresAvailableCurrentNameAndOriginalVoicePrefix(bool corruptName)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        if (corruptName) { f.NamePreferences.Name = "\0"; }
        await main.InitializeAsync();
        if (corruptName) { await f.RunAsync("stop asking about details"); }
        else { await f.RaiseActivatedTranscriptAsync("stop asking about details", 1); }
        main.Transcript.Should().Contain("current assistant-name voice prefix");
        settings.Preferences.Writes.Should().Be(0);
        main.DetailPresentationReviewText.Should().Be("No detail preference change is awaiting confirmation.");

    }

    [Fact]
    public async Task DetailPresentationDenialDuringRequestedAuditCannotSaveOrReportConfirmation()
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        await f.RunAsync("stop asking about details");
        settings.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested) { f.CallState.SetState(Kora.Core.Communication.CallState.Active); }
        };
        await f.RunAsync("confirm detail presentation change");
        main.Transcript.Should().Contain("not confirmed");
        settings.Preferences.Writes.Should().Be(0);
        settings.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);

    }

    [Fact]
    public async Task DetailPresentationObservationCannotDiscloseAfterPublicationRevokesHost()
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        settings.Preferences.Value = DetailPresentationMode.LinkOnly;
        settings.Service.Changed += (_, _) => main.BindCallOwnershipGate(static () => false);
        await f.RunAsync("get responses.detail-presentation");
        main.Transcript.Should().Contain("not confirmed").And.Contain("host admission changed before presentation");
        settings.Preferences.Writes.Should().Be(0);

    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetailPresentationPrivateDispatcherRejectsNonUserOrIncompleteReview(bool nonUser)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        // Malformed values and non-user initiators cannot be produced by the closed public grammar.
        var execute = typeof(MainViewModel).GetMethod("ExecuteDetailPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)execute.Invoke(main, [new DetailPresentationCommand(DetailPresentationOperation.Review),
            nonUser ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.TypedCommand, null])!);
        main.Transcript.Should().Contain(nonUser ? "unavailable" : "Choose Offer or LinkOnly");
        settings.Preferences.Writes.Should().Be(0);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailPreferenceEligibilityNotifiesBothNativeAndSharedControlsWhenDecisionGatesChange(bool retained)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        main.BindDetailPresentationNativeLifetime(static () => true);
        main.CanControlDetailPresentationNative.Should().BeTrue();
        var eligibility = true;
        if (retained) { main.BindRetainedAnswerReadDecisionGate(() => eligibility); }
        else { main.BindResponseReadDecisionGate(() => eligibility); }
        var observed = new List<(string? Property, bool Available)>();
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(main.CanControlDetailPresentation) or nameof(main.CanControlDetailPresentationNative))
            { observed.Add((args.PropertyName, main.CanControlDetailPresentationNative)); }
        };
        eligibility = false;
        main.NotifyResponseReadDecisionAvailability();
        observed.Should().Contain((nameof(main.CanControlDetailPresentation), false))
            .And.Contain((nameof(main.CanControlDetailPresentationNative), false));
        observed.Clear();
        eligibility = true;
        main.NotifyResponseReadDecisionAvailability();
        observed.Should().Contain((nameof(main.CanControlDetailPresentation), true))
            .And.Contain((nameof(main.CanControlDetailPresentationNative), true));
        settings.Preferences.Writes.Should().Be(0);

        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("hide")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("write")]
    public async Task DetailPreferenceCannotOutliveOriginalReviewAdmission(string boundary)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        main.BindDetailPresentationNativeLifetime(static () => true);
        await main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
        if (boundary is "cancel") { await main.CancelDetailPresentationCommand.ExecuteAsync(); }
        if (boundary is "hide") { main.BindDetailPresentationNativeLifetime(static () => false); main.BindDetailPresentationNativeLifetime(static () => true); }
        if (boundary is "privacy") { main.BindCallOwnershipGate(static () => false); }
        if (boundary is "call") { f.CallState.SetState(Kora.Core.Communication.CallState.Active); }
        if (boundary is "write") { settings.Preferences.SaveFailure = new IOException(); }
        await main.ConfirmDetailPresentationCommand.ExecuteAsync();
        settings.Preferences.Writes.Should().Be(0);

        f.Voice.StartCalls.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Fact]
    public async Task DisposedConsumerIgnoresQueuedPreferenceNotifications()
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        await f.ViewModel.InitializeAsync();
        f.Dispatcher.DeferPosts = true;
        settings.Service.HoldUnavailable();
        f.ViewModel.Dispose();
        var changes = 0;
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName?.StartsWith("DetailPresentation", StringComparison.Ordinal) == true) { changes++; }
        };
        f.Dispatcher.FlushPosts();
        changes.Should().Be(0);
        settings.Preferences.Writes.Should().Be(0);
    }


}
