using System.Reflection;
using System.Runtime.InteropServices;

using AwesomeAssertions;

using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class PocketSphinxKeywordDetectorTests
{
    [Fact]
    public void ConfiguresKeywordOnlySearchAndDisablesAllNativeLoggingBeforeInitialization()
    {
        var metadata = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        var model = metadata.Assets[3].Path;
        var profile = new HandsFreeNativeProfile(metadata.NativeLibraryPath, metadata.ModelDirectory, metadata.DictionaryPath,
            metadata.VadPath, metadata.Assets, ["Kora"], "Synthetic tests only", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mdef"] = model,
            }, keywordThreshold: 1e30);
        using var exports = new FakeLibrary();
        var detector = new PocketSphinxKeywordDetector(profile, new(profile, "Kora", "kora"), exports);
        exports.Calls[0].Should().Be("log:0");
        exports.Strings["hmm"].Should().Be(profile.ModelDirectory);
        exports.Strings["dict"].Should().Be(profile.DictionaryPath);
        exports.Strings["keyphrase"].Should().Be("kora");
        exports.Strings["mdef"].Should().Be(model);
        foreach (var key in new[] { "lm", "lmctl", "fsg", "jsgf", "allphone", "kws", "rawlogdir", "mfclogdir", "senlogdir", "logfn" })
        {
            exports.Strings.Should().ContainKey(key).WhoseValue.Should().BeNull();
        }
        exports.SampleRate.Should().Be(16000);
        exports.FrameRate.Should().Be(100);
        exports.KeywordThreshold.Should().Be(1e30);
        exports.Calls.Should().ContainInOrder("config", "decoder", "effective", "start");
        detector.Dispose();
        detector.Dispose();
        exports.Calls.TakeLast(4).Should().Equal("finish", "free-decoder", "free-config", "unload");
        exports.Calls.Count(call => call is "unload").Should().Be(1);
        var accept = () => detector.Accept([1]);
        accept.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 10)]
    public void CopiesPcmAndReturnsInclusiveFramesWithoutInventingAnInterval(int first, int last)
    {
        var profile = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        using var exports = new FakeLibrary { First = first, Last = last, HasSegment = true };
        using var detector = new PocketSphinxKeywordDetector(profile, new(profile, "Kora", "kora"), exports);
        var segment = detector.Accept([short.MinValue, 0, short.MaxValue]);
        segment.Should().Be(new KeywordAudioSegment(first, last));
        segment!.CommandStartSample.Should().Be(((long)last + 1) * 160);
        exports.Pcm.Should().Equal(short.MinValue, 0, short.MaxValue);
        exports.Calls.TakeLast(2).Should().Equal("frames", "free-segment");
    }

    [Fact]
    public void NoNativeSegmentIsNotAKeyword()
    {
        var profile = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        using var exports = new FakeLibrary();
        using var detector = new PocketSphinxKeywordDetector(profile, new(profile, "Kora", "kora"), exports);
        detector.Accept([0, 0]).Should().BeNull();
        exports.Calls.Should().NotContain("free-segment");
    }

    [Theory]
    [InlineData("config", 0, 0)]
    [InlineData("set", 0, 1)]
    [InlineData("set-sample-rate", 0, 1)]
    [InlineData("set-int", 0, 1)]
    [InlineData("set-threshold", 0, 1)]
    [InlineData("decoder", 0, 1)]
    [InlineData("phrase", 1, 1)]
    [InlineData("rate", 1, 1)]
    [InlineData("frames", 1, 1)]
    [InlineData("threshold", 1, 1)]
    [InlineData("start", 1, 1)]
    [InlineData("missing-export", 0, 0)]
    public void InitializationFailureReleasesOnlyAcquiredOwners(string fault, int decoders, int configs)
    {
        var profile = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        using var exports = new FakeLibrary { Fault = fault };
        var create = () => new PocketSphinxKeywordDetector(profile, new(profile, "Kora", "kora"), exports);
        create.Should().Throw<InvalidOperationException>();
        exports.Calls.Count(call => call is "free-decoder").Should().Be(decoders);
        exports.Calls.Count(call => call is "free-config").Should().Be(configs);
        exports.Calls.Count(call => call is "unload").Should().Be(1);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("identity")]
    [InlineData("segment-frames")]
    public void DecodeFailureIsExplicitAndFreesAnAcquiredSegment(string fault)
    {
        var profile = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        using var exports = new FakeLibrary { Fault = fault, HasSegment = true };
        using var detector = new PocketSphinxKeywordDetector(profile, new(profile, "Kora", "kora"), exports);
        var accept = () => detector.Accept([1, 2]);
        if (fault is "identity") { accept.Should().Throw<InvalidDataException>(); }
        else { accept.Should().Throw<InvalidOperationException>(); }
        exports.Calls.Count(call => call is "free-segment").Should().Be(fault is "process" ? 0 : 1);
    }

    [Fact]
    public void FinishFailureStillReleasesEachOwnerExactlyOnce()
    {
        var profile = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        using var exports = new FakeLibrary { Fault = "finish" };
        var detector = new PocketSphinxKeywordDetector(profile, new(profile, "Kora", "kora"), exports);
        var dispose = detector.Dispose;
        dispose.Should().Throw<InvalidOperationException>();
        detector.Dispose();
        exports.Calls.TakeLast(4).Should().Equal("finish", "free-decoder", "free-config", "unload");
    }

    [Fact]
    public void ConfigurationFromAnotherProfileCannotAcquireTheLibrary()
    {
        var profile = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        var other = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        using var exports = new FakeLibrary();
        var create = () => new PocketSphinxKeywordDetector(profile, new(other, "Kora", "kora"), exports);
        create.Should().Throw<InvalidOperationException>();
        exports.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidKeywordThresholdCannotBecomeProfileMetadata(double threshold)
    {
        var metadata = PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        var create = () => new HandsFreeNativeProfile(metadata.NativeLibraryPath, metadata.ModelDirectory,
            metadata.DictionaryPath, metadata.VadPath, metadata.Assets, ["Kora"], "Synthetic tests only",
            keywordThreshold: threshold);
        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("keywordThreshold");
    }

    [Fact]
    public void ExportDelegatesDeclareCdeclAndUtf8Strings()
    {
        foreach (var type in new[] { typeof(PocketSphinxKeywordDetector.CreateConfig), typeof(PocketSphinxKeywordDetector.Free),
            typeof(PocketSphinxKeywordDetector.UnaryPointer), typeof(PocketSphinxKeywordDetector.UnaryInt),
            typeof(PocketSphinxKeywordDetector.SetLog), typeof(PocketSphinxKeywordDetector.SegmentFree),
            typeof(PocketSphinxKeywordDetector.SegmentFrames), typeof(PocketSphinxKeywordDetector.ProcessRaw),
            typeof(PocketSphinxKeywordDetector.SetString),
            typeof(PocketSphinxKeywordDetector.SetInt),
            typeof(PocketSphinxKeywordDetector.SetFloat), typeof(PocketSphinxKeywordDetector.GetFloat),
            typeof(PocketSphinxKeywordDetector.GetInt), typeof(PocketSphinxKeywordDetector.GetString) })
        {
            type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention.Should().Be(CallingConvention.Cdecl);
            foreach (var parameter in type.GetMethod("Invoke")!.GetParameters().Where(parameter => parameter.ParameterType == typeof(string)))
            {
                parameter.GetCustomAttribute<MarshalAsAttribute>()!.Value.Should().Be(UnmanagedType.LPUTF8Str);
            }
        }
    }

    private sealed class FakeLibrary : IPocketSphinxLibrary
    {
        private readonly nint phrase = Marshal.StringToCoTaskMemUTF8("kora");
        private readonly nint wrong = Marshal.StringToCoTaskMemUTF8("not-kora");
        private bool disposed;
        internal List<string> Calls { get; } = [];
        internal Dictionary<string, string?> Strings { get; } = new(StringComparer.Ordinal);
        internal string? Fault { get; init; }
        internal bool HasSegment { get; init; }
        internal int First { get; init; }
        internal int Last { get; init; }
        internal int SampleRate { get; private set; }
        internal int FrameRate { get; private set; }
        internal double KeywordThreshold { get; private set; }
        internal short[] Pcm { get; private set; } = [];

        public T Export<T>(string symbol) where T : Delegate
        {
            if (Fault is "missing-export") { throw new InvalidOperationException("Synthetic missing C ABI export."); }
            Delegate export = symbol switch
            {
                "err_set_logfp" => new PocketSphinxKeywordDetector.SetLog(stream => Calls.Add($"log:{stream}")),
                "ps_config_init" => new PocketSphinxKeywordDetector.CreateConfig(_ => Acquire("config", 1)),
                "ps_config_free" => new PocketSphinxKeywordDetector.Free(_ => Free("free-config")),
                "ps_free" => new PocketSphinxKeywordDetector.Free(_ => Free("free-decoder")),
                "ps_config_set_str" => new PocketSphinxKeywordDetector.SetString((_, key, value) =>
                {
                    Strings.Add(key, value);
                    return Fault is "set" ? 0 : 1;
                }),
                "ps_config_set_int" => new PocketSphinxKeywordDetector.SetInt((_, key, value) =>
                {
                    if (string.Equals(key, "samprate", StringComparison.Ordinal))
                    {
                        SampleRate = value;
                        return Fault is "set-sample-rate" ? 0 : 1;
                    }
                    key.Should().Be("frate");
                    FrameRate = value;
                    return Fault is "set-int" ? 0 : 1;
                }),
                "ps_config_set_float" => new PocketSphinxKeywordDetector.SetFloat((_, key, value) =>
                {
                    key.Should().Be("kws_threshold");
                    KeywordThreshold = value;
                    return Fault is "set-threshold" ? 0 : 1;
                }),
                "ps_config_float" => new PocketSphinxKeywordDetector.GetFloat((_, key) =>
                {
                    key.Should().Be("kws_threshold");
                    return Fault is "threshold" ? 0 : KeywordThreshold;
                }),
                "ps_init" => new PocketSphinxKeywordDetector.UnaryPointer(_ => Acquire("decoder", 2)),
                "ps_get_config" => new PocketSphinxKeywordDetector.UnaryPointer(_ => Acquire("effective", 1)),
                "ps_config_str" => new PocketSphinxKeywordDetector.GetString((_, _) => Fault is "phrase" ? wrong : phrase),
                "ps_config_int" => new PocketSphinxKeywordDetector.GetInt((_, key) =>
                    string.Equals(key, "samprate", StringComparison.Ordinal)
                        ? Fault is "rate" ? 8000 : SampleRate
                        : Fault is "frames" ? 50 : FrameRate),
                "ps_start_utt" => new PocketSphinxKeywordDetector.UnaryInt(_ => Status("start")),
                "ps_end_utt" => new PocketSphinxKeywordDetector.UnaryInt(_ => Status("finish")),
                "ps_process_raw" => new PocketSphinxKeywordDetector.ProcessRaw((_, samples, count, noSearch, full) =>
                {
                    noSearch.Should().Be(0);
                    full.Should().Be(0);
                    Pcm = new short[checked((int)count)];
                    Marshal.Copy(samples, Pcm, 0, Pcm.Length);
                    return Status("process");
                }),
                "ps_seg_iter" => new PocketSphinxKeywordDetector.UnaryPointer(_ => HasSegment ? 3 : 0),
                "ps_seg_word" => new PocketSphinxKeywordDetector.UnaryPointer(_ => Fault is "identity" ? wrong : phrase),
                "ps_seg_frames" => new PocketSphinxKeywordDetector.SegmentFrames(ReadFrames),
                "ps_seg_free" => new PocketSphinxKeywordDetector.SegmentFree(_ => Calls.Add("free-segment")),
                _ => throw new InvalidOperationException("Unexpected synthetic export."),
            };
            return (T)export;
        }

        private nint Acquire(string operation, nint handle)
        {
            Calls.Add(operation);
            return string.Equals(Fault, operation, StringComparison.Ordinal) ? 0 : handle;
        }

        private int Free(string operation) { Calls.Add(operation); return 0; }
        private int Status(string operation) { Calls.Add(operation); return string.Equals(Fault, operation, StringComparison.Ordinal) ? -1 : 0; }
        private void ReadFrames(nint segment, out int first, out int last)
        {
            Calls.Add("frames");
            if (Fault is "segment-frames") { throw new InvalidOperationException("Synthetic segment failure."); }
            first = First;
            last = Last;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Calls.Add("unload");
            Marshal.FreeCoTaskMem(phrase);
            Marshal.FreeCoTaskMem(wrong);
        }
    }
}
