using System.Runtime.InteropServices;

namespace Kora.Windows.Audio;

internal sealed class PocketSphinxKeywordDetector : IHandsFreeKeywordDetector
{
    private readonly IPocketSphinxLibrary library;
    private readonly nint config;
    private readonly nint decoder;
    private readonly string name;
    private readonly Free freeConfig;
    private readonly Free freeDecoder;
    private readonly ProcessRaw process;
    private readonly UnaryPointer segments;
    private readonly UnaryPointer word;
    private readonly SegmentFrames frames;
    private readonly SegmentFree freeSegment;
    private readonly UnaryInt finish;
    private bool disposed;

    public PocketSphinxKeywordDetector(HandsFreeNativeProfile profile, PocketSphinxKeywordConfiguration configuration,
        IPocketSphinxLibrary? nativeLibrary = null)
    {
        if (!ReferenceEquals(profile, configuration.Profile) || !profile.CaptureQualified
            || !profile.Names.Contains(configuration.AssistantName))
        {
            throw new InvalidOperationException("The exact configured name has no qualified English wake profile.");
        }
        name = configuration.Keyphrase;
        library = nativeLibrary ?? new PocketSphinxLibrary(profile.NativeLibraryPath);
        try
        {
            Export<SetLog>("err_set_logfp")(nint.Zero);
            freeConfig = Export<Free>("ps_config_free");
            freeDecoder = Export<Free>("ps_free");
            process = Export<ProcessRaw>("ps_process_raw");
            segments = Export<UnaryPointer>("ps_seg_iter");
            word = Export<UnaryPointer>("ps_seg_word");
            frames = Export<SegmentFrames>("ps_seg_frames");
            freeSegment = Export<SegmentFree>("ps_seg_free");
            finish = Export<UnaryInt>("ps_end_utt");
            config = Export<CreateConfig>("ps_config_init")(nint.Zero);
            if (config == nint.Zero) { throw NativeFailure(); }
            var setString = Export<SetString>("ps_config_set_str");
            var setFloat = Export<SetFloat>("ps_config_set_float");
            var setInt = Export<SetInt>("ps_config_set_int");
            Require(setString(config, "hmm", profile.ModelDirectory));
            Require(setString(config, "dict", profile.DictionaryPath));
            foreach (var pair in profile.ConfigurationFiles) { Require(setString(config, pair.Key, pair.Value)); }
            Require(setString(config, "keyphrase", name));
            foreach (var option in new[] { "lm", "lmctl", "fsg", "jsgf", "allphone", "kws",
                "rawlogdir", "mfclogdir", "senlogdir", "logfn" })
            {
                Require(setString(config, option, null));
            }
            Require(setFloat(config, "samprate", 16000));
            Require(setInt(config, "frate", 100));
            decoder = Export<UnaryPointer>("ps_init")(config);
            if (decoder == nint.Zero) { throw NativeFailure(); }
            var effective = Export<UnaryPointer>("ps_get_config")(decoder);
            if (!string.Equals(Marshal.PtrToStringUTF8(Export<GetString>("ps_config_str")(effective, "keyphrase")),
                    configuration.Keyphrase, StringComparison.Ordinal)
                || Export<GetFloat>("ps_config_float")(effective, "samprate") != 16000
                || Export<GetInt>("ps_config_int")(effective, "frate") != 100
                || Export<UnaryInt>("ps_start_utt")(decoder) < 0)
            {
                throw NativeFailure();
            }
        }
        catch
        {
            if (decoder != nint.Zero) { (freeDecoder ?? throw NativeFailure())(decoder); }
            if (config != nint.Zero) { (freeConfig ?? throw NativeFailure())(config); }
            library.Dispose();
            throw;
        }
    }

    public unsafe KeywordAudioSegment? Accept(ReadOnlySpan<short> audio)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        fixed (short* samples = audio)
        {
            if (process(decoder, (nint)samples, (nuint)audio.Length, 0, 0) < 0) { throw NativeFailure(); }
        }
        var segment = segments(decoder);
        if (segment == nint.Zero) { return null; }
        try
        {
            if (!string.Equals(Marshal.PtrToStringUTF8(word(segment)), name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Native keyword identity did not match the admitted configured name.");
            }
            frames(segment, out var first, out var last);
            return new(first, last);
        }
        finally { freeSegment(segment); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        try
        {
            if (finish(decoder) < 0) { throw NativeFailure(); }
        }
        finally
        {
            freeDecoder(decoder);
            freeConfig(config);
            library.Dispose();
        }
    }

    private T Export<T>(string symbol) where T : Delegate =>
        library.Export<T>(symbol);
    private static void Require(nint result) { if (result == nint.Zero) { throw NativeFailure(); } }
    private static InvalidOperationException NativeFailure() => new("The admitted native keyword engine failed.");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint CreateConfig(nint definition);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Free(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint UnaryPointer(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int UnaryInt(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetLog(nint stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SegmentFree(nint segment);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SegmentFrames(nint segment, out int first, out int last);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ProcessRaw(nint decoder, nint samples, nuint count, int noSearch, int fullUtterance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint SetString(nint config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint SetFloat(nint config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint SetInt(nint config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate double GetFloat(nint config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int GetInt(nint config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint GetString(nint config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
}
