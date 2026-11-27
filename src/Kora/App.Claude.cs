using Kora.Application.Dependencies;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private ClaudeAccountWindowController? claudeAccountWindow;

    private void BindClaudePresentation(MainViewModel main)
    {
        claudeAccountWindow = new(main, Services.GetRequiredService<ClaudeAccountBroker>(),
            Services.GetRequiredService<AudioControlAdmission>(),
            Services.GetRequiredService<ILogger<ClaudeAccountWindowController>>());
        Avalonia.Controls.Window Owner() => (responseWindow
            ?? throw new InvalidOperationException("Private Claude presentation requires the owning workbench.")).PrepareWorkbenchOwner();
        claudeAccountWindow.BindWorkbench(Owner);
    }

    private void DisposeClaudePresentation()
    {
        claudeAccountWindow?.Dispose();
        claudeAccountWindow = null;
    }
}
