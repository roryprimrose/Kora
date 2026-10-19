using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed class PresenceSwitchTimingCapture : IAsyncDisposable, IDisposable
{
    internal const int Capacity = 256;
    private readonly MeterListener listener = new();
    private readonly Lock gate = new();
    private readonly Channel<Stamp> queue = Channel.CreateBounded<Stamp>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false,
    });
    private readonly Task worker;
    private IOException? failure;
    private bool closed;
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Stamp(PresenceSwitchPhase Phase, long Timestamp);

    private PresenceSwitchTimingCapture(TextWriter output)
    {
        var instrument = PresenceSwitchTiming.Instrument;
        listener.InstrumentPublished = (published, observer) =>
        {
            if (ReferenceEquals(published, instrument)) { observer.EnableMeasurementEvents(published); }
        };
        listener.SetMeasurementEventCallback<long>(Capture);
        // Timing output is opt-in scratch evidence and cannot carry a caller's host authority.
        using (ExecutionContext.SuppressFlow()) { worker = Task.Run(() => DrainAsync(output)); }
        listener.Start();
    }

    internal static PresenceSwitchTimingCapture? Create(string? setting, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return setting switch
        {
            null or "" => null,
            "1" => new(output),
            _ => throw new InvalidDataException("KORA_PRESENCE_TIMING must be absent or exactly 1."),
        };
    }

    private void Capture(Instrument instrument, long value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        lock (gate)
        {
            if (closed || failure is not null) { return; }
            if (value <= 0 || tags.Length != 1 || !string.Equals(tags[0].Key, "phase", StringComparison.Ordinal)
                || tags[0].Value is not PresenceSwitchPhase phase || !Enum.IsDefined(phase))
            {
                failure = new IOException("Presence phase timing received an invalid measurement; the trial is incomplete.");
            }
            else if (!queue.Writer.TryWrite(new(phase, value)))
            {
                failure = new IOException("The bounded presence phase timing capture is full; the trial is incomplete.");
            }
        }
    }

    private async Task DrainAsync(TextWriter output)
    {
        await output.WriteLineAsync("presence-timing-v1\tfrequency\t"
            + Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await foreach (var stamp in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await output.WriteLineAsync("presence-timing-v1\t" + stamp.Phase + "\t"
                + stamp.Timestamp.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }
        await output.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
#pragma warning disable VSTHRD002 // Program owns synchronous shutdown; the timing writer never dispatches to the UI.
        DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    public async ValueTask DisposeAsync()
    {
        listener.Dispose();
        lock (gate)
        {
            closed = true;
            queue.Writer.TryComplete();
        }
#pragma warning disable VSTHRD003 // Join the owned, context-free output worker before the supervisor receives EOF.
        await worker.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        if (failure is { } error) { throw new IOException("Presence phase timing capture failed.", error); }
    }
}
