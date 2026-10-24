using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private IAttachmentCataloguePreparation? attachmentCataloguePreparation;
    private long attachmentCatalogueBindingRevision;
    private long attachmentCatalogueComposerRevision;

    public void BindAttachmentCataloguePreparation(IAttachmentCataloguePreparation? presenter)
    {
        attachmentCataloguePreparation = presenter;
        Interlocked.Increment(ref attachmentCatalogueBindingRevision);
    }

    private Func<bool> CaptureCatalogueVoiceEligibility(AttachmentCataloguePreparationCommand command)
    {
        var name = assistantNameConfiguration.Get().Revision;
        var voiceOwner = Interlocked.Read(ref voiceOwnershipBindingRevision);
        var binding = Interlocked.Read(ref attachmentCatalogueBindingRevision);
        var composer = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        var owner = settingsOwnershipRevision();
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var inputRevision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        Func<bool> lifetime = static () => true;
        try
        {
            if (command.SessionId is not null && attachmentCataloguePreparation is { } presenter)
            { lifetime = presenter.CaptureLifetime(); }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Capturing attachment catalogue native lifetime");
            return static () => false;
        }
        return () => assistantNameConfiguration.Get().Revision == name
            && Interlocked.Read(ref voiceOwnershipBindingRevision) == voiceOwner
            && Interlocked.Read(ref attachmentCatalogueBindingRevision) == binding
            && Interlocked.Read(ref attachmentCatalogueComposerRevision) == composer
            && settingsOwnershipRevision() == owner
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == ownerBinding
            && Interlocked.Read(ref workspaceSurfaceInputRevision) == inputRevision + 1 && lifetime();
    }

    private async Task PrepareAttachmentCatalogue(AttachmentCataloguePreparationCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || !isAssistantNameAvailable || origin == RequestOrigin.ActivatedVoice && !command.IsActivated)
        {
            Transcript = "Attachment catalogue preparation unavailable: use original typed input or current-name activated voice.";
            return;
        }
        if (command.IsHelp)
        {
            Transcript = AttachmentCataloguePreparationCommand.Syntax;
            return;
        }
        if (command.SessionId is not { } id)
        {
            Transcript = "Attachment catalogue preparation refused. " + AttachmentCataloguePreparationCommand.Syntax;
            return;
        }
        var presenter = attachmentCataloguePreparation;
        if (presenter is null)
        {
            Transcript = "Attachment catalogue preparation unavailable: the native presenter is not bound.";
            return;
        }
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var owner = settingsOwnershipRevision();
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var binding = Interlocked.Read(ref attachmentCatalogueBindingRevision);
        var composer = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var name = assistantNameConfiguration.Get().Revision;
        var initiallyAdmitted = input.NavigationInitiallyAdmitted && input.CatalogueEligible() && input.Eligible();
        bool Admitted() => initiallyAdmitted && input.Origin == origin && input.CatalogueEligible() && input.Eligible()
            && input.Revision == CallPolicyRevision && !IsProtectedCall && CanRevealPrivatePresentation
            && (origin != RequestOrigin.ActivatedVoice || voiceOwnershipEligible())
            && settingsOwnershipRevision() == owner
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == ownerBinding
            && Interlocked.Read(ref attachmentCatalogueBindingRevision) == binding
            && Interlocked.Read(ref attachmentCatalogueComposerRevision) == composer
            && Interlocked.Read(ref workspaceSurfaceInputRevision) == revision
            && assistantNameConfiguration.Get().Revision == name;
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        try
        {
            var lifetime = presenter.CaptureLifetime();
            await uiDispatcher.InvokeAsync(() =>
            {
                using var activity = continuation();
                if (!Admitted() || !lifetime())
                {
                    Transcript = "Attachment catalogue preparation unavailable: the original host or native lifetime was revoked.";
                    activity.Complete(HostOperationOutcome.Cancelled);
                    return Task.CompletedTask;
                }
                try
                {
                    presenter.Prepare(id, () => Admitted() && lifetime());
                    if (!Admitted() || !lifetime())
                    { throw new InvalidOperationException("Original catalogue preparation admission was revoked."); }
                    Transcript = "Attachment catalogue PREPARED / REVIEW NEEDED. No I/O or search ran. "
                        + "Other filter drafts remain AND-composed. Check all visible filters, then explicitly Search. "
                        + "The ID is future metadata query data, not session selection, ownership, read authority or proof a retained copy exists.";
                    activity.Complete(HostOperationOutcome.Completed);
                }
                catch
                {
                    activity.Complete(HostOperationOutcome.Failed);
                    throw;
                }
                return Task.CompletedTask;
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Preparing the native attachment metadata catalogue");
            Transcript = "Attachment catalogue preparation unavailable: native focus/preparation was not acknowledged. No search or read was authorized.";
        }
    }
}
