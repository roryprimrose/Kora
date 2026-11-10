using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection("Host tracing")]
public sealed class HandsFreeOfflineEvaluationTests
{
    private static WindowsHandsFreeAudioSession? pendingNativeSession;

    [Theory]
    [InlineData("version")]
    [InlineData("missing-case")]
    [InlineData("duplicate-case")]
    [InlineData("path")]
    [InlineData("digest")]
    [InlineData("odd-pcm")]
    [InlineData("large-pcm")]
    [InlineData("chunk")]
    [InlineData("frames")]
    [InlineData("command-end")]
    [InlineData("negative-annotation")]
    [InlineData("filename")]
    [InlineData("network-root")]
    public void RejectsUnknownOrUnboundedCorpusBeforeNativeLoading(string fault)
    {
        var manifest = SyntheticManifest();
        var first = manifest.Cases[0];
        manifest = fault switch
        {
            "version" => manifest with { Version = 2 },
            "missing-case" => manifest with { Cases = manifest.Cases.RemoveAt(0) },
            "duplicate-case" => manifest with { Cases = manifest.Cases.SetItem(1, first) },
            "path" => manifest with { AssetsRoot = "relative" },
            "digest" => manifest with { Cases = manifest.Cases.SetItem(0, first with { Sha256 = new('z', 64) }) },
            "odd-pcm" => manifest with { Cases = manifest.Cases.SetItem(0, first with { Bytes = 31999 }) },
            "large-pcm" => manifest with { Cases = manifest.Cases.SetItem(0, first with { Bytes = 960002 }) },
            "chunk" => manifest with { Cases = manifest.Cases.SetItem(0, first with { ChunkSamples = 0 }) },
            "frames" => manifest with { Cases = manifest.Cases.SetItem(0, first with { LastFrameMaximum = int.MaxValue }) },
            "command-end" => manifest with { Cases = manifest.Cases.SetItem(0, first with { CommandEndMinimum = 0 }) },
            "negative-annotation" => manifest with { Cases = manifest.Cases.SetItem(3,
                manifest.Cases[3] with { LastFrameMaximum = 1 }) },
            "filename" => manifest with { Cases = manifest.Cases.SetItem(0, first with { File = "..\\outside.pcm" }) },
            "network-root" => manifest with { AssetsRoot = @"\\not-contacted.invalid\share" },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var validate = () => HandsFreeOfflineFixture.Validate(manifest);
        validate.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("assets")]
    public async Task UnavailableSelectedInputsFailAdmissionWithoutNativeExecution(string fault)
    {
        var manifest = SyntheticManifest();
        var path = Path.Combine(Path.GetDirectoryName(manifest.AssetsRoot)!, "manifest.json");
        var bytes = fault switch
        {
            "oversized" => new byte[65537],
            "malformed" => Encoding.UTF8.GetBytes("{}"),
            "unknown-field" => Encoding.UTF8.GetBytes("{\"Unknown\":1}"),
            "duplicate-field" => Encoding.UTF8.GetBytes("{\"Version\":1,\"Version\":1}"),
            _ => JsonSerializer.SerializeToUtf8Bytes(manifest),
        };
        if (fault is not "missing") { await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken); }
        var digest = fault is "identity" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes));
        var admit = () => HandsFreeOfflineFixture.Admit(path, digest, TestContext.Current.CancellationToken);
        if (fault is "missing") { await admit.Should().ThrowAsync<FileNotFoundException>(); }
        else if (fault is "malformed" or "unknown-field" or "duplicate-field") { await admit.Should().ThrowAsync<JsonException>(); }
        else { await admit.Should().ThrowAsync<InvalidDataException>(); }
    }

    [Fact]
    public async Task FileMicrophonePreservesOriginalPcmOffsetsAndStopsAfterDisposal()
    {
        var path = Path.Combine(HandsFreeOfflineFixture.CreateScratchDirectory(), "synthetic.pcm");
        short[] original = [short.MinValue, -1, 0, 1, short.MaxValue];
        await File.WriteAllBytesAsync(path, MemoryMarshal.AsBytes(original.AsSpan()).ToArray(), TestContext.Current.CancellationToken);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var microphone = new HandsFreeOfflineFixture.FileMicrophone(stream, 2);
        var observed = new List<short>();
        microphone.Samples += samples => observed.AddRange(samples.ToArray());
        microphone.Start();
        var positions = new List<long>();
        while (await microphone.Pump(TestContext.Current.CancellationToken)) { positions.Add(microphone.PositionSamples); }
        observed.Should().Equal(original);
        positions.Should().Equal(2, 4, 5);
        await microphone.DisposeAsync();
        var pump = () => microphone.Pump(TestContext.Current.CancellationToken);
        await pump.Should().ThrowAsync<ObjectDisposedException>();
        observed.Should().Equal(original);
    }

    [Fact]
    public async Task CancelledFileReplayEmitsNoPcm()
    {
        var path = Path.Combine(HandsFreeOfflineFixture.CreateScratchDirectory(), "cancelled.pcm");
        await File.WriteAllBytesAsync(path, new byte[640], TestContext.Current.CancellationToken);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var microphone = new HandsFreeOfflineFixture.FileMicrophone(stream, 320);
        var samples = 0;
        microphone.Samples += chunk => samples += chunk.Length;
        microphone.Start();
        var pump = () => microphone.Pump(new CancellationToken(canceled: true));
        await pump.Should().ThrowAsync<OperationCanceledException>();
        microphone.PositionSamples.Should().Be(0);
        samples.Should().Be(0);
    }

    [Fact(Explicit = true)]
    public async Task ActualNativeSilenceSmoke()
    {
        RequirePreviousNativeJoin();
        await using var fixture = await HandsFreeOfflineFixture.AdmitSelected(TestContext.Current.CancellationToken);
        var configuration = await PocketSphinxKeywordConfiguration.CreateAsync(fixture.Profile, "Kora", TestContext.Current.CancellationToken);
        configuration.Keyphrase.Should().Be("kora");
        using (var detector = new PocketSphinxKeywordDetector(fixture.Profile, configuration))
        {
            for (var index = 0; index < 50; index++)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                detector.Accept(new short[320]).Should().BeNull();
            }
        }
        WriteReceipt(fixture, "silence-smoke", 0, null, null, 0, 0, true);
    }

    [Theory(Explicit = true)]
    [InlineData("immediate")]
    [InlineData("paused")]
    [InlineData("no-command")]
    [InlineData("similar")]
    [InlineData("negative")]
    [InlineData("repeated")]
    [InlineData("chunk-split")]
    public async Task ActualNativeFileReplay(string id)
    {
        RequirePreviousNativeJoin();
        var token = TestContext.Current.CancellationToken;
        await using var fixture = await HandsFreeOfflineFixture.AdmitSelected(token);
        var specification = fixture.Specification.Cases.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        var configuration = await PocketSphinxKeywordConfiguration.CreateAsync(fixture.Profile, "Kora", token);
        configuration.Keyphrase.Should().Be("kora");
        KeywordAudioSegment? interval = null;
        long detectorSamples;
        await using (var input = fixture.Microphone(id))
        {
            using var detector = new PocketSphinxKeywordDetector(fixture.Profile, configuration);
            input.Samples += samples => interval ??= detector.Accept(samples);
            input.Start();
            while (await input.Pump(token))
            {
                if (interval is not null) { break; }
            }
            detectorSamples = input.PositionSamples;
        }
        var positive = id is not ("similar" or "negative");
        if (positive)
        {
            interval.Should().NotBeNull();
            interval!.FirstFrame.Should().BeInRange(specification.FirstFrameMinimum, specification.FirstFrameMaximum);
            interval.LastFrame.Should().BeInRange(specification.LastFrameMinimum, specification.LastFrameMaximum);
        }
        else { interval.Should().BeNull(); }

        var microphone = fixture.Microphone(id);
        var nativeDetector = new PocketSphinxKeywordDetector(fixture.Profile, configuration);
        SileroCommandEndpoint endpoint;
        try { endpoint = new(fixture.Profile.VadPath, VoiceCaptureTiming.Default); }
        catch
        {
            nativeDetector.Dispose();
            await microphone.DisposeAsync();
            throw;
        }
        using var probe = new PcmCapture();
        var opens = 0;
        var wakes = 0;
        var admitted = true;
        var binding = new HandsFreeAudioBinding(-101, new("file-only", "File-only offline evaluation"), "Kora",
            new(1, SpeechInputSelection.Default))
        {
            CanCapture = () => admitted,
            WakeActivated = _ => wakes++,
        };
        await using var session = new WindowsHandsFreeAudioSession(binding, microphone, nativeDetector, endpoint, fixture.TransferToSession(), stream =>
        {
            opens++;
            probe.Bind(stream);
            return Task.FromResult<IActivatedCapture>(probe);
        });
        pendingNativeSession = session;
        session.Start();
        var wakeTask = session.WaitForWakeAsync(token);
        while (!wakeTask.IsCompleted && await microphone.Pump(token)) { }
        if (!positive)
        {
            wakeTask.IsCompleted.Should().BeFalse();
            opens.Should().Be(0);
            wakes.Should().Be(0);
            admitted = false;
            await session.DisposeAsync();
            await FluentActions.Awaiting(() => wakeTask).Should().ThrowAsync<OperationCanceledException>();
            session.IsQuiescent.Should().BeTrue();
            WriteReceipt(fixture, id, detectorSamples, null, null, opens, 0, session.IsQuiescent);
            return;
        }
        wakeTask.IsCompleted.Should().BeTrue("the selected positive fixture must produce a real wake before EOF");
        var wake = await wakeTask;
        wake.CommandStartSample.Should().Be(interval!.CommandStartSample);
        var stale = () => session.CaptureCommandAsync(wake with { Generation = wake.Generation - 1 }, token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        opens.Should().Be(0);
        var capture = session.CaptureCommandAsync(wake, token);
        var repeated = () => session.CaptureCommandAsync(wake, token);
        await repeated.Should().ThrowAsync<InvalidOperationException>();
        probe.Drain();
        while (!probe.Ended && await microphone.Pump(token)) { probe.Drain(); }
        probe.Drain();
        probe.Ended.Should().BeTrue("selected fixtures must include enough trailing PCM for the real VAD endpoint");
        (await capture.WaitAsync(token)).Should().BeNull("the PCM probe never fabricates recognition or emits transcripts");
        opens.Should().Be(1);
        wakes.Should().Be(1);
        var commandEnd = wake.CommandStartSample + probe.Bytes / 2;
        commandEnd.Should().BeInRange(specification.CommandEndMinimum, specification.CommandEndMaximum);
        var original = await fixture.ReadPcm(id, token);
        var observed = probe.Pcm;
        try
        {
            observed.AsSpan().SequenceEqual(original.AsSpan(checked((int)wake.CommandStartSample), checked((int)(probe.Bytes / 2))))
                .Should().BeTrue(
                "every command sample starts at the exclusive real keyword end, without pre-name audio or a lost first sample");
        }
        finally { Array.Clear(original); Array.Clear(observed); }
        // Continue through the remaining/repeated utterance; the consumed wake must not reopen capture.
        while (await microphone.Pump(token)) { probe.Drain(); }
        probe.Bytes.Should().Be((commandEnd - wake.CommandStartSample) * 2);
        opens.Should().Be(1);
        wakes.Should().Be(1);
        admitted = false;
        await session.DisposeAsync();
        session.IsQuiescent.Should().BeTrue();
        WriteReceipt(fixture, id, detectorSamples, interval, wake.CommandStartSample, opens, commandEnd, session.IsQuiescent);
    }

    private static void WriteReceipt(HandsFreeOfflineFixture fixture, string id, long detectorSamples,
        KeywordAudioSegment? interval, long? commandStart, int captures, long commandEnd, bool quiescent)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = "kora-offline-evaluation-v1",
            EvaluationOnly = true,
            TestAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(HandsFreeOfflineEvaluationTests).Assembly.Location))),
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            fixture.ManifestDigest,
            Assets = fixture.Profile.Assets.Select(asset => new { File = Path.GetFileName(asset.Path), asset.Length, asset.Sha256 }),
            Fixtures = fixture.Specification.Cases.Select(item => new { item.Id, item.Bytes, item.Sha256 }),
            Case = id,
            SampleRate = 16000,
            FrameRate = 100,
            DetectorSamples = detectorSamples,
            FirstFrame = interval?.FirstFrame,
            LastFrameInclusive = interval?.LastFrame,
            CommandStart = commandStart,
            CommandEnd = commandEnd,
            Captures = captures,
            Quiescent = quiescent,
            Status = "passed",
        }));
    }

    private static void RequirePreviousNativeJoin()
    {
        if (pendingNativeSession is { IsQuiescent: false })
        {
            throw new InvalidOperationException("A previous native session has unconfirmed closure; its owners are retained and replacement is denied.");
        }
        pendingNativeSession = null;
    }

    private static HandsFreeOfflineFixture.Manifest SyntheticManifest()
    {
        var root = HandsFreeOfflineFixture.CreateScratchDirectory();
        var assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
        var corpus = Directory.CreateDirectory(Path.Combine(root, "corpus")).FullName;
        return new(1, assets, Path.Combine(root, HandsFreeSetupCatalogue.NativeFileName), corpus,
            HandsFreeOfflineFixture.CaseIds.Select(id =>
            {
                var positive = id is not ("similar" or "negative");
                return new HandsFreeOfflineFixture.ReplayCase(id, id + ".pcm", 32000, new('0', 64),
                    id is "chunk-split" ? 127 : 320, 0, 0, 0, 0, positive ? 8000 : 0, positive ? 9000 : 0);
            }).ToImmutableArray());
    }

    // This observes command-only PCM, not recognition. It cannot emit a transcript or route an effect.
    private sealed class PcmCapture : IActivatedCapture, IDisposable
    {
        private readonly MemoryStream pcm = new();
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BlockingAudioStream? stream;
        private bool disposed;
        internal bool Ended { get; private set; }
        internal long Bytes => pcm.Length;
        internal short[] Pcm => MemoryMarshal.Cast<byte, short>(pcm.GetBuffer().AsSpan(0, checked((int)pcm.Length))).ToArray();
        public event ActivatedAudioAvailable? DataAvailable { add { } remove { } }
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized { add { } remove { } }
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler? SpeechDetected { add { } remove { } }
        public Task Completion => completion.Task;
        public long? RecorderReleasedTimestamp => null;
        internal void Bind(BlockingAudioStream input) => stream = input;
        public void Start() { }
        public Task ReleaseRecorder() => Task.CompletedTask;
        public void FinishRecognition(bool cancel)
        {
            Ended = true;
            if (cancel) { completion.TrySetCanceled(); }
        }
        internal void Drain()
        {
            var input = stream ?? throw new InvalidOperationException("Command-only stream is not bound.");
            var buffer = new byte[input.BufferedBytes];
            try
            {
                input.ReadExactly(buffer);
                pcm.Write(buffer);
                if (Ended) { completion.TrySetResult(); }
            }
            finally { Array.Clear(buffer); }
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Array.Clear(pcm.GetBuffer());
            pcm.Dispose();
        }
    }
}
