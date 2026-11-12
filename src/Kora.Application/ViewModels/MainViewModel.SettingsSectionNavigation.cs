using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;

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
            || origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(original))
        {
            Transcript = "Settings navigation unavailable: use original typed input or the current assistant-name voice prefix.";
            return;
        }
        if (command.Section is null && command.Filter is null)
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
        try
        {
            await NavigateSettingsAsync(presenter, command.Section, command.Filter, Admitted, CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Navigating the native Settings section");
            Transcript = "Settings navigation unavailable: the native view did not confirm navigation. Retry from the owning unlocked host.";
        }
    }

    public Task ShowSettingFilterAsync(string query, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        if (!SettingsNavigationTool.IsValidQuery(query)) { throw new ArgumentException("Invalid settings search filter.", nameof(query)); }
        var presenter = settingsSectionNavigation
            ?? throw new InvalidOperationException("The native Settings presenter is not bound.");
        var ownerRevision = settingsOwnershipRevision();
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var callRevision = CallPolicyRevision;
        var nameRevision = assistantNameConfiguration.Get().Revision;
        bool Current() => admitted() && !token.IsCancellationRequested && CanRevealPrivatePresentation
            && settingsOwnershipRevision() == ownerRevision
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == ownerBinding
            && CallPolicyRevision == callRevision && assistantNameConfiguration.Get().Revision == nameRevision
            && ReferenceEquals(presenter, settingsSectionNavigation);
        return NavigateSettingsAsync(presenter, null, query.Trim(), Current, token);
    }

    private async Task NavigateSettingsAsync(ISettingsSectionNavigation presenter, SettingsSection? section,
        string? query, Func<bool> admitted, CancellationToken token)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        var nativeLifetime = presenter.CaptureLifetime();
        await uiDispatcher.InvokeAsync(() =>
        {
            using var activity = continuation();
            if (token.IsCancellationRequested)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                token.ThrowIfCancellationRequested();
            }
            if (!admitted() || !nativeLifetime())
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                throw new OperationCanceledException("Settings navigation lost its original host or native lifetime.");
            }
            try
            {
                if (query is not null) { presenter.ShowFilter(query, admitted); }
                else { presenter.Navigate(section!.Value, admitted); }
                if (!admitted()) { throw new InvalidOperationException("Settings navigation admission changed."); }
                Transcript = query is null
                    ? "Settings section opened. Navigation only; no setting or permission was changed."
                    : "Settings search opened. Navigation only; no setting or permission was changed.";
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
}
