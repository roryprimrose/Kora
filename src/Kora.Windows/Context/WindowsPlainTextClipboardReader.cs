using System.Diagnostics;
using System.Text;

using Kora.Core.Context;

namespace Kora.Windows.Context;

public sealed class WindowsPlainTextClipboardReader : IPlainTextClipboardReader
{
    private static readonly ActivitySource ActivitySource =
        new("Kora.Windows", typeof(WindowsPlainTextClipboardReader).Assembly.GetName().Version!.ToString());
    private readonly IClipboardNative native;

    public WindowsPlainTextClipboardReader() : this(new WindowsClipboardNative()) { }
    internal WindowsPlainTextClipboardReader(IClipboardNative native) => this.native = native;

    public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ClipboardReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // No watcher, retries or preactivation read. One bounded request owns this STA.
        var thread = new Thread(() =>
        {
            using var activity = ActivitySource.StartActivity("context.clipboard.read");
            activity?.SetStatus(ActivityStatusCode.Error);
            try
            {
                var result = Read(cancellationToken);
                activity?.SetStatus(result.Outcome == ClipboardOutcome.Captured ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
                if (result.Outcome == ClipboardOutcome.Cancelled) { completion.SetCanceled(cancellationToken); }
                else { completion.SetResult(result); }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.SetCanceled(cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
            {
                completion.SetException(new InvalidOperationException(
                    "Native plain-text clipboard read failed. Failure type: " + exception.GetType().Name));
            }
        })
        { IsBackground = true, Name = "Kora clipboard snapshot" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private ClipboardReadResult Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var version = native.GetSequence();
        if (version == 0) { return new(ClipboardOutcome.Unavailable); }
        if (!native.Open()) { return new(Failure(ClipboardOutcome.Busy)); }
        ClipboardReadResult result;
        bool closed;
        try
        {
            token.ThrowIfCancellationRequested();
            result = ReadOpened(version, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { result = new(ClipboardOutcome.Cancelled); }
        finally { closed = native.Close(); }
        return closed ? result : new(ClipboardOutcome.Unavailable, ResourcesReleased: false);
    }

    private ClipboardReadResult ReadOpened(uint version, CancellationToken token)
    {
        if (native.GetSequence() != version) { return new(ClipboardOutcome.Changed); }
        if (!native.HasUnicodeText()) { return new(ClipboardOutcome.UnsupportedFormat); }
        token.ThrowIfCancellationRequested();
        var handle = native.GetUnicodeText();
        if (handle == nint.Zero) { return new(Failure(ClipboardOutcome.Unavailable)); }
        var size = native.GetSize(handle);
        if (size < sizeof(char) || size % sizeof(char) != 0) { return new(ClipboardOutcome.InvalidText); }
        var pointer = native.Lock(handle);
        if (pointer == nint.Zero) { return new(Failure(ClipboardOutcome.Unavailable)); }
        ClipboardReadResult result;
        bool unlocked;
        try { result = ReadLocked(pointer, size, version, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { result = new(ClipboardOutcome.Cancelled); }
        finally { unlocked = native.Unlock(handle); }
        return unlocked ? result : new(ClipboardOutcome.Unavailable, ResourcesReleased: false);
    }

    private ClipboardReadResult ReadLocked(nint pointer, nuint size, uint version, CancellationToken token)
    {
        var limit = (int)Math.Min(size / sizeof(char), (nuint)ClipboardSnapshot.MaximumUtf8Bytes + 1);
        var text = new StringBuilder();
        for (var index = 0; index < limit; index++)
        {
            token.ThrowIfCancellationRequested();
            var character = native.ReadCodeUnit(pointer, index);
            if (character == '\0')
            {
                var source = text.ToString();
                if (native.GetSequence() != version) { return new(ClipboardOutcome.Changed); }
                if (source.Length == 0) { return new(ClipboardOutcome.Empty); }
                try
                {
                    if (new UTF8Encoding(false, true).GetByteCount(source) > ClipboardSnapshot.MaximumUtf8Bytes)
                    {
                        return new(ClipboardOutcome.Oversize);
                    }
                }
                catch (EncoderFallbackException) { return new(ClipboardOutcome.InvalidText); }
                return new(ClipboardOutcome.Captured, source, version);
            }
            text.Append(character);
        }
        return new(size / sizeof(char) > ClipboardSnapshot.MaximumUtf8Bytes
            ? ClipboardOutcome.Oversize : ClipboardOutcome.InvalidText);
    }

    private ClipboardOutcome Failure(ClipboardOutcome fallback) =>
        native.LastError == 5 ? ClipboardOutcome.AccessDenied : fallback;
}