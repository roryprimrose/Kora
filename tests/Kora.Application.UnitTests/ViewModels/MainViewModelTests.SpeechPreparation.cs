using System.Collections.Concurrent;
using AwesomeAssertions;
using Kora.Core.Communication;
using Kora.Core.Platform;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Protected_call_and_host_disposal_retire_the_injected_preparation_workflow()
    {
        var fixture = PreparationFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.SpeechPreparation.WaitForQuiescenceAsync();

        fixture.CallState.SetState(CallState.Active);
        await fixture.ViewModel.CallClosureTask;
        fixture.ViewModel.SpeechPreparationStatus.Should().BeEmpty();
        fixture.ViewModel.Dispose();
        await fixture.SpeechPreparation.WaitForQuiescenceAsync();
        fixture.ViewModel.SpeechPreparationStatus.Should().BeEmpty();
        fixture.Preparation.Voices.Should().Equal("af_heart");
    }

    [Fact]
    public void Preparation_status_is_empty_when_the_optional_workflow_is_unavailable()
    {
        var fixture = new Fixture();
        fixture.ViewModel.SpeechPreparationStatus.Should().BeEmpty();
    }

    [Fact]
    public async Task Startup_and_settings_selection_prepare_only_selected_installed_Kokoro_voice()
    {
        var fixture = PreparationFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.SpeechPreparation.WaitForQuiescenceAsync();
        fixture.Preparation.Voices.Should().Equal("af_heart");
        fixture.ViewModel.SpeechPreparationStatus.Should().Contain("prepared");
        fixture.ViewModel.SelectedVoice = fixture.ViewModel.InstalledSpeechVoices.Single(voice =>
            string.Equals(voice.Id, "af_sky", StringComparison.Ordinal));
        await fixture.SpeechPreparation.WaitForQuiescenceAsync();
        fixture.Preparation.Voices.Should().Equal("af_heart", "af_sky");
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.SpokenVoice.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Locked_or_unowned_startup_does_not_prepare_voice(bool locked)
    {
        var fixture = PreparationFixture();
        if (locked) { fixture.Session.IsUnlocked = false; }
        else { fixture.ViewModel.BindCallOwnershipGate(static () => false); }

        await fixture.ViewModel.InitializeAsync();
        await fixture.SpeechPreparation.WaitForQuiescenceAsync();

        fixture.Preparation.Voices.Should().BeEmpty();
    }

    [Fact]
    public async Task Exit_and_handoff_wait_for_preparation_resource_quiescence()
    {
        var fixture = PreparationFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Preparation.Prepare = (_, _) => { started.SetResult(); return release.Task; };
        await fixture.ViewModel.InitializeAsync();
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
            var exit = fixture.ViewModel.ExitAsync();
            exit.IsCompleted.Should().BeFalse();
            release.SetResult();
            await exit;
        }
        finally { release.TrySetResult(); }
        fixture.ViewModel.SpeechPreparationStatus.Should().BeEmpty();
    }

    [Fact]
    public async Task Privacy_closure_retires_preparation_without_publishing_stale_readiness()
    {
        var fixture = PreparationFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Preparation.Prepare = (_, _) => { started.SetResult(); return release.Task; };
        await fixture.ViewModel.InitializeAsync();
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.CloseForObservedPrivacyEvent("Locked", hidePresentation: true, WindowsSessionState.Locked);
        release.SetResult();
        await fixture.SpeechPreparation.WaitForQuiescenceAsync();
        fixture.ViewModel.SpeechPreparationStatus.Should().BeEmpty();
    }

    private static Fixture PreparationFixture()
    {
        var fixture = new Fixture(enableSpeechPreparation: true);
        fixture.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(true)];
        fixture.TextToSpeech.Voices =
        [
            new("female", "Windows", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
            CreateKokoroVoice("af_sky", "Sky"),
        ];
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        fixture.Preferences.VoiceId = "af_heart";
        return fixture;
    }

    private sealed class PreparationService : ISpeechPreparationService
    {
        public ConcurrentQueue<string> Voices { get; } = new();
        public Func<SpeechVoice, CancellationToken, Task> Prepare { get; set; } = (_, _) => Task.CompletedTask;

        public Task PrepareAsync(SpeechVoice voice, CancellationToken cancellationToken = default)
        {
            Voices.Enqueue(voice.Id);
            return Prepare(voice, cancellationToken);
        }
    }
}
