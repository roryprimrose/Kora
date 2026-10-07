using Kora.Tools.Clipboard;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ClipboardSnapshotBroker clipboardPreview;
    private readonly ClipboardRead clipboardRead;
    private readonly ClipboardReuse clipboardReuse;
    private readonly ClipboardRevoke clipboardRevoke;
    private Func<bool> clipboardOwnershipEligible = static () => false;
    public ClipboardSnapshot? ClipboardPreview => clipboardPreview.Current;
    public event EventHandler? ClipboardPreviewChanged;
    public void BindClipboardOwnershipGate(Func<bool> gate) => clipboardOwnershipEligible = gate;

    private bool IsClipboardEligible(RequestOrigin origin, long callRevision) =>
        !disposed && IsHostInputEligible && clipboardOwnershipEligible()
        && callRevision == CallPolicyRevision && ClipboardCommand.IsDeliberateOrigin(origin)
        && (origin != RequestOrigin.ActivatedVoice || CallObservation.AllowActivation);

    private void OnClipboardPreviewChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() =>
        {
            OnPropertyChanged(nameof(IsCancelTaskVisible));
            ClipboardPreviewChanged?.Invoke(this, EventArgs.Empty);
        });

    public Task PreviewClipboardAsync() => RunClipboardUiAsync("preview clipboard");

    public Task ReuseClipboardAsync(Guid snapshotId) => RunClipboardUiAsync("reuse clipboard snapshot " + snapshotId.ToString("D"));
    public Task RevokeClipboardAsync() => RunClipboardUiAsync("revoke clipboard snapshot");

    private Task RunClipboardUiAsync(string command) => Kora.Application.Hosting.HostRequestRunner.RunAsync(
        HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
        () => RouteTranscriptAsync(command, 1, Kora.Core.Auditing.SecurityAuditInitiator.LocalUser));

    public void ClearClipboardPreview() => clipboardPreview.Clear();

    private async Task ExecuteClipboardCommandAsync(ClipboardCommand command)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        var revision = CallPolicyRevision;
        bool Eligible() => IsClipboardEligible(origin, revision);
        ClipboardOutcome outcome;
        switch (command.Operation)
        {
            case ClipboardOperation.Capture:
            case ClipboardOperation.ExplainUnavailable:
                if (!disposed && IsHostInputEligible)
                {
                    ShowInformation("Local clipboard capture requested.",
                        "Reading one bounded plain-text source. Cancel task discards the request; native resource release must finish before handoff/exit.");
                }
                outcome = await clipboardRead.ExecuteAsync(Eligible, CancellationToken.None);
                break;
            case ClipboardOperation.Reuse:
                outcome = clipboardReuse.Execute(command.SnapshotId, Eligible);
                break;
            case ClipboardOperation.Revoke:
                outcome = clipboardRevoke.Execute(Eligible);
                break;
            default:
                ShowInformation("Clipboard snapshot ID required.",
                    "Use reuse clipboard snapshot followed by the exact host snapshot ID shown in the native preview.");
                return;
        }
        // Never put the clipboard body in the response, transcript, speech or a model prompt.
        if (!disposed && IsHostInputEligible)
        {
            ShowInformation("Local clipboard preview: " + outcome,
                DescribeClipboardOutcome(outcome) + " Explicit plain-text snapshot only (256 KiB UTF-8 maximum); no truncation, clipboard write or history. "
                + "Reuse requires the same snapshot ID; clear/revoke discards it. Clipboard changes never replace an existing snapshot. "
                + "Explanation is unavailable until the qualified local tool-loop and clipboard-answering gates pass. "
                + "Preview is not egress approval. Text may contain secrets; no safety/redaction claim is made.");
        }
    }

    internal static string DescribeClipboardOutcome(ClipboardOutcome outcome) => outcome switch
    {
        ClipboardOutcome.Captured => "A fresh immutable snapshot is available in the native preview.",
        ClipboardOutcome.Reused => "The same identified snapshot was selected again; the clipboard was not reread.",
        ClipboardOutcome.Revoked => "The local preview and selected snapshot were cleared; the Windows clipboard was not changed.",
        ClipboardOutcome.Empty => "Unicode plain text is empty. Copy text yourself, then make a new explicit request.",
        ClipboardOutcome.UnsupportedFormat => "Unicode plain text is unavailable. HTML, images, files and URL fetching are unsupported.",
        ClipboardOutcome.Oversize => "The text exceeds 256 KiB UTF-8. Select a smaller source yourself and request a new snapshot; nothing was truncated.",
        ClipboardOutcome.InvalidText => "Clipboard text is malformed or has invalid Unicode. Replace the source yourself before requesting again.",
        ClipboardOutcome.Busy => "A read or another application's clipboard operation is busy. Wait, then make a new explicit request; no retry is queued.",
        ClipboardOutcome.AccessDenied => "Windows denied clipboard access. Resolve the access issue, then make a new explicit request.",
        ClipboardOutcome.Changed => "The source version changed during capture. Make a new explicit request; no mixed-version snapshot was admitted.",
        ClipboardOutcome.Unavailable => "The clipboard source or native read/release could not be verified. Resolve the failure; unverified native release requires restart and blocks clean handoff.",
        ClipboardOutcome.Denied => "Current host ownership, privacy, call policy or original input origin does not permit capture/reuse.",
        ClipboardOutcome.Stale => "That snapshot ID is no longer selected or was revoked. Request a fresh preview; no automatic recapture occurred.",
        ClipboardOutcome.Cancelled => "Capture was cancelled or its generation/eligibility changed. No late result was presented.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}
