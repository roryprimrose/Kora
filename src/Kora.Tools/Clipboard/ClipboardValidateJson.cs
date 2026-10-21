using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Tools.Clipboard;

public sealed partial class ClipboardValidateJson
{
    private readonly ClipboardSnapshotBroker snapshots;
    private readonly TimeProvider time;
    private readonly ILogger<ClipboardValidateJson> logger;
    private readonly Func<ClipboardSnapshot, CancellationToken, ClipboardJsonSyntaxResult> parse;

    public ClipboardValidateJson(ClipboardSnapshotBroker snapshots, TimeProvider time, ILogger<ClipboardValidateJson> logger)
        : this(snapshots, time, logger, ClipboardJsonSyntax.Validate) { }

    internal ClipboardValidateJson(ClipboardSnapshotBroker snapshots, TimeProvider time, ILogger<ClipboardValidateJson> logger,
        Func<ClipboardSnapshot, CancellationToken, ClipboardJsonSyntaxResult> parse)
    {
        this.snapshots = snapshots;
        this.time = time;
        this.logger = logger;
        this.parse = parse;
    }

    public ClipboardJsonSyntaxResult Execute(ClipboardSnapshot source, Func<bool> eligible, CancellationToken token)
    {
        var request = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        bool Current() => request.Origin == RequestOrigin.LocalUi && !token.IsCancellationRequested
            && eligible() && IsCurrentSource(source);
        try
        {
            if (!Current())
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return new(token.IsCancellationRequested ? ClipboardJsonSyntaxOutcome.Cancelled : ClipboardJsonSyntaxOutcome.Unavailable);
            }
            var result = parse(source, token);
            if (!Current())
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return new(ClipboardJsonSyntaxOutcome.Cancelled);
            }
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            return new(ClipboardJsonSyntaxOutcome.Cancelled);
        }
        catch (Exception)
        {
            Failure(logger);
            activity.Complete(HostOperationOutcome.Failed);
            return new(ClipboardJsonSyntaxOutcome.Failed);
        }

    }

    public Func<bool> CaptureEligibility(ClipboardSnapshot source, Func<bool> eligible)
    {
        var retired = !eligible() || !IsCurrentSource(source);
        return () =>
        {
            retired = retired || !eligible() || !IsCurrentSource(source);
            return !retired;
        };
    }

    private bool IsCurrentSource(ClipboardSnapshot source) => ReferenceEquals(snapshots.Current, source)
        && time.GetUtcNow() >= source.CapturedAt
        && time.GetUtcNow() < source.CapturedAt + ClipboardExplanationContext.Lifetime;
}
