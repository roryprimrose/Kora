using Avalonia.Controls;
using Kora.Application.Infrastructure;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.KnowledgeSources;
using Kora.Core.Dependencies;
using Kora.KnowledgeSources;
using Kora.Windows.KnowledgeSources;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsWindow
{
    private KnowledgeSourcesWindow? knowledgeSources;

    private void BindKnowledgeSources()
    {
        var button = this.FindControl<Button>("ManageKnowledgeSources")
            ?? throw new InvalidOperationException("Knowledge source navigation is unavailable.");
        button.Command = new AsyncCommand(() =>
        {
            if (!model.CanRevealPrivatePresentation || model.IsProtectedCall || !IsVisible)
            { throw new InvalidOperationException("Private native knowledge source management is unavailable."); }
            if (knowledgeSources is null)
            {
                var store = new WindowsKnowledgeSourceStore(App.Services.GetRequiredService<IApplicationDataPaths>());
                var manager = new KnowledgeSourceManager(App.Services.GetRequiredService<IKnowledgeSourceInspector>(),
                    store, App.Services.GetRequiredService<ISecurityAuditLog>(), TimeProvider.System);
                var window = new KnowledgeSourcesWindow(model, manager);
                knowledgeSources = window;
                window.Closed += (_, _) => knowledgeSources = null;
                window.Show(this);
            }
            knowledgeSources.Activate();
            return Task.CompletedTask;
        }, exception => model.ReportHostInteractionFailure(
            "Knowledge source management unavailable. Failure type: " + exception.GetType().Name));
        Closing += (_, args) =>
        {
            knowledgeSources?.Close();
            if (knowledgeSources is { IsVisible: true }) { args.Cancel = true; }
        };
    }
}
