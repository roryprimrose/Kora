using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Conversation.SessionRouting;
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
            Services.GetRequiredService<TimeProvider>(), Services.GetRequiredService<ISessionRelatednessMatcher>(),
            Services.GetRequiredService<SessionRoutingControlAdmission>(),
            () => sessionsWindow?.RetainRoutingPresentation());
        sessionRouting.Bind();
    }
}
