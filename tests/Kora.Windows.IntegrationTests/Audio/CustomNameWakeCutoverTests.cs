using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Kora.Windows.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using static Kora.Windows.Audio.PocketSphinxKeywordDetector;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CustomNameWakeCutoverTests
{
    [Theory]
    [InlineData("Kora", false)]
    [InlineData("Nova", false)]
    [InlineData("Nova", true)]
    public async Task Reviewed_name_and_exact_profile_reach_real_keyword_API_command_PCM_and_existing_command(string name, bool routeRequest)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var assets = new Assets();
            using var host = new NativeUxFixtureSession(assets.Root, syntheticVoice: true);
            var factory = new Factory();
            var stage = new WindowsHandsFreeAudioStage([assets.Profile], factory);
            await using var voice = new HandsFreeVoiceRecognitionService(host.Voice, stage,
                NullLogger<HandsFreeVoiceRecognitionService>.Instance);
            await using var admission = new AudioControlAdmission(host.Interactions, host.Interactions, new(host.Tasks));
            await using var configuration = new SpeechInputConfigurationService(
                new LocalSpeechInputPreferences(host), voice, admission, host.Audit,
                NullLogger<SpeechInputConfigurationService>.Instance);
            await host.InitializeAsync(null, voiceRecognition: voice, speechInputConfiguration: configuration);
            try
            {
            host.Main.HasVoiceConsent.Should().BeFalse();
            voice.IsCaptureQuiescent.Should().BeTrue();
            factory.Created.Should().Be(0, "fixture initialization cannot open the injected capture runtime");
            host.Main.BindVoiceOwnershipGate(static () => true);
            host.Main.BindSpeechInputNativeLifetime(static () => true);
            string? routedText = null;
            RequestOrigin? routedOrigin = null;
            bool? routedAdmission = null;
            host.Main.BindSessionRouting((text, origin, admitted) =>
            {
                routedText = text;
                routedOrigin = origin;
                routedAdmission = admitted();
                return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
            });
            await host.Main.SetAssistantNameAsync(name);
            host.Main.AssistantName.Should().Be(name);
            host.Main.SpeechInputProfiles.Should().Contain(profile => profile.Id == SpeechInputProfileCatalogue.NativeWakeProfileId);
            host.Main.SelectedSpeechInputProfile = SpeechInputProfileCatalogue.NativeWakeProfile;
            host.Main.SelectedSpeechInputResource = SpeechInputResource.Cpu;
            await host.Main.SaveSpeechInputCommand.ExecuteAsync();
            host.Main.HasSpeechInputReview.Should().BeTrue();
            host.Main.SpeechInputReviewStatus.Should().Contain(name).And.Contain("PocketSphinx 5.1.1");
            factory.Created.Should().Be(0, "review/preparation cannot execute native code or open capture");
            await host.Main.ConfirmSpeechInputCommand.ExecuteAsync();
            configuration.Get().Effective!.Selection.Should().Be(SpeechInputProfileCatalogue.NativeWakeSelection);
            voice.ProfileGeneration.Should().Be(configuration.Get().Effective,
                "the fixture setup phase must bind the supplied runtime and configuration to the same exact generation");
            host.Main.IsListening.Should().BeFalse();
            host.Main.IsVoiceEnabled.Should().BeFalse();
            await host.Main.SetVoiceConsentAsync(true);
            if (!host.Main.IsVoiceEnabled) { await host.Main.ToggleListeningCommand.ExecuteAsync(); }
            var first = await factory.NextAsync();
            await first.Microphone.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            host.Voice.StartCalls.Should().Be(0, "wake capture belongs to the supplied hands-free runtime, not the fixture default");
            first.Api.Strings["keyphrase"].Should().Be(name.ToLowerInvariant());
            first.Api.Strings["kws"].Should().BeNull();
            first.Api.Strings["lm"].Should().BeNull();
            first.Api.Strings["rawlogdir"].Should().BeNull();
            first.Api.LogDisabled.Should().BeTrue();
            first.Microphone.Emit(Enumerable.Repeat((short)1, 160).Concat(Enumerable.Repeat((short)2, 160)).ToArray());
            first.Command.Started.Task.IsCompleted.Should().BeFalse();
            first.Api.HasSegment = true;
            first.Microphone.Emit(Enumerable.Repeat((short)3, 320).ToArray());
            await first.Command.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            var commandBytes = new byte[640];
            await first.Stream!.ReadExactlyAsync(commandBytes, TestContext.Current.CancellationToken);
            MemoryMarshal.Cast<byte, short>(commandBytes).ToArray().Should().AllBeEquivalentTo((short)3);
            first.Microphone.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var finalText = routeRequest ? "read a fixture" : "what version are you running";
            first.Command.Final(finalText);
            await first.Microphone.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
            host.Main.ResponseTitle.Should().NotBe(name + " version", "dispatch waits for physical join");
            routedText.Should().BeNull("no command reaches routing before physical join");
            first.Api.Disposed.Should().BeFalse();
            first.Microphone.Release.SetResult();
            var second = await factory.NextAsync();
            await second.Microphone.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (routeRequest)
            {
                routedText.Should().Be(finalText, "the separately admitted wake reaches existing routing without a reconstructed ASR prefix");
                routedOrigin.Should().Be(RequestOrigin.ActivatedVoice);
                routedAdmission.Should().BeTrue();
            }
            else { host.Main.ResponseTitle.Should().Be(name + " version"); }
            first.Command.Disposed.Should().BeTrue();
            first.Api.Disposed.Should().BeTrue();
            var previousGeneration = voice.Generation;
            await host.Main.SetAssistantNameAsync("Unknown Name");
            second.Microphone.Disposed.Should().BeTrue();
            second.Api.Disposed.Should().BeTrue();
            voice.Generation.Should().NotBe(previousGeneration);
            configuration.Get().Effective.Should().BeNull();
            configuration.Get().Desired.Should().Be(SpeechInputProfileCatalogue.NativeWakeSelection);
            host.Main.SpeechInputStatus.Should().Contain("unavailable");
            host.Main.IsVoiceEnabled.Should().BeFalse();
            second.Command.Final("what version are you running");
            factory.Created.Should().Be(2);
            host.Main.ListeningStatus.Should().NotContain("Wake listening");
            voice.IsCaptureQuiescent.Should().BeTrue();
            }
            finally
            {
                host.Main.Dispose();
                await voice.StopAsync(TestContext.Current.CancellationToken);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.SystemIdle);
            }
        });
    }

    [Theory]
    [InlineData("missing-word")]
    [InlineData("not-qualified")]
    [InlineData("name")]
    [InlineData("asset")]
    [InlineData("backend")]
    [InlineData("default-profile")]
    [InlineData("utf8")]
    [InlineData("cancel")]
    public async Task Unsupported_identity_or_lexicon_never_loads_keyword_API_or_opens_microphone(string denial)
    {
        using var assets = new Assets(denial is "missing-word" ? "kora K AO R AH\n" : null,
            qualified: denial is not "not-qualified", invalidUtf8: denial is "utf8");
        var factory = new Factory();
        var stage = new WindowsHandsFreeAudioStage([assets.Profile], factory);
        if (denial is "asset") { File.WriteAllText(assets.Profile.DictionaryPath, "drift"); }
        var selection = denial is "default-profile" ? SpeechInputSelection.Default : denial is "backend"
            ? SpeechInputProfileCatalogue.NativeWakeSelection with { Resource = SpeechInputResource.Gpu }
            : SpeechInputProfileCatalogue.NativeWakeSelection;
        var binding = new HandsFreeAudioBinding(-11, new("fixture", "Fixture"),
            denial is "name" ? "Unsupported" : "Nova", new(1, selection)) { CanCapture = static () => true };
        var open = () => stage.OpenAsync(binding, denial is "cancel" ? new CancellationToken(true) : TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<Exception>();
        factory.Created.Should().Be(0);
    }

    [Theory]
    [InlineData("keyword-readback")]
    [InlineData("clock")]
    [InlineData("start")]
    public async Task Native_configuration_readback_or_start_failure_closes_resources_before_capture(string failure)
    {
        using var assets = new Assets();
        var factory = new Factory { Failure = failure };
        var stage = new WindowsHandsFreeAudioStage([assets.Profile], factory);
        var open = () => stage.OpenAsync(new(-12, new("fixture", "Fixture"), "Nova",
            new(1, SpeechInputProfileCatalogue.NativeWakeSelection)) { CanCapture = static () => true },
            TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidOperationException>();
        factory.Current!.Api.Disposed.Should().BeTrue();
        factory.Current.Microphone.Started.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Exact_profile_preparation_is_an_owned_consumer_joined_before_model_mutation()
    {
        using var assets = new Assets();
        await using var runtime = new HandsFreeSetupRuntime(HandsFreeSetupCatalogue.Component, assets.Profile, compatible: true);
        var factory = new Factory();
        var stage = new WindowsHandsFreeAudioStage(runtime, factory);
        var before = runtime.Observe(HandsFreeSetupCatalogue.Component);
        await using var prepared = await stage.PrepareProfileAsync("Nova", SpeechInputProfileCatalogue.NativeWakeSelection,
            TestContext.Current.CancellationToken);
        var current = runtime.Observe(HandsFreeSetupCatalogue.Component);
        current.ActiveConsumers.Should().Be(1);
        current.ConsumerRevision.Should().BeGreaterThan(before.ConsumerRevision);
        FluentActions.Invoking(() => File.Open(assets.Profile.DictionaryPath, FileMode.Open, FileAccess.Write, FileShare.None).Dispose())
            .Should().Throw<IOException>();
        await using var mutation = (await runtime.TryAcquireExclusiveAsync(HandsFreeSetupCatalogue.Component,
            TestContext.Current.CancellationToken)).RequireLease();
        runtime.Observe(HandsFreeSetupCatalogue.Component).ActiveConsumers.Should().Be(0);
        stage.SupportsProfile("Nova", SpeechInputProfileCatalogue.NativeWakeSelection).Should().BeFalse();
        using (File.Open(assets.Profile.DictionaryPath, FileMode.Open, FileAccess.Write, FileShare.None)) { }
        runtime.HoldUnavailable(HandsFreeSetupCatalogue.Component);
        stage.IsAvailable.Should().BeFalse();
        stage.SupportsProfile("Nova", SpeechInputProfileCatalogue.NativeWakeSelection).Should().BeFalse();
        await FluentActions.Awaiting(() => stage.PrepareProfileAsync("Nova", SpeechInputProfileCatalogue.NativeWakeSelection,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        factory.Created.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Model_mutation_invalidates_exact_name_capture_and_joins_pending_open_and_physical_teardown(bool lateOpen)
    {
        using var assets = new Assets();
        await using var runtime = new HandsFreeSetupRuntime(HandsFreeSetupCatalogue.Component, assets.Profile, compatible: true);
        var factory = new Factory
        {
            MicrophoneOpenRelease = lateOpen ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null,
        };
        var stage = new WindowsHandsFreeAudioStage(runtime, factory);
        var binding = new HandsFreeAudioBinding(-13, new("fixture", "Fixture"), "Nova",
            new(1, SpeechInputProfileCatalogue.NativeWakeSelection)) { CanCapture = static () => true };
        var open = stage.OpenAsync(binding, TestContext.Current.CancellationToken);
        var resources = await factory.NextAsync();
        resources.Microphone.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IHandsFreeAudioSession? session = null;
        try
        {
            if (!lateOpen) { session = await open; }
            var mutationTask = runtime.TryAcquireExclusiveAsync(HandsFreeSetupCatalogue.Component,
                TestContext.Current.CancellationToken).AsTask();
            resources.Binding!.CanCapture().Should().BeFalse();
            mutationTask.IsCompleted.Should().BeFalse();
            stage.IsAvailable.Should().BeFalse();
            stage.SupportsProfile("Nova", SpeechInputProfileCatalogue.NativeWakeSelection).Should().BeFalse();
            runtime.Observe(HandsFreeSetupCatalogue.Component).ActiveConsumers.Should().Be(1);
            factory.MicrophoneOpenRelease?.SetResult();
            await resources.Microphone.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
            mutationTask.IsCompleted.Should().BeFalse();
            resources.Api.Disposed.Should().BeFalse();
            FluentActions.Invoking(() => File.Open(assets.Profile.DictionaryPath, FileMode.Open, FileAccess.Write, FileShare.None).Dispose())
                .Should().Throw<IOException>();
            resources.Microphone.Release.SetResult();
            if (lateOpen) { await FluentActions.Awaiting(() => open).Should().ThrowAsync<OperationCanceledException>(); }
            await using var mutation = (await mutationTask).RequireLease();
            resources.Microphone.Disposed.Should().BeTrue();
            resources.Api.Disposed.Should().BeTrue();
            if (session is not null) { session.IsQuiescent.Should().BeTrue(); }
            else { resources.Microphone.Started.Task.IsCompleted.Should().BeFalse(); }
            runtime.Observe(HandsFreeSetupCatalogue.Component).ActiveConsumers.Should().Be(0);
            using (File.Open(assets.Profile.DictionaryPath, FileMode.Open, FileAccess.Write, FileShare.None)) { }
            runtime.HoldUnavailable(HandsFreeSetupCatalogue.Component);
            stage.SupportsProfile("Nova", SpeechInputProfileCatalogue.NativeWakeSelection).Should().BeFalse();
            await FluentActions.Awaiting(() => stage.OpenAsync(binding, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            factory.Created.Should().Be(1);
        }
        finally
        {
            factory.MicrophoneOpenRelease?.TrySetResult();
            resources.Microphone.Release.TrySetResult();
            if (session is not null) { await session.DisposeAsync(); }
            else
            {
                try { await using var pending = await open; }
                catch (OperationCanceledException) { }
            }
        }
    }

    private sealed class Assets : IDisposable
    {
        internal string Root { get; }
        internal HandsFreeNativeProfile Profile { get; }
        internal Assets(string? dictionary = null, bool qualified = true, bool invalidUtf8 = false)
        {
            var source = new DirectoryInfo(AppContext.BaseDirectory);
            while (source is not null && !File.Exists(Path.Combine(source.FullName, "Kora.slnx"))) { source = source.Parent; }
            var scratch = Path.Combine(source?.FullName ?? throw new InvalidOperationException("Owning source root unavailable."),
                ".scratch", "custom-name-wake-fixtures");
            Directory.CreateDirectory(scratch);
            Root = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
            var model = Path.Combine(Root, "model");
            var native = Path.Combine(Root, "native");
            Directory.CreateDirectory(model);
            Directory.CreateDirectory(native);
            var files = new[]
            {
                (Path.Combine(native, "fixture.dll"), Encoding.ASCII.GetBytes("inert native fixture")),
                (Path.Combine(model, "dictionary.data"), invalidUtf8 ? [0xff] : Encoding.UTF8.GetBytes(dictionary ?? "kora K AO R AH\nnova N OW V AH\nstop S T AA P\n")),
                (Path.Combine(model, "vad.data"), Encoding.ASCII.GetBytes("inert vad fixture")),
                (Path.Combine(model, "mdef.data"), Encoding.ASCII.GetBytes("inert acoustic fixture")),
            };
            foreach (var (path, bytes) in files) { File.WriteAllBytes(path, bytes); }
            Profile = new(files[0].Item1, model, files[1].Item1, files[2].Item1,
                files.Select(file => new HandsFreeNativeAsset(file.Item1, file.Item2.Length, Convert.ToHexString(SHA256.HashData(file.Item2)))),
                qualified ? ["Kora", "Nova", "Stop"] : [], qualified ? "Deterministic test-only exact-name evidence" : null);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class Factory : IHandsFreeNativeFactory
    {
        private readonly Channel<Resources> opened = Channel.CreateUnbounded<Resources>();
        internal int Created { get; private set; }
        internal Resources? Current { get; private set; }
        internal string? Failure { get; init; }
        internal TaskCompletionSource? MicrophoneOpenRelease { get; init; }
        public IHandsFreeKeywordDetector CreateKeyword(HandsFreeNativeProfile profile, PocketSphinxKeywordConfiguration configuration)
        {
            Created++;
            Current = new(new Api { Failure = Failure });
            return new PocketSphinxKeywordDetector(profile, configuration, Current.Api);
        }
        public IHandsFreeCommandEndpoint CreateEndpoint(HandsFreeNativeProfile profile) => new Endpoint();
        public async Task<IHandsFreeMicrophone> OpenMicrophoneAsync(HandsFreeAudioBinding binding, Func<bool> admitted)
        {
            admitted().Should().BeTrue();
            var current = Current ?? throw new InvalidOperationException("Keyword resources missing.");
            current.Binding = binding;
            opened.Writer.TryWrite(current).Should().BeTrue();
            if (MicrophoneOpenRelease is not null) { await MicrophoneOpenRelease.Task; }
            return current.Microphone;
        }
        public Task<IActivatedCapture> OpenCommandAsync(HandsFreeAudioBinding binding, BlockingAudioStream stream, Func<bool> admitted)
        {
            admitted().Should().BeTrue();
            Current!.Stream = stream;
            return Task.FromResult<IActivatedCapture>(Current.Command);
        }
        internal async Task<Resources> NextAsync() => await opened.Reader.ReadAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Resources(Api api)
    {
        internal Api Api { get; } = api;
        internal Microphone Microphone { get; } = new();
        internal Capture Command { get; } = new();
        internal BlockingAudioStream? Stream { get; set; }
        internal HandsFreeAudioBinding? Binding { get; set; }
    }

    private sealed class Api : IPocketSphinxLibrary
    {
        private readonly List<nint> strings = [];
        internal Dictionary<string, string?> Strings { get; } = new(StringComparer.Ordinal);
        internal bool HasSegment { get; set; }
        internal bool LogDisabled { get; private set; }
        internal bool Disposed { get; private set; }
        internal string? Failure { get; init; }
        private nint Text(string value)
        {
            var pointer = Marshal.StringToCoTaskMemUTF8(value);
            strings.Add(pointer);
            return pointer;
        }
        public T Export<T>(string symbol) where T : Delegate
        {
            Delegate export = symbol switch
            {
                "err_set_logfp" => (SetLog)(stream => { stream.Should().Be(nint.Zero); LogDisabled = true; }),
                "ps_config_init" => (CreateConfig)(_ => 1),
                "ps_config_free" or "ps_free" => (Free)(_ => 0),
                "ps_config_set_str" => (SetString)((_, key, value) => { Strings[key] = value; return 1; }),
                "ps_config_set_float" => (SetFloat)((_, key, value) => { key.Should().Be("samprate"); value.Should().Be(16000); return 1; }),
                "ps_config_set_int" => (SetInt)((_, key, value) => { key.Should().Be("frate"); value.Should().Be(100); return 1; }),
                "ps_init" => (UnaryPointer)(_ => 2),
                "ps_get_config" => (UnaryPointer)(_ => 1),
                "ps_config_str" => (GetString)((_, key) => Text(Failure is "keyword-readback" ? "wrong" : Strings[key]!)),
                "ps_config_float" => (GetFloat)((_, _) => Failure is "clock" ? 8000 : 16000),
                "ps_config_int" => (GetInt)((_, _) => 100),
                "ps_start_utt" => (UnaryInt)(_ => Failure is "start" ? -1 : 0),
                "ps_end_utt" => (UnaryInt)(_ => 0),
                "ps_process_raw" => (ProcessRaw)((_, _, _, noSearch, full) => { noSearch.Should().Be(0); full.Should().Be(0); return 0; }),
                "ps_seg_iter" => (UnaryPointer)(_ => HasSegment ? 3 : 0),
                "ps_seg_word" => (UnaryPointer)(_ => Text(Strings["keyphrase"]!)),
                "ps_seg_frames" => (SegmentFrames)((nint _, out int first, out int last) => { first = 1; last = 1; }),
                "ps_seg_free" => (SegmentFree)(_ => { }),
                _ => throw new InvalidOperationException("Unadmitted C ABI export."),
            };
            return export as T ?? throw new InvalidOperationException("C ABI signature mismatch.");
        }
        public void Dispose()
        {
            foreach (var pointer in strings) { Marshal.FreeCoTaskMem(pointer); }
            Disposed = true;
        }
    }

    private sealed class Microphone : IHandsFreeMicrophone
    {
        public event HandsFreeSamplesAvailable? Samples;
        public event EventHandler? Failed { add { } remove { } }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? Release { get; set; }
        internal bool Disposed { get; private set; }
        public void Start() => Started.TrySetResult();
        internal void Emit(short[] samples) => Samples?.Invoke(samples);
        public async ValueTask DisposeAsync()
        {
            Closing.TrySetResult();
            if (Release is { } release) { await release.Task.WaitAsync(TestContext.Current.CancellationToken); }
            Disposed = true;
        }
    }

    private sealed class Endpoint : IHandsFreeCommandEndpoint
    {
        public bool Accept(ReadOnlySpan<short> samples) => false;
        public void Dispose() { }
    }

    private sealed class Capture : IActivatedCapture
    {
        public event ActivatedAudioAvailable? DataAvailable { add { } remove { } }
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler? SpeechDetected { add { } remove { } }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => completed.Task;
        public long? RecorderReleasedTimestamp => null;
        internal bool Disposed { get; private set; }
        public void Start() => Started.TrySetResult();
        public Task ReleaseRecorder() => Task.CompletedTask;
        public void FinishRecognition(bool cancel) { if (cancel) { completed.TrySetResult(); } }
        internal void Final(string text) { TranscriptRecognized?.Invoke(this, new(text, 1)); completed.TrySetResult(); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
