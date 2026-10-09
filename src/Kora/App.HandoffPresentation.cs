using Kora.Application.Dependencies;
using Kora.Application.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private ModelHandoffWindowController? handoffWindow;

    private void BindHandoffPresentation(MainViewModel main)
    {
        handoffWindow = new(main, Services.GetRequiredService<ModelHandoffPresentation>(),
            Services.GetRequiredService<ILogger<ModelHandoffWindowController>>());
    }

    private void DisposeHandoffPresentation()
    {
        handoffWindow?.Dispose();
        handoffWindow = null;
    }
}
