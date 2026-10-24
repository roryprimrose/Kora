using Kora.Application.Hosting;
using Kora.Application.Dependencies;
using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private InstructionUseController? instructionUse;

    private void BindInstructionUse(MainViewModel main)
    {
        instructionUse = new(main, new(Services.GetRequiredService<ScopedInstructionUseBroker>(),
            Services.GetRequiredService<SessionWorkspaceService>(), Services.GetRequiredService<SessionConversationService>(),
            Services.GetRequiredService<IModelTurnExecution>(), Services.GetRequiredService<TimeProvider>()),
            () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
            () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision,
            Services.GetRequiredService<ILogger<InstructionUseController>>());
        instructionUse.Bind();
    }
}
