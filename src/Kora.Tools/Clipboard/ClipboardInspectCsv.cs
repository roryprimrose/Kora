using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Tools.Clipboard;

/// <summary>Native-only inspection of an already admitted exact source, never a capture or model-use route.</summary>
public sealed class ClipboardInspectCsv
{
    public ClipboardCsvResult Execute(ClipboardSnapshot source, Func<ClipboardSnapshot?> current,
        Func<bool> eligible, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        if (HostActivity.RequireCurrent().Request.Origin != RequestOrigin.LocalUi || !Current())
        {
            activity.Complete(HostOperationOutcome.Failed);
            return new(ClipboardCsvOutcome.Stale);
        }
        try
        {
            token.ThrowIfCancellationRequested();
            var result = CommaSeparatedProfile.Inspect(source.Text, token);
            if (!Current())
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return new(ClipboardCsvOutcome.Stale);
            }
            token.ThrowIfCancellationRequested();
            activity.Complete(HostOperationOutcome.Completed);
            return new(ClipboardCsvOutcome.Inspected, result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            return new(ClipboardCsvOutcome.Cancelled);
        }

        bool Current() => eligible() && ReferenceEquals(current(), source);
    }
}
