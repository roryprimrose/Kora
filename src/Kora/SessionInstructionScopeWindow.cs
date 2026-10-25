using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

using Kora.Core.Artifacts;

namespace Kora;

internal sealed class SessionInstructionScopeWindow : Window
{
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
    internal SessionInstructionScopeWindow()
    {
        Title = "Instruction for this Active session";
        Width = 780; Height = 680; MinWidth = 480; MinHeight = 400;
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
}
