using Kora.Application.Dependencies;
using Kora.Application.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private ModelHandoffWindowController? handoffWindow;
    private Action? showVerifiedHandoff;

    private void BindHandoffPresentation(MainViewModel main)
    {
        BindClaudePresentation(main);
        handoffWindow = new(main, Services.GetRequiredService<ModelHandoffPresentation>(),
            Services.GetRequiredService<ILogger<ModelHandoffWindowController>>());
        showVerifiedHandoff = () => Avalonia.Threading.Dispatcher.UIThread.Post(main.ReviewPendingHandoff);
        Services.GetRequiredService<VerifiedLocalHandoffOperation>().ReviewRequested += showVerifiedHandoff;
    }

    private void DisposeHandoffPresentation()
    {
        DisposeClaudePresentation();
        if (showVerifiedHandoff is not null)
        {
            Services.GetRequiredService<VerifiedLocalHandoffOperation>().ReviewRequested -= showVerifiedHandoff;
            showVerifiedHandoff = null;
        }
        handoffWindow?.Dispose();
        handoffWindow = null;
    }
}
