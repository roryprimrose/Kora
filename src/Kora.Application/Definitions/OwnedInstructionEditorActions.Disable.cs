using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

public sealed partial class OwnedInstructionEditorActions
{
    public bool IsReviewedUseEnabled(OwnedInstructionSnapshot reviewed) =>
        reviewed.Document.Definition.Kind == Kind && revisions.IsEnabled(reviewed)
        && catalogue.Candidates.Any(item => Matches(item, reviewed.Name)
            && item.IsUseEnabled && string.Equals(item.OwnedInstructionRevision, reviewed.Revision, StringComparison.Ordinal));

    public async Task<InstructionDisableResult> Disable(OwnedInstructionSnapshot reviewed,
        Func<CancellationToken, Task<bool>> exactConfirmation, Func<bool> eligible, CancellationToken token)
    {
        reviewed.Document.RequireEditableInstruction();
        OwnedInstructionNames.WriteName(reviewed.Name);
        if (!reviewed.Document.Definition.RequiresInstructionReview || reviewed.Document.Definition.Kind != Kind
            || !string.Equals(reviewed.CanonicalFileName, reviewed.Name + OwnedDefinitionKind.Extension(Kind), StringComparison.Ordinal))
        { throw new InvalidDataException("Disable requires the exact canonical owned native v1 source of the selected kind."); }
        var epoch = revisions.CaptureReviewEpoch(reviewed);
        var attempted = false;
        var observed = false;
        var controlReceipt = Guid.Empty;
        try
        {
            return await admission.Run(eligible, async (request, session, current, admitted) =>
            {
                controlReceipt = request.RequestId.Value;
                var identity = $"{Kind}\n{reviewed.Document.Definition.Id}\n{reviewed.Revision}";
                var target = "definition.owned-use." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    Kind == ArtifactKind.Instruction ? "definition.instruction.disable-use" : "definition.prompt.disable-use",
                    SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                        ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, target);
                var confirmed = false;
                try
                {
                    auditLog.Write(audit);
                    if (!await exactConfirmation(admitted).ConfigureAwait(false))
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "exact-disable-cancelled"));
                        return new InstructionDisableResult(reviewed, request.RequestId.Value, Cancelled: true, WasEnabled: false);
                    }
                    confirmed = true;
                    var live = await store.Read(reviewed.Name, admitted).ConfigureAwait(false);
                    if (live is null || !string.Equals(live.Revision, reviewed.Revision, StringComparison.Ordinal)
                        || revisions.CaptureReviewEpoch(reviewed) != epoch)
                    { throw new InvalidOperationException("The exact disable review is stale. Reload and review the current disk revision; nothing was retargeted."); }
                    var wasEnabled = await admission.Publish(request, session, current, () =>
                    {
                        if (revisions.CaptureReviewEpoch(reviewed) != epoch)
                        { throw new InvalidOperationException("The exact use review was retired. Begin a fresh review."); }
                        attempted = true;
                        var enabled = revisions.DisableExact(live, catalogue);
                        observed = true;
                        return enabled;
                    }, admitted).ConfigureAwait(false);
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded,
                        wasEnabled ? "exact-revision-disabled" : "exact-revision-already-held"));
                    return new InstructionDisableResult(reviewed, request.RequestId.Value, Cancelled: false, WasEnabled: wasEnabled);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // A failed mandatory audit or loss of a confirmed source fails closed. Never
                    // retarget the old receipt to a replacement, or restore a prior enablement.
                    if (confirmed || exception is not OperationCanceledException)
                    {
                        if (catalogue.Candidates.Any(item => Matches(item, reviewed.Name)
                            && string.Equals(item.OwnedInstructionRevision, reviewed.Revision, StringComparison.Ordinal)))
                        {
                            attempted = true;
                            try
                            {
                                revisions.HoldExact(reviewed, catalogue);
                                observed = true;
                            }
                            catch (Exception holdFailure) when (holdFailure is not OutOfMemoryException)
                            { exception = new AggregateException(exception, holdFailure); }
                        }
                    }
                    auditLog.Write(audit.WithOutcome(attempted ? SecurityAuditOutcome.Unknown
                        : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                        attempted ? "exact-use-held-unconfirmed" : "disable-not-published"));
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (attempted && exception is not OutOfMemoryException)
        {
            throw new InstructionUseRevocationUnconfirmedException(controlReceipt, observed, exception);
        }
    }
}
