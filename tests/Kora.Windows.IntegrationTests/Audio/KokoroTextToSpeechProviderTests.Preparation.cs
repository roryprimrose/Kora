using AwesomeAssertions;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using KokoroSharp.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed partial class KokoroTextToSpeechProviderTests
{
    [Fact]
    public async Task Preparation_opens_no_output_and_invalidated_queued_speech_never_starts()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var runtime = new RecordingSynthesizer();
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ =>
            {
                started.SetResult();
#pragma warning disable VSTHRD002 // Model an uninterruptible native constructor on its background thread.
                release.Wait(TestContext.Current.CancellationToken);
#pragma warning restore VSTHRD002
                return runtime;
            },
            loadVoice: path => new KokoroVoice { Name = Path.GetFileNameWithoutExtension(path), Features = new float[1, 1, 1] },
            prepareLanguage: _ => { });
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        await using var service = new WindowsTextToSpeechService(provider, NullLogger<WindowsTextToSpeechService>.Instance);
        var voice = new SpeechVoice("af_heart", "Heart", "en-US", SpeechVoiceGender.Female)
        {
            ProviderId = SpeechProviderIds.Kokoro,
        };
        var preparation = service.PrepareAsync(voice, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var invoked = false;
        var queued = service.RunCurrentOutputAsync((_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        try
        {
            service.IsSpeaking.Should().BeFalse();
            service.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
            queued.IsCompleted.Should().BeFalse();
            service.InvalidateOutput();
        }
        finally { release.Set(); }
        await preparation;
        var action = () => queued;
        await action.Should().ThrowAsync<OperationCanceledException>();
        invoked.Should().BeFalse();
        runtime.Voices.Should().BeEmpty();
        service.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task Preparation_loads_selected_model_voice_and_language_without_synthesizing()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy", "voices/af_sky.npy", "voices/bf_emma.npy");
        using var client = CreateClient(model, voices);
        var runtime = new RecordingSynthesizer();
        var modelLoads = 0;
        var loaded = new List<string>();
        var languages = new List<string>();
        var logger = new PhaseLogger();
        using var provider = CreateProvider(client, model, voices, logger: logger,
            createSynthesizer: _ => { modelLoads++; return runtime; },
            loadVoice: path =>
            {
                var id = Path.GetFileNameWithoutExtension(path);
                loaded.Add(id);
                return new KokoroVoice { Name = id, Features = new float[1, 1, 1] };
            },
            prepareLanguage: voice => languages.Add(voice.Name));
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);

        await provider.PrepareAsync("af_heart", TestContext.Current.CancellationToken);
        await provider.PrepareAsync("af_heart", TestContext.Current.CancellationToken);
        await provider.PrepareAsync("af_sky", TestContext.Current.CancellationToken);
        await provider.PrepareAsync("bf_emma", TestContext.Current.CancellationToken);

        modelLoads.Should().Be(1);
        loaded.Should().Equal("af_heart", "af_sky", "bf_emma");
        languages.Should().Equal("af_heart", "bf_emma");
        runtime.Voices.Should().BeEmpty();
        logger.Phases.Should().Equal("voice-loading", "model-loading", "language-preparation",
            "voice-loading", "voice-loading", "language-preparation");
        await provider.SynthesizeAsync("Preview.", "bf_emma", TestContext.Current.CancellationToken);
        modelLoads.Should().Be(1);
        loaded.Should().HaveCount(3);
        runtime.Voices.Should().ContainSingle().Which.Name.Should().Be("bf_emma");
    }

    [Fact]
    public async Task Absent_assets_remain_unavailable_without_downloading_for_preparation()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ => throw new InvalidOperationException("Must not load."));

        var action = () => provider.PrepareAsync("af_heart", TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not installed*");
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public async Task Canceled_model_preparation_is_joined_before_removal_and_does_not_initialize_language()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        var runtime = new RecordingSynthesizer();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ =>
            {
                started.SetResult();
#pragma warning disable VSTHRD002 // Deterministically model an uninterruptible native constructor on its background thread.
                release.Wait(TestContext.Current.CancellationToken);
#pragma warning restore VSTHRD002
                return runtime;
            },
            loadVoice: path => new KokoroVoice { Name = Path.GetFileNameWithoutExtension(path), Features = new float[1, 1, 1] },
            prepareLanguage: _ => throw new InvalidOperationException("Canceled work must not initialize language."));
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var prepare = provider.PrepareAsync("af_heart", cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var remove = provider.RemoveAsync(TestContext.Current.CancellationToken);
        try
        {
            remove.IsCompleted.Should().BeFalse();
            runtime.IsDisposed.Should().BeFalse();
        }
        finally { release.Set(); }

        var action = () => prepare;
        await action.Should().ThrowAsync<OperationCanceledException>();
        await remove;
        runtime.IsDisposed.Should().BeTrue();
        runtime.Voices.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_language_preparation_is_retried_without_reloading_model_or_synthesizing()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        var runtime = new RecordingSynthesizer();
        var attempts = 0;
        var modelLoads = 0;
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ => { modelLoads++; return runtime; },
            loadVoice: path => new KokoroVoice { Name = Path.GetFileNameWithoutExtension(path), Features = new float[1, 1, 1] },
            prepareLanguage: _ =>
            {
                if (++attempts == 1) { throw new InvalidOperationException("Language initialization failed."); }
            });
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        var action = () => provider.PrepareAsync("af_heart", TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidOperationException>();

        await provider.PrepareAsync("af_heart", TestContext.Current.CancellationToken);

        attempts.Should().Be(2);
        modelLoads.Should().Be(1);
        runtime.Voices.Should().BeEmpty();
    }
}
