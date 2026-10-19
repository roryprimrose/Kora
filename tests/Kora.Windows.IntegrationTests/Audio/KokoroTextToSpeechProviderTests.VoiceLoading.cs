using System.Globalization;
using System.IO.Compression;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Windows.Audio;

using KokoroSharp.Core;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed partial class KokoroTextToSpeechProviderTests
{
    [Fact]
    public async Task Settings_catalogue_keeps_all_choices_while_previews_load_only_the_requested_voice()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy", "voices/af_sky.npy", "voices/ff_siwis.npy");
        using var client = CreateClient(model, voices);
        var runtime = new RecordingSynthesizer();
        var loaded = new List<string>();
        var modelLoads = 0;
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ => { modelLoads++; return runtime; },
            loadVoice: path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                loaded.Add(name);
                return new KokoroVoice { Name = name, Features = new float[1, 1, 1] };
            });
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            provider.GetVoices().Select(voice => voice.Id).Should().BeEquivalentTo("af_heart", "af_sky");
            loaded.Should().BeEmpty();
            modelLoads.Should().Be(0);

            foreach (var id in new[] { "af_heart", "af_heart", "af_sky", "af_heart" })
            {
                await provider.SynthesizeAsync("Preview.", id, TestContext.Current.CancellationToken);
                provider.GetVoices().Should().HaveCount(2);
            }

            loaded.Should().Equal("af_heart", "af_sky", "af_heart");
            runtime.Voices.Select(voice => voice.Name).Should().Equal("af_heart", "af_heart", "af_sky", "af_heart");
            runtime.Voices[0].Should().BeSameAs(runtime.Voices[1]);
            runtime.Voices[3].Should().NotBeSameAs(runtime.Voices[0]);
            modelLoads.Should().Be(1);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Fact]
    public async Task Real_voice_loader_reads_selected_embedding_without_opening_an_invalid_unselected_voice()
    {
        byte[] model = [1];
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
#pragma warning disable VSTHRD103 // Building an in-memory NPY fixture, without disk I/O.
            using (var entry = archive.CreateEntry("voices/af_heart.npy").Open())
            using (var writer = new BinaryWriter(entry, Encoding.ASCII, leaveOpen: false))
            {
                var header = "{'descr': '<f4', 'fortran_order': False, 'shape': (1, 1, 1), }";
                header = header.PadRight(117) + "\n";
                writer.Write(new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0 });
                writer.Write((ushort)header.Length);
                writer.Write(Encoding.ASCII.GetBytes(header));
                writer.Write(0.25f);
            }
            using var invalid = archive.CreateEntry("voices/af_sky.npy").Open();
#pragma warning restore VSTHRD103
            invalid.Write([1, 2, 3]);
        }
        var voices = stream.ToArray();
        using var client = CreateClient(model, voices);
        var runtime = new RecordingSynthesizer();
        using var provider = CreateProvider(client, model, voices, createSynthesizer: _ => runtime);
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);

        var audio = await provider.SynthesizeAsync("Preview.", "af_heart", TestContext.Current.CancellationToken);

        audio.Samples.Should().Equal(1, 2);
        runtime.Voices.Should().ContainSingle().Which.Features[0, 0, 0].Should().Be(0.25f);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("..\\af_heart")]
    [InlineData("AF_HEART")]
    public async Task Unknown_voice_is_rejected_before_loading_any_runtime_or_embedding(string voiceId)
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ => throw new InvalidOperationException("Model must not load."),
            loadVoice: _ => throw new InvalidOperationException("Embedding must not load."));
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);

        var action = () => provider.SynthesizeAsync("Preview.", voiceId, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Voice_loading_failure_does_not_synthesize_with_the_previous_voice()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy", "voices/af_sky.npy");
        using var client = CreateClient(model, voices);
        var runtime = new RecordingSynthesizer();
        var loads = new List<string>();
        var logger = new PhaseLogger();
        using var provider = CreateProvider(client, model, voices, logger: logger,
            createSynthesizer: _ => runtime,
            loadVoice: path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                loads.Add(name);
                if (string.Equals(name, "af_sky", StringComparison.Ordinal)) { throw new InvalidDataException("Invalid embedding."); }
                return new KokoroVoice { Name = name, Features = new float[1, 1, 1] };
            });
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        await provider.SynthesizeAsync("Preview.", "af_heart", TestContext.Current.CancellationToken);

        var action = () => provider.SynthesizeAsync("Preview.", "af_sky", TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidDataException>();
        runtime.Voices.Should().ContainSingle();
        await provider.SynthesizeAsync("Preview.", "af_heart", TestContext.Current.CancellationToken);
        loads.Should().Equal("af_heart", "af_sky", "af_heart");
        logger.Phases.Should().Equal("voice-loading", "model-loading", "synthesis", "voice-loading", "voice-loading", "synthesis");
        logger.Outcomes.Should().Equal(true, true, true, false, true, true);
    }

    [Fact]
    public async Task Cancellation_before_synthesis_loads_nothing()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ => throw new InvalidOperationException("Model must not load."),
            loadVoice: _ => throw new InvalidOperationException("Embedding must not load."));
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => provider.SynthesizeAsync("Preview.", "af_heart", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Removal_waits_for_canceled_inference_and_reinstallation_reloads_model_and_voice()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new RecordingSynthesizer
        {
            Synthesize = () => { started.SetResult(); return finished.Task; },
        };
        var modelLoads = 0;
        var voiceLoads = 0;
        using var provider = CreateProvider(client, model, voices,
            createSynthesizer: _ => { modelLoads++; return modelLoads == 1 ? runtime : new RecordingSynthesizer(); },
            loadVoice: path =>
            {
                voiceLoads++;
                return new KokoroVoice { Name = Path.GetFileNameWithoutExtension(path), Features = new float[1, 1, 1] };
            });
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var speech = provider.SynthesizeAsync("Preview.", "af_heart", cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var removal = provider.RemoveAsync(TestContext.Current.CancellationToken);
        removal.IsCompleted.Should().BeFalse();
        runtime.IsDisposed.Should().BeFalse();
        byte[] samples = [1, 2];
        finished.SetResult(samples);

        var action = () => speech;
        await action.Should().ThrowAsync<OperationCanceledException>();
        await removal;
        samples.Should().OnlyContain(value => value == 0);
        runtime.IsDisposed.Should().BeTrue();
        provider.IsInstalled.Should().BeFalse();
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        await provider.SynthesizeAsync("Preview.", "af_heart", TestContext.Current.CancellationToken);
        modelLoads.Should().Be(2);
        voiceLoads.Should().Be(2);
    }

    [Fact]
    public async Task Installation_preparation_loads_model_without_loading_any_voice()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        var runtime = new RecordingSynthesizer();
        var modelLoads = 0;
        using var provider = CreateProvider(client, model, voices, prepareAfterInstall: true,
            createSynthesizer: _ => { modelLoads++; return runtime; },
            loadVoice: _ => throw new InvalidOperationException("No voice was requested."));

        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);

        modelLoads.Should().Be(1);
        runtime.Voices.Should().BeEmpty();
    }

    [Fact]
    public async Task Timing_diagnostics_record_phases_without_speech_text_voice_or_paths()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        var logger = new PhaseLogger();
        using var provider = CreateProvider(client, model, voices, logger: logger,
            createSynthesizer: _ => new RecordingSynthesizer(),
            loadVoice: path => new KokoroVoice { Name = Path.GetFileNameWithoutExtension(path), Features = new float[1, 1, 1] });
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);

        await provider.SynthesizeAsync("Private preview text.", "af_heart", TestContext.Current.CancellationToken);
        await provider.SynthesizeAsync("Another private preview.", "af_heart", TestContext.Current.CancellationToken);

        logger.Phases.Should().Equal("voice-loading", "model-loading", "synthesis", "synthesis");
        logger.ElapsedMilliseconds.Should().HaveCount(4).And.OnlyContain(value => value >= 0);
        logger.Outcomes.Should().Equal(true, true, true, true);
        logger.Messages.Should().OnlyContain(message =>
            !message.Contains("Private", StringComparison.Ordinal)
            && !message.Contains("private", StringComparison.Ordinal)
            && !message.Contains("af_heart", StringComparison.Ordinal)
            && !message.Contains(root, StringComparison.Ordinal));
    }

    private sealed class RecordingSynthesizer : IKokoroSynthesizer
    {
        public List<KokoroVoice> Voices { get; } = [];
        public Func<Task<byte[]>> Synthesize { get; init; } = () => Task.FromResult<byte[]>([1, 2]);
        public bool IsDisposed { get; private set; }

        public Task<byte[]> SynthesizeAsync(string text, KokoroVoice voice)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Voices.Add(voice);
            return Synthesize();
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class PhaseLogger : ILogger<KokoroTextToSpeechProvider>
    {
        public List<string> Phases { get; } = [];
        public List<double> ElapsedMilliseconds { get; } = [];
        public List<bool> Outcomes { get; } = [];
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (var property in properties)
                {
                    if (string.Equals(property.Key, "Phase", StringComparison.Ordinal) && property.Value is string phase)
                    {
                        Phases.Add(phase);
                    }
                    if (string.Equals(property.Key, "ElapsedMilliseconds", StringComparison.Ordinal) && property.Value is double elapsed)
                    {
                        ElapsedMilliseconds.Add(elapsed);
                    }
                    if (string.Equals(property.Key, "Succeeded", StringComparison.Ordinal) && property.Value is bool succeeded)
                    {
                        Outcomes.Add(succeeded);
                    }
                }
            }
        }
    }
}
