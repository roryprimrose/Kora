using Avalonia.Controls;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.KnowledgeSources;
using Kora.Core.Dependencies;
using Kora.KnowledgeSources;
using Kora.Windows.KnowledgeSources;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsView
{
    private KnowledgeSourcesView? knowledgeSources;

    private void BindKnowledgeSources()
    {
        var button = this.FindControl<Button>("ManageKnowledgeSources")
            ?? throw new InvalidOperationException("Knowledge source navigation is unavailable.");
        button.Command = new AsyncCommand(() =>
        {
            if (!model.CanRevealPrivatePresentation || model.IsProtectedCall || !IsNativePresented)
            { throw new InvalidOperationException("Private native knowledge source management is unavailable."); }
            OpenKnowledgeSources().Activate();
            return Task.CompletedTask;
        }, exception => model.ReportHostInteractionFailure(
            "Knowledge source management unavailable. Failure type: " + exception.GetType().Name));
    }

    private KnowledgeSourcesView OpenKnowledgeSources()
    {
        if (knowledgeSources is not null) { return knowledgeSources; }
        var limitsPreferences = new LocalKnowledgeSourceCaptureLimitsPreferences(App.Services.GetRequiredService<IApplicationDataPaths>());
        var store = new WindowsKnowledgeSourceStore(App.Services.GetRequiredService<IApplicationDataPaths>(), limitsPreferences);
        var manager = new KnowledgeSourceManager(App.Services.GetRequiredService<IKnowledgeSourceInspector>(),
            store, App.Services.GetRequiredService<ISecurityAuditLog>(), TimeProvider.System, limitsPreferences);
        var limits = new KnowledgeSourceCaptureLimitsConfigurationService(store, limitsPreferences,
            App.Services.GetRequiredService<ISecurityAuditLog>(), TimeProvider.System,
            App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KnowledgeSourceCaptureLimitsConfigurationService>>());
        var workflow = ActivatorUtilities.CreateInstance<KnowledgeSourceAnswerWorkflow>(App.Services, manager);
        var window = owner is ResponseWindow ? new KnowledgeSourcesView(owner, model, manager, answers: workflow, captureLimits: limits)
            : new KnowledgeSourcesWindow(model, manager, answers: workflow, captureLimits: limits).View;
        knowledgeSources = window;
        window.Closed += (_, _) => knowledgeSources = null;
        if (owner is ResponseWindow) { window.Show(); }
        else { window.Owner.Show(owner); }
        return window;
    }

    internal Task OpenKnowledgeSourceAnswerAsync(KnowledgeSourceAnswerCommand command, Func<bool> eligible) =>
        OpenKnowledgeSources().OpenAnswerAsync(command, eligible);

    internal Task OpenKnowledgeSourceCaptureLimitsAsync(KnowledgeSourceCaptureLimitsCommand command, Func<bool> eligible) =>
        OpenKnowledgeSources().OpenCaptureLimitsAsync(command, eligible);
}
