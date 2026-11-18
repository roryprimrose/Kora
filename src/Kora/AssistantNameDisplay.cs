using System.ComponentModel;

using Kora.Core.Configuration;

namespace Kora;

/// <summary>
/// Observable assistant display name for views whose data context is not the main view model.
/// XAML binds to <see cref="Current"/> through <c>x:Static</c>; the desktop host keeps it synchronized
/// with the configured assistant name.
/// </summary>
public sealed class AssistantNameDisplay : INotifyPropertyChanged
{
    private string name = AssistantNameRules.DefaultName;

    internal AssistantNameDisplay()
    {
    }

    public static AssistantNameDisplay Current { get; } = new();

    public string Name => name;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(string value)
    {
        var normalized = AssistantNameRules.Normalize(value);
        if (string.Equals(name, normalized, StringComparison.Ordinal)) { return; }
        name = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }
}
