using System.Security.Cryptography;

using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

public sealed class ProfileInstructionForkWorkflow(
    IProfileInstructionReader reader, InstructionControlAdmission admission,
    OwnedInstructionEditorActions actions, ISecurityAuditLog auditLog)
{
    public async Task<InstructionWriteResult> Fork(string selectedPath, string destinationName,
        IProfileInstructionCopyReview review, Func<bool> eligible, CancellationToken token,
        Func<OwnedInstructionSnapshot, bool>? reconcile = null)
    {
        OwnedInstructionNames.WriteName(destinationName);
        IProfileInstructionSelection? selection = null;
        try
        {
            var snapshot = await admission.Run(eligible, async (request, session, current, admitted) =>
            {
                if (request.Origin != RequestOrigin.LocalUi || actions.Kind != ArtifactKind.Instruction)
                { throw new InvalidOperationException("Profile instruction fork requires a fresh native local gesture and the Instruction kind."); }
                await actions.RequireNewProfileCopyDestination(destinationName, admitted).ConfigureAwait(false);
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                selection = await reader.OpenSelection(selectedPath, admitted).ConfigureAwait(false);
                var sourceId = selection.Id;
                var metadata = selection.Metadata;
                void RequireSelected()
                {
                    admitted.ThrowIfCancellationRequested();
                    if (selection.Id != sourceId || selection.Metadata != metadata)
                    { throw new InvalidDataException("The selected instruction source changed after read consent review."); }
                }
                if (metadata.ByteLength is <= 0 or > ArtifactDefinition.MaximumContentLength)
                { throw new InvalidDataException("A native instruction must contain between 1 and 65536 bytes."); }
                if (!await review.ConfirmRead(metadata, admitted).ConfigureAwait(false)) { return null; }
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                RequireSelected();
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "definition.instruction.profile-read", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
                    "definition.profile-instruction");
                auditLog.Write(audit);
                byte[]? bytes = null;
                try
                {
                    bytes = await selection.Read(admitted).ConfigureAwait(false);
                    RequireSelected();
                    var captured = ProfileInstructionSnapshot.Parse(sourceId, metadata, bytes);
                    await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded, "exact-source-captured"));
                    return captured;
                }
                catch (Exception exception)
                {
                    auditLog.Write(audit.WithOutcome(exception is OperationCanceledException
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, "source-capture-unconfirmed"));
                    throw;
                }
                finally { if (bytes is not null) { CryptographicOperations.ZeroMemory(bytes); } }
            }, token).ConfigureAwait(false);
            if (snapshot is null) { return new(false, false, false, null); }
            var copy = actions.PrepareProfileCopy(destinationName, snapshot);
            var result = await actions.SaveProfileCopy(copy, selection!, review, eligible, reconcile, token).ConfigureAwait(false);
            if (result.Conflict)
            { throw new InvalidOperationException("The copy destination is occupied. Inspect it or choose another new owned ID; no overwrite or automatic retry."); }
            return result;
        }
        finally { selection?.Dispose(); }
    }
}
