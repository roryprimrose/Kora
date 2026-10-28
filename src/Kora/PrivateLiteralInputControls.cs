using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed class PrivateLiteralInputControls : StackPanel
{
    internal Dictionary<string, TextBox> Inputs { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Button> SpeakInputs { get; } = new(StringComparer.Ordinal);

    internal void Configure(IEnumerable<string> names, Action<string, string> edit,
        Action<string>? speak = null, Action? cancelVoice = null)
    {
        Clear();
        foreach (var name in names)
        {
            var input = new TextBox
            {
                AcceptsReturn = true, MaxLength = OwnedPromptTemplate.MaximumValueLength, MinHeight = 42,
                IsUndoEnabled = false, ContextMenu = null, ContextFlyout = null,
            };
            AutomationProperties.SetName(input, "Required private literal input " + name);
            Inputs.Add(name, input);
            Children.Add(new TextBlock
            {
                Text = name + " (required; 512 UTF-16 / 2048 UTF-8 maximum)", TextWrapping = TextWrapping.Wrap,
            });
            Children.Add(input);
            var voice = new Button { Content = "Speak input " + name, IsEnabled = speak is not null };
            SpeakInputs.Add(name, voice);
            voice.Click += (_, _) => speak?.Invoke(name);
            voice.LostFocus += (_, _) => cancelVoice?.Invoke();
            Children.Add(voice);
            input.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty) { edit(name, input.Text ?? string.Empty); }
            };
        }
    }

    internal IReadOnlyDictionary<string, string> Capture() =>
        Inputs.ToDictionary(pair => pair.Key, pair => pair.Value.Text ?? string.Empty, StringComparer.Ordinal);

    internal void Clear()
    {
        foreach (var input in Inputs.Values) { input.Text = string.Empty; }
        Children.Clear();
        Inputs.Clear();
        SpeakInputs.Clear();
    }
}
