using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Kora;

internal sealed class SessionDictationButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    internal SessionDictationWindow? DictationWindow { get; private set; }

    public SessionDictationButton()
    {
        Content = "Dictate session message...";
        AutomationProperties.SetName(this, "Open explicit dictation for the selected session's memory-only unsent message");
        Click += (_, _) =>
        {
            if (DataContext is not SessionsViewModel { CanCompose: true } model || TopLevel.GetTopLevel(this) is not Window owner) { return; }
            if (DictationWindow is { IsVisible: true } existing) { existing.Activate(); return; }
            var window = new SessionDictationWindow(model);
            DictationWindow = window;
            void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == IsVisibleProperty && !owner.IsVisible) { window.Close(); }
            }
            void OwnerClosed(object? sender, EventArgs args) => window.Close();
            owner.PropertyChanged += OwnerChanged;
            owner.Closed += OwnerClosed;
            window.Closed += (_, _) =>
            {
                owner.PropertyChanged -= OwnerChanged;
                owner.Closed -= OwnerClosed;
                if (ReferenceEquals(DictationWindow, window)) { DictationWindow = null; }
            };
            window.Show(owner);
        };
    }
}
