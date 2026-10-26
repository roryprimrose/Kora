using Kora.Application.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public partial class App
{
    private SessionRoutingController? sessionRouting;

    private void BindSessionRouting(Kora.Application.ViewModels.MainViewModel main)
    {
        sessionRouting = new(main, Services.GetRequiredService<SessionWorkspaceService>(),
            Services.GetRequiredService<SessionConversationService>(), Services.GetRequiredService<ISessionWorkspaceAccess>(),
            Services.GetRequiredService<TimeProvider>());
        sessionRouting.Bind();
    }
}
