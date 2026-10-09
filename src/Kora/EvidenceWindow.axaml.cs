using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class EvidenceWindow : Window
{
    internal EvidenceWindow(EvidenceViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Search.Click += async (_, _) => await model.SearchAsync();
        Next.Click += async (_, _) => await model.NextAsync();
        ClearAdvanced.Click += (_, _) => model.ClearAdvancedFilters();
        Trace.Click += async (_, _) => await model.ReadTraceAsync();
        OpenSegment.Click += async (_, _) =>
        {
            if (Segments.SelectedItem is EvidenceSegment segment) { await model.NavigateAsync(segment); }
        };
        Records.SelectionChanged += (_, _) => model.Select(Records.SelectedItem as EvidenceRecord);
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Close();
        foreach (var input in this.GetLogicalDescendants().OfType<TextBox>())
        {
            input.CopyingToClipboard += (_, args) => args.Handled = true;
            input.CuttingToClipboard += (_, args) => args.Handled = true;
        }
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is Key.C or Key.X or Key.Insert
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.Delete)
            {
                args.Handled = true;
                return;
            }
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
}
