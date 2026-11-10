using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

using Kora.Core.Artifacts;

namespace Kora;

internal sealed class SessionInstructionScopeView : UserControl, IDisposable
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime presentation;
    private bool disposed;
    internal bool IsNativePresented => presentation.IsPresented;
    internal void RefreshPresentation() => presentation.Refresh();
    internal Func<bool> CapturePresentation() => presentation.Capture();
    internal event EventHandler? PresentationLost;
    internal event EventHandler? Closed;
    internal void Close() => Dispose();
    internal ComboBox Sources { get; } = new() { Name = "ScopeSource", MinWidth = 300 };
    internal Button Refresh { get; } = new() { Name = "ScopeRefresh", Content = "Refresh enabled instructions" };
    internal Button Review { get; } = new() { Name = "ScopeReview", Content = "Review complete source" };
    internal Button Confirm { get; } = new() { Name = "ScopeConfirm", Content = "Confirm apply to this session", IsEnabled = false };
    internal Button Stop { get; } = new() { Name = "ScopeStop", Content = "Stop applying" };
    internal Button Status { get; } = new() { Name = "ScopeStatus", Content = "Refresh scope status" };
    internal TextBox Preview { get; } = new()
    {
        Name = "ScopePreview", IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 240,
        VerticalContentAlignment = VerticalAlignment.Top,
    };
    internal SessionInstructionScopeView(Window owner, Func<bool> privateEligible)
    {
        Owner = owner;
        presentation = new(this, owner, () => !disposed && privateEligible());
        presentation.PresentationChanged += (_, _) =>
        {
            if (!presentation.IsPresented) { PresentationLost?.Invoke(this, EventArgs.Empty); }
        };
        owner.Closed += OnOwnerClosed;
        Sources.ItemTemplate = new FuncDataTemplate<ArtifactDefinition>((item, _) => new TextBlock { Text = item?.Name });
        AutomationProperties.SetName(Sources, "Enabled owned instruction by readable name; selection is not consent");
        AutomationProperties.SetName(Preview, "Complete pinned source, exact target, ordinary local scope and lifetime");
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { Refresh, Sources, Review, Confirm, Stop, Status } };
        Content = new Grid
        {
            Margin = new Thickness(14), RowDefinitions = RowDefinitions.Parse("Auto,*"),
            Children = { buttons, Preview },
        };
        Grid.SetRow(Preview, 1);
    }

    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        Owner.Closed -= OnOwnerClosed;
        presentation.Dispose();
        Preview.Text = string.Empty;
        Sources.ItemsSource = null;
        Closed?.Invoke(this, EventArgs.Empty);
        if (Owner is ResponseWindow primary) { primary.RemoveNativeReview(this); }
        else if (Owner is SessionInstructionScopeWindow) { Owner.Close(); }
    }
}
