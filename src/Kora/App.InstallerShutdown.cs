using Avalonia.Controls.ApplicationLifetimes;

using Kora.Application;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Coordination;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Coordination;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class App
{
    private static void BindInstallerShutdown(MainViewModel host, IUiDispatcher dispatcher,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var coordinator = Services.GetRequiredService<IInstanceLifecycleController>() as WindowsInstanceCoordinator
            ?? throw new InvalidOperationException("The Windows installer lifecycle owner is unavailable.");
        coordinator.BindInstallerShutdown(async cancellationToken =>
        {
            var closeRequested = false;
            await dispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                void ObserveClose(object? sender, WindowAction action)
                {
                    if (action == WindowAction.Close) { closeRequested = true; }
                }
                host.WindowActionRequested += ObserveClose;
                try { await host.ExitAsync(); }
                finally { host.WindowActionRequested -= ObserveClose; }
            }, HostActivityLayer.Desktop, HostOperation.Recovery));
            return closeRequested;
        });
        desktop.Exit += (_, _) => coordinator.BindInstallerShutdown(null);
    }
}
