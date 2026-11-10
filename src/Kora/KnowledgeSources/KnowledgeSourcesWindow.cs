using Avalonia.Controls;
using Kora.Application.Configuration;
using Kora.Application.KnowledgeSources;
using Kora.Application.ViewModels;
using Kora.Core.Context;
using Kora.Core.KnowledgeSources;

namespace Kora.KnowledgeSources;

internal sealed class KnowledgeSourcesWindow : Window, IDisposable
{
    internal KnowledgeSourcesView View { get; }
    internal KnowledgeSourcesWindow(MainViewModel host, KnowledgeSourceManager manager, IUserFolderPicker? picker = null,
        KnowledgeSourceAnswerWorkflow? answers = null, KnowledgeSourceCaptureLimitsConfigurationService? captureLimits = null)
    {
        Title = "Knowledge sources - device-local copies";
        Width = 840; Height = 780;
        View = new(this, host, manager, picker, answers, captureLimits);
        Content = View;
    }

    internal Task StopAsync() => View.StopAsync();
    internal Task OpenAnswerAsync(KnowledgeSourceAnswerCommand command, Func<bool> current) => View.OpenAnswerAsync(command, current);
    internal Task OpenCaptureLimitsAsync(KnowledgeSourceCaptureLimitsCommand command, Func<bool> eligible) =>
        View.OpenCaptureLimitsAsync(command, eligible);
    public void Dispose() => View.Dispose();
}
