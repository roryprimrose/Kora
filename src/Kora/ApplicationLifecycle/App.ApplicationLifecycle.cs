using Kora.Application.Hosting;
using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class App
{
    private ApplicationLifecycleController? applicationLifecycle;

    private void BindApplicationLifecycle(MainViewModel main, NativeQuestionHost questions)
    {
        applicationLifecycle = new(main, Services.GetRequiredService<SessionConversationService>(),
            Services.GetRequiredService<SessionQueueService>(), questions,
            Services.GetRequiredService<HostTaskCoordinator>(), Services.GetRequiredService<NativeDetailRenderer>(),
            GetPresenceOwner);
    }
}
