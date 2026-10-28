using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kora.Windows.Audio;

internal sealed class HandsFreeMicrophone(WasapiRecorder recorder, MMDevice? endpoint) : IHandsFreeMicrophone
{
    private int stopping;
    private bool received;
    public event HandsFreeSamplesAvailable? Samples;
    public event EventHandler? Failed;

    public void Start()
    {
        recorder.DataAvailable += OnData;
        recorder.RecordingStopped += OnStopped;
        recorder.StartRecording();
    }

    private void OnData(ReadOnlySpan<byte> audio, AudioClientBufferFlags flags, long position, long timestamp)
    {
        if (audio.Length % 2 != 0 || audio.Length > 64000
            || flags.HasFlag(AudioClientBufferFlags.TimestampError)
            || received && flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity))
        {
            Failed?.Invoke(this, EventArgs.Empty);
            return;
        }
        received = true;
        if (flags.HasFlag(AudioClientBufferFlags.Silent))
        {
            var silence = new short[audio.Length / 2];
            try { Samples?.Invoke(silence); }
            finally { Array.Clear(silence); }
        }
        else { Samples?.Invoke(MemoryMarshal.Cast<byte, short>(audio)); }
    }

    private void OnStopped(object? sender, StoppedEventArgs args)
    {
        if (Volatile.Read(ref stopping) == 0) { Failed?.Invoke(this, EventArgs.Empty); }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref stopping, 1);
        recorder.DataAvailable -= OnData;
        recorder.RecordingStopped -= OnStopped;
        try { await Task.Run(recorder.StopRecording, CancellationToken.None).ConfigureAwait(false); }
        finally
        {
            try { await recorder.DisposeAsync().ConfigureAwait(false); }
            finally { endpoint?.Dispose(); }
        }
    }
}
