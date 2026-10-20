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
    private readonly ClipboardExplain clipboardExplain;
    private readonly ClipboardValidateJson clipboardValidateJson;
    private readonly ClipboardInspectWebUrl clipboardInspectWebUrl;
    private readonly Kora.Application.Context.ClipboardExplanationWorkflow? clipboardExplanation;
    private Func<bool> clipboardOwnershipEligible = static () => false;
    public ClipboardSnapshot? ClipboardPreview => clipboardPreview.Current;
    public string? ClipboardExplanation => clipboardExplanation?.CurrentExplanation;
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

    public Kora.Core.Network.WebUrlSyntaxResult InspectClipboardWebUrl(
        ClipboardSnapshot source, Func<bool> nativeCurrent, CancellationToken token)
    {
        var revision = CallPolicyRevision;
        bool Eligible() => nativeCurrent() && IsClipboardEligible(source.Request.Origin, revision);
        return clipboardInspectWebUrl.Execute(source, Eligible, token);
    }

    public Task ReuseClipboardAsync(Guid snapshotId) => RunClipboardUiAsync("reuse clipboard snapshot " + snapshotId.ToString("D"));
    public Task RevokeClipboardAsync() => RunClipboardUiAsync("revoke clipboard snapshot");

    public Func<bool> CaptureClipboardJsonEligibility(ClipboardSnapshot source)
    {
        var input = CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision);
        var privacy = privacyObservation.Current.TopologyRevision;
        var ownership = clipboardOwnershipEligible;
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        return clipboardValidateJson.CaptureEligibility(source, () => input.Origin == RequestOrigin.LocalUi
            && input.Eligible() && IsClipboardEligible(RequestOrigin.LocalUi, input.Revision)
            && privacyObservation.Current.TopologyRevision == privacy
            && ReferenceEquals(ownership, clipboardOwnershipEligible)
            && ownerBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && input.NavigationOwnerRevision == auditImpactOwnerRevision()
            && ReferenceEquals(ClipboardPreview, source));
    }

    public ClipboardJsonSyntaxResult ValidateClipboardJson(ClipboardSnapshot source, Func<bool> eligible, CancellationToken token)
    {
        using var activity = HostActivity.BeginRoot(HostRequest.Create(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        var result = clipboardValidateJson.Execute(source, eligible, token);
        activity.Complete(result.Outcome == ClipboardJsonSyntaxOutcome.Failed ? HostOperationOutcome.Failed
            : result.Outcome is ClipboardJsonSyntaxOutcome.Cancelled or ClipboardJsonSyntaxOutcome.Unavailable
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Completed);
        return result;
    }

    public Task ExplainClipboardAsync(Guid snapshotId) => Kora.Application.Hosting.HostRequestRunner.RunAsync(
        HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi, async () =>
        {
            var origin = HostActivity.RequireCurrent().Request.Origin;
            var revision = CallPolicyRevision;
            var result = await clipboardExplain.ExecuteAsync(snapshotId,
                () => IsClipboardEligible(origin, revision), CancellationToken.None);
            ShowClipboardExplanationOutcome(result);
        });

    private Task RunClipboardUiAsync(string command) => Kora.Application.Hosting.HostRequestRunner.RunAsync(
        HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
        () => RouteTranscriptAsync(command, 1, Kora.Core.Auditing.SecurityAuditInitiator.LocalUser,
            Interlocked.Read(ref manualCallSpeechRevision)));

    public void ClearClipboardPreview()
    {
        clipboardExplanation?.Clear();
        clipboardPreview.Clear();
    }

    private async Task ExecuteClipboardCommandAsync(ClipboardCommand command)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        var revision = CallPolicyRevision;
        bool Eligible() => IsClipboardEligible(origin, revision);
        ClipboardOutcome outcome;
        switch (command.Operation)
        {
            case ClipboardOperation.Explain:
                ShowClipboardExplanationOutcome(await clipboardExplain.ExecuteAsync(null, Eligible, CancellationToken.None));
                return;
            case ClipboardOperation.Capture:
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
        // Source and answer are native-only; generic response/transcript/speech never receive either body.
        if (!disposed && IsHostInputEligible)
        {
            ShowInformation("Local clipboard preview: " + outcome,
                DescribeClipboardOutcome(outcome) + " Explicit plain-text snapshot only (256 KiB UTF-8 maximum); no truncation, clipboard write or history. "
                + "Reuse requires the same snapshot ID; clear/revoke discards it. Clipboard changes never replace an existing snapshot. "
                + "Explanation requires explicit native review, implemented clipboard source-use policy and a qualified local runtime; capture is not explanation success. "
                + "Preview is not egress approval. Text may contain secrets; no safety/redaction claim is made.");
        }

    }

    private void ShowClipboardExplanationOutcome(ClipboardExplanationResult result)
    {
        if (disposed || !IsHostInputEligible) { return; }
        ShowInformation("Local clipboard explanation: " + result.Outcome,
            result.Outcome switch
            {
                ClipboardExplanationOutcome.ReviewRequired =>
                    "Review this exact snapshot in the native preview, then request policy-gated admission with Review and explain locally. "
                    + "This does not approve egress. A qualified local runtime is required; there is no hosted or bootstrap fallback. "
                    + "Production explanation is unavailable until the required clipboard secret/sensitive-content source-use policy is implemented.",
                ClipboardExplanationOutcome.Succeeded =>
                    "The admitted local response is available only in the exact native snapshot preview. It is not saved or spoken.",
                ClipboardExplanationOutcome.CredentialRejected =>
                    "Structural credential screening refused this text. Nothing was sent. Screening is best-effort, not a secret-free guarantee.",
                ClipboardExplanationOutcome.EnvelopeRejected =>
                    "The complete local context envelope was refused. Select a smaller source yourself; nothing was truncated or sent.",
                ClipboardExplanationOutcome.Expired =>
                    "This snapshot's explanation review expired. Revoke it and request a fresh snapshot explicitly; nothing was recaptured.",
                ClipboardExplanationOutcome.Stale =>
                    "The exact reviewed snapshot is no longer current. Nothing was recaptured or sent.",
                ClipboardExplanationOutcome.Cancelled =>
                    "Explanation was cancelled or source/host eligibility changed. No late answer is presented; provider release must finish.",
                ClipboardExplanationOutcome.Busy =>
                    "Local inference is still running or releasing resources. No retry or fallback is queued.",
                ClipboardExplanationOutcome.Denied =>
                    "Original user input, ownership, privacy or call policy does not admit this local explanation.",
                ClipboardExplanationOutcome.SourceUsePolicyUnavailable =>
                    "Explanation is unavailable: the required clipboard secret/sensitive-content source-use policy is not implemented. "
                    + "No model context was constructed or sent. Explicit consent and credential heuristics do not satisfy this rule.",
                ClipboardExplanationOutcome.SourceUseDenied =>
                    "The clipboard source-use policy refused this exact snapshot. No model context was constructed or sent.",
                _ => "Explanation is unavailable. No qualified local response was produced; no hosted or bootstrap fallback was used. "
                    + (result.CaptureOutcome is { } capture ? DescribeClipboardOutcome(capture) : "Model admission reason: " + result.ModelReason),
            });
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