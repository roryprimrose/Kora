using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Tools.Clipboard;

namespace Kora.Application.Context;

/// <summary>Admits the built-in read before native access, independently of model consent.</summary>
public sealed class ClipboardReadWorkflow(ClipboardSnapshotBroker broker, ClipboardExplanationAdmission admission,
    ClipboardPermissionSourceUsePolicy permissions) : IClipboardReadWorkflow
{
    public Task<ClipboardOutcome> ReadAsync(Func<bool> eligible, CancellationToken token)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        if (!ClipboardCommand.IsDeliberateOrigin(origin) || !eligible())
        { return Task.FromResult(ClipboardOutcome.Denied); }
        return admission.RunReadAsync(origin, eligible, async (_, current, admitted) =>
        {
            try
            {
                var permission = await permissions.RequestReadAsync(current, admitted).ConfigureAwait(false);
                if (permission is null) { return ClipboardOutcome.Denied; }
                return await broker.CaptureAsync(current, admitted,
                    check => permissions.ValidatePermissionAsync(permission, check)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (admitted.IsCancellationRequested)
            { return ClipboardOutcome.Cancelled; }
            catch (OperationCanceledException exception)
            { throw new InvalidOperationException("Clipboard permission ended without caller cancellation.", exception); }
        }, token);
    }
}
