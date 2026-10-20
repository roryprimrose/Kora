using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISettingsSectionNavigation? settingsSectionNavigation;
    private Func<long> settingsOwnershipRevision = static () => 0;
    private long settingsOwnershipBindingRevision;

    public void BindSettingsSectionNavigation(ISettingsSectionNavigation? navigation) =>
        settingsSectionNavigation = navigation;

    private async Task ExecuteSettingsSectionCommand(SettingsSectionCommand command, string original,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || !isAssistantNameAvailable
            || origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(original, AssistantName))
        {
            Transcript = "Settings navigation unavailable: use original typed input or the current assistant-name voice prefix.";
            return;
        }
        if (command.Section is not { } section)
        {
            Transcript = "Clarify the Settings section. " + SettingsSectionCommand.Syntax;
            return;
        }
        var presenter = settingsSectionNavigation;
        if (presenter is null)
        {
            Transcript = "Settings navigation unavailable: the native Settings presenter is not bound.";
            return;
        }
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var ownerRevision = settingsOwnershipRevision();
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        bool Admitted() => input.Origin == origin && input.Eligible()
            && CanRevealPrivatePresentation && settingsOwnershipRevision() == ownerRevision
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == ownerBinding
            && assistantNameConfiguration.Get().Revision == nameRevision
            && ReferenceEquals(presenter, settingsSectionNavigation);
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        try
        {
            var nativeLifetime = presenter.CaptureLifetime();
            await uiDispatcher.InvokeAsync(() =>
            {
                using var activity = continuation();
                if (!Admitted() || !nativeLifetime())
                {
                    Transcript = "Settings navigation unavailable: the original host or native lifetime was revoked.";
                    activity.Complete(HostOperationOutcome.Cancelled);
                    return Task.CompletedTask;
                }
                try
                {
                    presenter.Navigate(section, Admitted);
                    if (!Admitted()) { throw new InvalidOperationException("Settings navigation admission changed."); }
                    Transcript = "Settings section opened. Navigation only; no setting or permission was changed.";
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
            ApplicationLog.Error(logger, exception, "Navigating the native Settings section");
            Transcript = "Settings navigation unavailable: the native view did not confirm navigation. Retry from the owning unlocked host.";
        }
    }
}
