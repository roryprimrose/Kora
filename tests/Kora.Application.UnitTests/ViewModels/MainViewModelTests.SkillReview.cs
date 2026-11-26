using AwesomeAssertions;

using Kora.Application.Skills;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SkillReviewQuestionArrivingInsideQueueRetainsItsCompletePreview()
    {
        var f = await Fixture.CreateInitializedAsync();
        await using var details = new PendingDetailsQuestion(f);
        var navigation = new ReviewNavigation();
        f.ViewModel.BindSkillReviewNavigation(navigation, static () => 1);
        Task? question = null;
        f.Dispatcher.BeforeInvoke = () => question = details.PresentAsync();
        await f.RunAsync("open skill packages");
        await question!;
        navigation.Surfaces.Should().BeEmpty();
        f.ViewModel.HasDetailsQuestion.Should().BeTrue();
        f.ViewModel.DetailsQuestionText.Should().Contain("exact completed answer");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkillReviewOriginalTypedAndActivatedRoutesPreserveOriginAndOnlyNavigate(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var navigation = new ReviewNavigation();
        f.ViewModel.BindSkillReviewNavigation(navigation, static () => 1);
        foreach (var phrase in SkillReviewCommand.FixedPhrases)
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + phrase, 1); }
            else { await f.RunAsync(phrase); }
            f.ViewModel.Transcript.Should().Contain("review opened");
        }
        navigation.Surfaces.Should().Equal(SkillReviewSurface.BundledPackages, SkillReviewSurface.BundledPackages,
            SkillReviewSurface.SharedSources, SkillReviewSurface.SharedSources);
        navigation.Origins.Should().OnlyContain(origin => origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));

        f.Session.LockCalls.Should().Be(0);
        f.CallPreferences.SavedSettings.Should().BeNull();
        if (voice) { f.Voice.StartedPhrases.Should().Contain("Nova open skill packages"); }
        else { f.Voice.StartCalls.Should().Be(0); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkillReviewMissingOrUnavailableNativeDoesNotClaimOpened(bool bound)
    {
        var f = await Fixture.CreateInitializedAsync();
        var navigation = new ReviewNavigation { Available = false };
        if (bound) { f.ViewModel.BindSkillReviewNavigation(navigation, static () => 1); }
        await f.RunAsync("open skill packages");
        f.ViewModel.Transcript.Should().Contain("unavailable").And.NotContain("review opened");
        navigation.Surfaces.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("lock")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("binding")]
    [InlineData("name")]
    public async Task SkillReviewQueuedDispatchRechecksCapturedAuthority(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var navigation = new ReviewNavigation();
        long owner = 1;
        f.ViewModel.BindSkillReviewNavigation(navigation, () => owner);
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (change)
            {
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "owner-revision": owner++; break;
                case "lock": f.Session.IsUnlocked = false; break;
                case "privacy": f.ViewModel.CloseForObservedPrivacyEvent("Synthetic test closure", false); break;
                case "call": f.CallState.SetState(CallState.Unknown); break;
                case "dispose": f.ViewModel.Dispose(); break;
                case "binding": f.ViewModel.BindSkillReviewNavigation(navigation, () => owner); break;
                case "name":
                    f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser)
                        .IsCompletedSuccessfully.Should().BeTrue();
                    break;
            }
        };
        await f.RunAsync("open shared skill sources");
        navigation.Surfaces.Should().BeEmpty();
        f.ViewModel.Transcript.Should().NotContain("review opened");

    }

    [Fact]
    public async Task SkillReviewReservedMalformedAndUnavailablePrefixNeverDispatchOrReachModels()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var navigation = new ReviewNavigation();
        f.ViewModel.BindSkillReviewNavigation(navigation, static () => 1);
        await f.RunAsync("Kora, open skill packages");
        f.ViewModel.Transcript.Should().Contain("original typed input");
        await f.RunAsync("open skill packages arbitrary");
        f.ViewModel.Transcript.Should().Contain("exact review-only command");
        await f.RaiseActivatedTranscriptAsync("open skill packages", 1);
        navigation.Surfaces.Should().BeEmpty();

    }

    [Theory]
    [InlineData("generation")]
    [InlineData("owner-revision")]
    [InlineData("binding")]
    [InlineData("consent")]
    public async Task SkillReviewActivatedInputRetainsPreQueueLifetime(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var navigation = new ReviewNavigation();
        long owner = 1;
        f.ViewModel.BindSkillReviewNavigation(navigation, () => owner);
        await f.RaiseActivatedTranscriptAsync("Kora, open skill packages", 1);
        navigation.Surfaces.Clear();
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var consentChange = Task.CompletedTask;
        f.Dispatcher.BeforeInvoke = () =>
        {
            if (change is "generation") { f.Voice.InvalidateCapture(); }
            if (change is "owner-revision") { owner++; }
            if (change is "binding") { f.ViewModel.BindSkillReviewNavigation(navigation, () => owner); }
            if (change is "consent")
            {
                consentChange = f.ViewModel.SetVoiceConsentAsync(false);
            }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, open shared skill sources", 1);
        await consentChange;
        navigation.Surfaces.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Fact]
    public async Task SkillReviewRejectsHostSystemOriginAndUnprefixedVoiceWithoutChangingAnInspector()
    {
        var f = await Fixture.CreateInitializedAsync();
        var navigation = new ReviewNavigation();
        f.ViewModel.BindSkillReviewNavigation(navigation, static () => 1);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            await f.RunAsync("open skill packages");
        }
        await f.RaiseActivatedTranscriptAsync("open skill packages", 1);
        navigation.Surfaces.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("current-name activated voice");
        f.ViewModel.BindSkillReviewNavigation(null, static () => 1);
        await f.RunAsync("open skill packages");
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(CallState.Unavailable, true)]
    [InlineData(CallState.Clear, true)]
    [InlineData(CallState.Active, true)]
    [InlineData(CallState.Suspected, true)]
    [InlineData(CallState.Unknown, false)]
    public async Task SkillReviewUnknownCallObservationNeverBecomesNavigationAuthority(CallState state, bool eligible)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(state);
        await f.Dispatcher.LastInvocation;
        f.ViewModel.IsSkillReviewCallEligible.Should().Be(eligible);
        var navigation = new ReviewNavigation();
        f.ViewModel.BindSkillReviewNavigation(navigation, static () => 1);
        await f.RunAsync("open skill packages");
        navigation.Surfaces.Count.Should().Be(eligible ? 1 : 0);
    }

    [Fact]
    public async Task SkillReviewUnconfirmedManualCallEvidenceFailsClosed()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ManualCallStore.FailOutcomeAudit = true;
        await f.RunAsync("set call.manual-active to on");
        f.ViewModel.IsSkillReviewCallEligible.Should().BeFalse();
        var navigation = new ReviewNavigation();
        f.ViewModel.BindSkillReviewNavigation(navigation, static () => 1);
        await f.RunAsync("open skill packages");
        navigation.Surfaces.Should().BeEmpty();
    }

    private sealed class ReviewNavigation : ISkillReviewNavigation
    {
        public bool Available { get; init; } = true;
        public List<SkillReviewSurface> Surfaces { get; } = [];
        public List<RequestOrigin> Origins { get; } = [];

        public bool TryOpen(SkillReviewSurface surface, Func<bool> eligible)
        {
            if (!Available || !eligible()) { return false; }
            Surfaces.Add(surface);
            Origins.Add(HostActivity.Current!.Request.Origin);
            return true;
        }
    }
}
