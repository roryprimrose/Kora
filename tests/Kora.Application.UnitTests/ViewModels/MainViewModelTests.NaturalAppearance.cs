using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalAppearanceTypedAndCurrentNameVoiceShareConfirmedStateAndAudit(bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        async Task Run(string text)
        {
            if (voice) { await fixture.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await fixture.RunAsync(text); }
        }
        await Run("use dark mode");
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
        await Run("use light mode");
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Light);
        await Run("use system theme");
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
        await Run("use reduced motion");
        fixture.ViewModel.IsReducedMotionEnabled.Should().BeTrue();
        fixture.AppearancePreferences.ReducedMotionEnabled.Should().BeTrue();
        await Run("follow system motion");
        fixture.ViewModel.IsReducedMotionEnabled.Should().BeFalse();
        fixture.AppearancePreferences.ReducedMotionEnabled.Should().BeFalse();
        fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.reduced-motion", StringComparison.Ordinal))
            .Should().HaveCount(4).And.OnlyContain(item =>
                item.Initiator == (voice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        await Run("set reader text scale to 150 percent");
        await Run("increase reader text scale by 25 percentage points");
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(175);
        await Run("decrease reader text scale by 100 percentage points");
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(75);
        await Run("get reader text scale");
        fixture.ViewModel.ResponseBody.Should().Contain("appearance.reader-text-scale = 75 percent").And.Contain("Saved preference");
        await Run("reset reader text scale");
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(100);
        await Run("reset reader text scale");
        fixture.ViewModel.ResponseTitle.Should().Be("Appearance setting unchanged.");
        fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.reader-text-scale", StringComparison.Ordinal)).Should()
            .OnlyContain(item => item.Initiator == (voice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalAppearanceSupersedesPendingQuestionInsteadOfBecomingModelFeedback(bool voice)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which color?", ["Blue", "Green"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose a color");
        await fixture.ViewModel.ActiveReasoningTask!;
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, use dark mode", 1); }
        else { await fixture.RunAsync("use dark mode"); }
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
        fixture.Reasoner.Requests.Should().ContainSingle();
        await fixture.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("use dark mode")]
    [InlineData("use reduced motion")]
    [InlineData("Kora, use reduced motion")]
    [InlineData("Kora, use dark mode")]
    [InlineData("Novation, set reader text scale to 125 percent")]
    public async Task NaturalAppearanceVoiceWithoutCurrentNameHasNoPreferenceAuthority(string transcript)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        await fixture.RaiseActivatedTranscriptAsync(transcript, 1);
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
        fixture.AppearancePreferences.ReaderTextScalePercent.Should().BeNull();
        fixture.AppearancePreferences.ReducedMotionEnabled.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("increase reader text scale by 101 percentage points")]
    [InlineData("decrease reader text scale by 26 percentage points")]
    [InlineData("increase reader text scale by 2147483647 percentage points")]
    [InlineData("set reader text scale to 201 percent")]
    public async Task NaturalAppearanceClarifiesInvalidResultWithoutClampingOrSaving(string text)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync(text);
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the appearance setting.");
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(100);
        fixture.AppearancePreferences.ReaderTextScalePercent.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task NaturalAppearanceRelativeResolutionWaitsForOwnedSaveAndUsesFreshConfirmedValue()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.ReaderTextScalePercent = 150;
        await fixture.RunAsync("increase reader text scale by 25 percentage points");
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(175);
        await fixture.RunAsync("decrease reader text scale by 100 percentage points");
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(75);
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task NaturalAppearanceRevisionRaceRefusesInsteadOfOverwriting()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.BeforeInvoke = () => fixture.ViewModel.SetThemeMode(ApplicationThemeMode.Dark);
        await fixture.RunAsync("increase reader text scale by 25 percentage points");
        fixture.ViewModel.ResponseTitle.Should().Be("The appearance setting was not saved.");
        fixture.ViewModel.ResponseBody.Should().Contain("Appearance changed");
        fixture.AppearancePreferences.ReaderTextScalePercent.Should().BeNull();
        await fixture.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalAppearanceHostRevocationBeforeCommitRefusesTheProposal(bool dispose)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.BeforeInvoke = () =>
        {
            if (dispose) { fixture.ViewModel.Dispose(); }
            else { fixture.Session.IsUnlocked = false; }
        };
        await fixture.RunAsync("increase reader text scale by 25 percentage points");
        fixture.AppearancePreferences.ReaderTextScalePercent.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("Appearance settings are unavailable.");
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task NaturalAppearanceFailureDoesNotPublishSavedStateAndUnconfirmedEvidenceBlocksRelativeRead()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = new IOException("reader save failed");
        await fixture.RunAsync("increase reader text scale by 25 percentage points");
        fixture.AppearancePreferences.ReaderScalePending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().NotBe("Appearance setting saved.");
        await fixture.ViewModel.ExecuteAppearanceCommandAsync(
            NaturalAppearanceCommand.Parse("increase reader text scale by 25 percentage points", "Kora")!,
            SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ResponseTitle.Should().Be("Appearance settings are unavailable.");
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("not confirmed");
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(100);
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.reader-text-scale"
            && item.Outcome == SecurityAuditOutcome.Failed);
        await fixture.ViewModel.DisposeAsync();
    }
}
