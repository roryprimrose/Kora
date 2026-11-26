using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static void BindCompletedAnswerRouting(Fixture fixture, Func<HostRequest, Task<string>> answer)
    {
        fixture.ViewModel.BindSessionRouting(async (_, _, _) =>
        {
            var request = HostActivity.RequireCurrent().Request;
            var body = await answer(request);
            var message = SpeechAnswer(request, body);
            var target = new SessionWorkspaceEntry(new(request.SessionId, new(1), true),
                new(request.SessionId, new(1), new("Reviewed destination")));
            return new SessionRoutingReceipt(target,
                new(message, new(request, new(3), message.ExpectedTaskState)));
        });
    }

    [Theory]
    [InlineData("current")]
    [InlineData("missing-lease")]
    [InlineData("unbound")]
    [InlineData("input")]
    [InlineData("binding")]
    [InlineData("short")]
    [InlineData("preference")]
    [InlineData("expired")]
    [InlineData("native")]
    [InlineData("session")]
    [InlineData("retired")]
    [InlineData("replaced")]
    [InlineData("oversize")]
    [InlineData("unicode")]
    public async Task CanonicalModelTerminalUsesOnlyItsCapturedForegroundIntent(string boundary)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Qualified deterministic fake");
        await main.InitializeAsync();
        await settings.Set(DetailPresentationMode.OpenAutomatically);
        var presenter = new AutomaticResponsePresenter
        {
            CaptureAvailable = boundary is not "missing-lease",
            ForeignSession = boundary is "session"
        };
        if (boundary is not "unbound") { main.BindCurrentResponsePresentation(presenter); }
        var answerGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        BindCompletedAnswerRouting(f, _ => answerGate.Task);
        var retired = false;
        Task? replacement = null;
        main.PropertyChanged += (_, args) =>
        {
            if (retired || boundary is not ("retired" or "replaced")
                || !string.Equals(args.PropertyName, nameof(main.IsDetailsOfferPending), StringComparison.Ordinal)
                || !main.IsDetailsOfferPending
                || !main.ResponseTitle.StartsWith("Request destination:", StringComparison.Ordinal)) { return; }
            retired = true;
            if (boundary is "retired") { main.RetireCurrentResponseSpeech(); }
            else { replacement = f.RunAsync("what power action is pending"); }
        };
        var response = f.RunAsync("prepare the complete deployment plan");
        try
        {
            presenter.Intents.Should().BeEmpty("provisional work cannot open Details");
            if (boundary is "input") { main.CommandText = "new input"; }
            if (boundary is "binding") { main.BindCurrentResponsePresentation(new ResponsePresenter(main)); }
            if (boundary is "preference") { await settings.Set(DetailPresentationMode.LinkOnly); }
            if (boundary is "expired") { settings.Time.Now = settings.Time.Now.AddMinutes(2); }
            if (boundary is "native") { presenter.NativeCurrent = false; }
            if (boundary is "retired" or "replaced") { await settings.Set(DetailPresentationMode.Offer); }
        }
        finally
        {
            answerGate.TrySetResult(boundary switch
            {
                "short" => "A short answer.",
                "oversize" => DetailOfferAnswer + new string('x', NativeDetailProfile.MaximumUtf8Bytes),
                "unicode" => DetailOfferAnswer + "\ud800",
                _ => DetailOfferAnswer
            });
            await response;
            if (replacement is not null) { await replacement; }
        }
        presenter.Opened.Should().Be(boundary is "current" ? 1 : 0);

        if (boundary is "current")
        {
            var intent = presenter.Intents.Single();
            intent.Content.Source.Should().Be(DetailOfferAnswer);
            intent.Content.SessionSource!.RequestId.Should().Be(intent.Request.RequestId);
            intent.Content.Reference.Revision.Should().BeGreaterThan(0);
            intent.TryConsume().Should().BeFalse("native duplicate callbacks cannot reopen the consumed intent");
            main.RetireCurrentResponseSpeech();
            intent.IsSourceCurrent().Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticPreferenceFailurePreservesCompletedAnswerAndNeverDisclosesAfterRevocation(bool revoke)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Qualified deterministic fake");
        await main.InitializeAsync();
        await settings.Set(DetailPresentationMode.OpenAutomatically);
        var presenter = new AutomaticResponsePresenter
        {
            PublicationFailure = new InvalidDataException("Unconfirmed Markdown preference"),
            BeforePublish = revoke ? () => main.BindCallOwnershipGate(static () => false) : null
        };
        main.BindCurrentResponsePresentation(presenter);
        BindCompletedAnswerRouting(f, _ => Task.FromResult(DetailOfferAnswer));
        await f.RunAsync("prepare the complete deployment plan");

        presenter.Intents.Should().ContainSingle();
        presenter.Opened.Should().Be(0);

        main.ResponseTitle.Should().StartWith("Request destination:");
        main.ResponseBody.Should().Contain(AnswerPresentation.From(DetailOfferAnswer).Brief);
        if (revoke) { main.Transcript.Should().NotContain("Automatic Markdown Details unavailable"); }
        else { main.Transcript.Should().Contain("Automatic Markdown Details unavailable").And.Contain("saved default was not applied"); }
        f.TextToSpeech.SpeakCalls.Should().Be(0, "session routing does not dispatch fallback speech");
        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    public async Task OriginalUserSeparatelyReviewsAndConfirmsFutureOnlyAutomaticDetails(string route)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        main.BindDetailPresentationNativeLifetime(static () => true);
        Task Run(string text, Func<Task> native) => route switch
        {
            "native" => native(),
            "voice" => f.RaiseActivatedTranscriptAsync("Kora, " + text, 1),
            _ => f.RunAsync(text)
        };
        await Run("always open detailed results", main.AutomaticDetailPresentationCommand.ExecuteAsync);
        main.DetailPresentationReviewText.Should().Contain("OpenAutomatically").And.Contain("without activation");
        settings.Preferences.Writes.Should().Be(0);
        await Run("confirm detail presentation change", main.ConfirmDetailPresentationCommand.ExecuteAsync);
        settings.Preferences.Value.Should().Be(DetailPresentationMode.OpenAutomatically);
        settings.Service.CaptureOfferEpoch().Should().BeNull();
        settings.Service.CaptureAutomaticPreference().Should().NotBeNull();

        f.TextToSpeech.SpeakCalls.Should().Be(0);
        if (route is not "voice") { f.Voice.StartCalls.Should().Be(0); }
    }

    [Fact]
    public async Task ReadOnlyAutomaticAdmissionCapturesOwnerPrivacyAndInputWithoutSkillMutationAuthority()
    {
        var f = await Fixture.CreateInitializedAsync();
        using var main = f.ViewModel;
        main.CaptureAutomaticDetailsAdmission()().Should().BeTrue();
        var input = main.CaptureAutomaticDetailsAdmission();
        main.CommandText = "later independent input";
        input().Should().BeFalse();
        var owner = main.CaptureAutomaticDetailsAdmission();
        main.BindCallOwnershipGate(static () => false);
        owner().Should().BeFalse();
        main.CaptureAutomaticDetailsAdmission()().Should().BeFalse();
    }

    private sealed class AutomaticResponsePresenter : ICurrentResponsePresentation, IAutomaticDetailPresentation
    {
        internal bool CaptureAvailable { get; init; } = true;
        internal bool ForeignSession { get; init; }
        internal bool NativeCurrent { get; set; } = true;
        internal List<AutomaticDetailIntent> Intents { get; } = [];
        internal int Opened { get; private set; }
        internal Exception? PublicationFailure { get; init; }
        internal Action? BeforePublish { get; init; }
        public Func<bool> CaptureLifetime() => () => NativeCurrent;
        public bool Dismiss(Func<bool> admitted) => throw new InvalidOperationException("Automatic opening cannot dismiss.");
        public Task<bool> OpenDetailsAsync(AdmittedDetailContent content, Func<bool> admitted) =>
            throw new InvalidOperationException("Automatic opening cannot use the activating manual route.");
        public AutomaticDetailLease? CaptureAutomaticDetails()
        {
            if (!CaptureAvailable) { return null; }
            var request = HostActivity.RequireCurrent().Request;
            return new(ForeignSession ? new(Guid.NewGuid()) : request.SessionId, new(1), 1, 2, 3,
                () => NativeCurrent, () => NativeCurrent);
        }
        public Task<bool> PublishAutomaticDetailsAsync(AutomaticDetailIntent intent)
        {
            Intents.Add(intent);
            BeforePublish?.Invoke();
            if (PublicationFailure is { } failure) { return Task.FromException<bool>(failure); }
            if (!intent.TryConsume()) { return Task.FromResult(false); }
            Opened++;
            return Task.FromResult(true);
        }
    }
}
