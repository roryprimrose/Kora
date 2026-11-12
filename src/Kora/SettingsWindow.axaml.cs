using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.ViewModels;
using Kora.Application.Presentation;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsWindow : Window
{
    internal SettingsView View { get; }
    internal T? FindControl<T>(string name) where T : Control => View.FindControl<T>(name);

    public SettingsWindow() : this(App.Services.GetRequiredService<MainViewModel>()) { }

    public SettingsWindow(MainViewModel viewModel, Action? chooseMicrophone = null,
        SpeechCaptionWindowController? captionDisplay = null)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        View = new(viewModel, this, chooseMicrophone, captionDisplay);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }

    public void ShowReadiness() => View.ShowReadiness();
    public void ShowVoiceRecovery() => View.ShowVoiceRecovery();
    public void ShowSection(SettingsSection section) => View.ShowSection(section);
    public void ShowFilter(string query) => View.ShowFilter(query);
    internal void BindNavigation(Action? instructions, Action? skills, Action? grants,
        Func<Task>? reviewVersion, Func<Task>? returnVersion) =>
        View.BindNavigation(instructions, skills, grants, reviewVersion, returnVersion);

    internal Task OpenKnowledgeSourceAnswerAsync(Kora.Application.KnowledgeSources.KnowledgeSourceAnswerCommand command,
        Func<bool> eligible) => View.OpenKnowledgeSourceAnswerAsync(command, eligible);
    internal Task OpenKnowledgeSourceCaptureLimitsAsync(
        Kora.Application.Configuration.KnowledgeSourceCaptureLimitsCommand command, Func<bool> eligible) =>
        View.OpenKnowledgeSourceCaptureLimitsAsync(command, eligible);
}
