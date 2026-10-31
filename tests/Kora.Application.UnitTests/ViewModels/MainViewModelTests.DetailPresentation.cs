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
        await f.RunAsync("always open detailed results");
        main.Transcript.Should().Contain("input not accepted").And.Contain("Automatic opening is unavailable");
        await main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
        await main.CancelDetailPresentationCommand.ExecuteAsync();
        main.DetailPresentationReviewText.Should().Be("No detail preference change is awaiting confirmation.");
        settings.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
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
        f.Reasoner.Requests.Should().BeEmpty();
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
        f.Reasoner.Requests.Should().BeEmpty();
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
        f.Reasoner.Requests.Should().BeEmpty();
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
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "native")]
    [InlineData(false, "typed")]
    [InlineData(false, "voice")]
    [InlineData(true, "native")]
    [InlineData(true, "typed")]
    [InlineData(true, "voice")]
    public async Task DetailPreferenceReviewChangesOnlyFutureOffersAndManualSourceRemainsExact(bool linkOnly, string route)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "Synthetic ready provider");
        f.Reasoner.Response = new(DetailOfferAnswer, null);
        await main.InitializeAsync();
        var nativeLifetime = true;
        main.BindDetailPresentationNativeLifetime(() => nativeLifetime);
        var initialSpeech = f.TextToSpeech.SpeakCalls;
        var initialCapture = f.Voice.StartCalls;
        if (route is "native")
        {
            await (linkOnly ? main.LinkOnlyDetailPresentationCommand : main.OfferDetailPresentationCommand).ExecuteAsync();
        }
        else
        {
            var phrase = linkOnly ? "stop asking about details" : "offer detailed results";
            if (route is "voice") { await f.RaiseActivatedTranscriptAsync("Kora, " + phrase, 1); }
            else { await f.RunAsync(phrase); }
        }
        settings.Preferences.Writes.Should().Be(0);
        main.DetailPresentationReviewText.Should().Contain("No content, speech or offers are replayed");
        if (route is "native") { await main.ConfirmDetailPresentationCommand.ExecuteAsync(); }
        else if (route is "voice") { await f.RaiseActivatedTranscriptAsync("Kora, confirm detail presentation change", 1); }
        else { await f.RunAsync("confirm detail presentation change"); }
        settings.Preferences.Value.Should().Be(linkOnly ? DetailPresentationMode.LinkOnly : DetailPresentationMode.Offer);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpeakCalls.Should().Be(initialSpeech);
        f.Voice.StartCalls.Should().Be(initialCapture + (route is "voice" ? 2 : 0),
            "only the two deliberately activated test inputs may start capture");
        await f.RunAsync("prepare a detailed plan");
        await main.ActiveReasoningTask!;
        main.IsDetailsOfferPending.Should().Be(!linkOnly);
        if (linkOnly) { main.ResponseBody.Should().NotContain(AnswerPresentation.Offer); }
        else { main.ResponseBody.Should().Contain(AnswerPresentation.Offer); }
        var presenter = new ResponsePresenter(main);
        main.BindCurrentResponsePresentation(presenter);
        if (route is "native") { await main.OpenCurrentResponseDetailsAsync(); }
        else if (route is "voice") { await f.RaiseActivatedTranscriptAsync("Kora, open current response details", 1); }
        else { await f.RunAsync(linkOnly ? "open current response details" : "show me the detailed results"); }
        presenter.Content!.Source.Should().Be(DetailOfferAnswer);
        f.Reasoner.Requests.Should().ContainSingle();
        settings.Preferences.Writes.Should().Be(1, "accepting or manually opening one result cannot change the preference");
        main.DeclineCurrentDetailsOffer();
        settings.Preferences.Writes.Should().Be(1);
        nativeLifetime = false;
    }

    [Theory]
    [InlineData("pending", false)]
    [InlineData("pending", true)]
    [InlineData("declined", false)]
    [InlineData("declined", true)]
    [InlineData("link-only", false)]
    [InlineData("link-only", true)]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    public async Task GenericDetailsTargetsCurrentOfferOnlyOtherwisePreservesSelectedSessionRoute(string state, bool voice)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        if (state is "link-only") { settings.Preferences.Value = DetailPresentationMode.LinkOnly; }
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "Synthetic ready provider");
        f.Reasoner.Response = new(DetailOfferAnswer, null);
        await main.InitializeAsync();
        await f.RunAsync("prepare a detailed plan");
        await main.ActiveReasoningTask!;
        if (state is "declined") { main.DeclineCurrentDetailsOffer(); }
        if (state is "reset")
        {
            await f.RunAsync("stop asking about details");
            await f.RunAsync("confirm detail presentation change");
            await f.RunAsync("reset responses.detail-presentation");
            await f.RunAsync("confirm detail presentation change");
        }
        var presenter = new ResponsePresenter(main);
        main.BindCurrentResponsePresentation(presenter);
        var navigations = new List<Kora.Application.Hosting.WorkspaceSurfaceNavigation>();
        main.WorkspaceSurfaceRequested += navigation =>
        {
            navigation.IsAdmitted().Should().BeTrue();
            navigations.Add(navigation);
            return Task.CompletedTask;
        };
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, show details", 1); }
        else { await f.RunAsync("show details"); }
        if (state is "pending")
        {
            presenter.Content!.Source.Should().Be(DetailOfferAnswer);
            navigations.Should().BeEmpty();
        }
        else
        {
            presenter.Calls.Should().Be(0, "a retained model source is not a pending foreground offer");
            navigations.Should().ContainSingle().Which.Target.Should().Be(Kora.Core.Commands.WorkspaceSurfaceTarget.Details);
            navigations[0].Origin.Should().Be(voice ? Kora.Core.Hosting.RequestOrigin.ActivatedVoice : Kora.Core.Hosting.RequestOrigin.LocalUi);
        }
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, open current response details", 1); }
        else { await f.RunAsync("open current response details"); }
        presenter.Content!.Source.Should().Be(DetailOfferAnswer, "the exact current-response command remains independent of offers");
        f.Reasoner.Requests.Should().ContainSingle();
        settings.Preferences.Writes.Should().Be(state is "reset" ? 2 : 0);
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
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task SuppressionAndResetNeverReplayCurrentResponseOrChangeWindowPreferences()
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "Synthetic ready provider");
        f.Reasoner.Response = new(DetailOfferAnswer, null);
        await main.InitializeAsync();
        await f.RunAsync("prepare a detailed plan");
        await main.ActiveReasoningTask!;
        main.IsDetailsOfferPending.Should().BeTrue();
        var body = main.ResponseBody;
        var topmost = main.IsResponseWindowTopmost;
        var alwaysShow = main.IsResponseAlwaysVisible;
        var speech = f.TextToSpeech.SpeakCalls;
        var capture = f.Voice.StartCalls;
        await f.RunAsync("stop asking about details");
        await f.RunAsync("confirm detail presentation change");
        main.IsDetailsOfferPending.Should().BeFalse();
        await f.RunAsync("reset responses.detail-presentation");
        settings.Preferences.Value.Should().Be(DetailPresentationMode.LinkOnly);
        await f.RunAsync("confirm detail presentation change");
        main.IsDetailsOfferPending.Should().BeFalse("the old offer is retired even after returning to Offer");
        main.ResponseBody.Should().Be(body, "changing a future-only preference does not rewrite an old retained/published response");
        f.TextToSpeech.SpeakCalls.Should().Be(speech);
        f.Voice.StartCalls.Should().Be(capture);
        main.IsResponseWindowTopmost.Should().Be(topmost);
        main.IsResponseAlwaysVisible.Should().Be(alwaysShow);
        f.Reasoner.Requests.Should().ContainSingle();
        await f.RunAsync("prepare another detailed plan");
        await main.ActiveReasoningTask!;
        main.IsDetailsOfferPending.Should().BeTrue();
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
        f.Reasoner.Requests.Should().BeEmpty();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailPreferenceCommandsCannotAnswerCancelOrReplacePendingQuestionsAndApprovals(bool approval)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "Synthetic ready provider");
        if (approval) { f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await main.InitializeAsync();
        await f.RunAsync("help with this machine");
        await main.ActiveReasoningTask!;
        var title = main.ResponseTitle;
        var body = main.ResponseBody;
        var speech = f.TextToSpeech.SpeakCalls;
        foreach (var phrase in DetailPresentationCommand.FixedPhrases) { await f.RunAsync(phrase); }
        main.IsModelActionApprovalPending.Should().Be(approval);
        main.IsModelQuestionPending.Should().Be(!approval);
        main.ResponseTitle.Should().Be(title);
        main.ResponseBody.Should().Be(body);
        settings.Preferences.Writes.Should().Be(0);
        settings.Store.Tasks.Should().BeEmpty();
        settings.Audit.Events.Should().BeEmpty();
        f.Reasoner.Requests.Should().ContainSingle();
        f.TextToSpeech.SpeakCalls.Should().Be(speech);
        f.Session.LockCalls.Should().Be(0);
    }
}
