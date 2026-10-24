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
    private ClaudeTransmissionWindowController? claudeTransmissionWindow;

    private void BindClaudePresentation(MainViewModel main)
    {
        claudeAccountWindow = new(main, Services.GetRequiredService<ClaudeAccountBroker>(),
            Services.GetRequiredService<AudioControlAdmission>(),
            Services.GetRequiredService<ILogger<ClaudeAccountWindowController>>());
        claudeTransmissionWindow = new(main, Services.GetRequiredService<HostedTransmissionReviewService>(),
            Services.GetRequiredService<ILogger<ClaudeTransmissionWindowController>>());
    }

    private void DisposeClaudePresentation()
    {
        claudeTransmissionWindow?.Dispose();
        claudeTransmissionWindow = null;
        claudeAccountWindow?.Dispose();
        claudeAccountWindow = null;
    }
}
