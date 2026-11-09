using Avalonia.Controls;

using Kora.Application.Definitions;
using Kora.Application.ViewModels;
using Kora.Core.Customizations;
using Kora.Core.Mcp;

namespace Kora;

internal sealed class CustomizationWindow : Window
{
    internal CustomizationView View { get; }
    internal ListBox Kinds => View.Kinds;
    internal TextBox Search => View.Search;
    internal Button ClearFilter => View.ClearFilter;
    internal TextBlock FilterSummary => View.FilterSummary;
    internal ComboBox Destination => View.Destination;
    internal TextBox NewName => View.NewName;
    internal ListBox Files => View.Files;
    internal TextBox FileName => View.FileName;
    internal TextBox Editor => View.Editor;
    internal TextBox Request => View.Request;
    internal TextBox RequiredPromptInputs => View.RequiredPromptInputs;
    internal TextBlock Status => View.Status;
    internal TextBlock Details => View.Details;
    internal TextBlock VoiceContext => View.VoiceContext;
    internal Button PushToTalkButton => View.PushToTalkButton;
    internal TextBox McpUrl => View.McpUrl;
    internal CheckBox McpHttpType => View.McpHttpType;
    internal ComboBox CapabilityCategory => View.CapabilityCategory;
    internal TextBlock CapabilityCount => View.CapabilityCount;
    internal CustomizationSource? SelectedSource => View.SelectedSource;
    internal McpCapabilityRow? SelectedCapability => View.SelectedCapability;
    internal McpCapabilityCategory? SelectedCapabilityCategory => View.SelectedCapabilityCategory;
    internal CustomizationKind Kind { get => View.Kind; set => View.Kind = value; }
    internal CustomizationScope Scope => View.Scope;

    internal CustomizationWindow(Action<string> run, MainViewModel main)
    {
        Title = "Kora Customizations";
        Width = 1240;
        Height = 840;
        MinWidth = 900;
        MinHeight = 600;
        View = new(run, main, this);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }

    internal void SetSource(CustomizationSource? source, bool recovery, bool originalRegistered = true) =>
        View.SetSource(source, recovery, originalRegistered);
    internal void ShowSources(IReadOnlyList<CustomizationSource> sources, Func<CustomizationSource, bool> disabled,
        Action<CustomizationSource> select) => View.ShowSources(sources, disabled, select);
    internal void SetVoiceCaptureAvailable(bool value) => View.SetVoiceCaptureAvailable(value);
    internal void SetCapabilityMetadata(IReadOnlyList<McpDisplayedServer> servers, Action manage) =>
        View.SetCapabilityMetadata(servers, manage);
    internal Task<string> Review(string title, string text, CancellationToken token, params string[] choices) =>
        View.Review(title, text, token, choices);
    internal Task<CustomizationVoiceChoice> ReviewClarification(string text,
        CustomizationVoiceClarification question, CancellationToken token) =>
        View.ReviewClarification(text, question, token);
    internal void CancelReview() => View.CancelReview();
    internal void ClearPrivateText() => View.ClearPrivateText();
}
