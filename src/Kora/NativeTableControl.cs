using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kora.Controls;

namespace Kora;

internal sealed class NativeTableControl : StackPanel
{
    private readonly NativeDocumentInteraction interaction;
    private readonly Grid grid = new() { ColumnSpacing = 12, RowSpacing = 8 };
    private readonly NamedTextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button reset = new() { Content = "Reset table order" };
    private readonly List<Button> headers = [];
    private readonly List<TextBlock> headerLabels = [];
    private readonly List<NamedTextBlock[]> rows = [];
    private readonly Dictionary<Control, CellPosition> positions = [];
    private NativeTableData? data;
    private int[] order;
    private int? sortColumn;
    private bool descending;
    private CellPosition? selectedCell;

    internal NativeTableControl(NativeTableData data, NativeDocumentInteraction interaction)
    {
        this.data = data;
        this.interaction = interaction;
        order = Enumerable.Range(0, data.Rows.Length).ToArray();
        Spacing = 8;
        Margin = new Thickness(0, 4, 0, 10);
        summary.FontSize = 14;
        summary.LineHeight = 14 * 1.55;
        for (var column = 0; column < data.Headers.Length; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var index = column;
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 14 * 1.55 };
            headerLabels.Add(label);
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Content = label };
            button.Click += (_, _) => Sort(index);
            headers.Add(button);
            positions.Add(button, new(null, column));
        }
        for (var row = 0; row <= data.Rows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        for (var row = 0; row < data.Rows.Length; row++)
        {
            var cells = new NamedTextBlock[data.Headers.Length];
            for (var column = 0; column < cells.Length; column++)
            {
                var position = new CellPosition(row, column);
                var cell = new NamedTextBlock
                {
                    Text = data.Rows[row][column].Length == 0 ? "(empty cell)" : data.Rows[row][column],
                    Focusable = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    LineHeight = 14 * 1.55,
                    ContextMenu = null,
                };
                cell.GotFocus += (_, _) => selectedCell = position;
                AutomationProperties.SetLabeledBy(cell, headers[column]);
                cells[column] = cell;
                positions.Add(cell, position);
            }
            rows.Add(cells);
        }
        reset.Click += (_, _) => Reset();
        AutomationProperties.SetName(reset, $"Reset table {data.Number} to original row order");
        Children.Add(summary);
        Children.Add(reset);
        Children.Add(grid);
        AddHandler(KeyDownEvent, OnNavigationKey, RoutingStrategies.Bubble);
        DetachedFromVisualTree += (_, _) => Retire();
        UpdateOrder();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    internal IEnumerable<TextBlock> Readers => headerLabels.Concat(rows.SelectMany(row => row)).Prepend(summary);

    private bool Admit()
    {
        if (data is not null && interaction.CanInteract) { return true; }
        interaction.Report("Table interaction unavailable: this exact document is closed or no longer accessible.");
        return false;
    }

    private void Sort(int column)
    {
        if (!Admit()) { return; }
        descending = sortColumn == column && !descending;
        sortColumn = column;
        var table = data!;
        var values = table.Rows;
        order = (descending
            ? Enumerable.Range(0, values.Length).OrderByDescending(row => values[row][column], StringComparer.Ordinal)
            : Enumerable.Range(0, values.Length).OrderBy(row => values[row][column], StringComparer.Ordinal))
            .ThenBy(row => row).ToArray();
        UpdateOrder();
        if (!Admit()) { return; }
        interaction.Report($"Table {table.Number} sorted by column {column + 1}, "
            + (descending ? "descending" : "ascending") + " ordinal text; equal values retain original order.");
    }

    private void Reset()
    {
        if (!Admit()) { return; }
        var table = data!;
        sortColumn = null;
        descending = false;
        order = Enumerable.Range(0, table.Rows.Length).ToArray();
        UpdateOrder();
        if (selectedCell is { } selected) { FocusCell(selected); }
        if (!Admit()) { return; }
        interaction.Report($"Table {table.Number} original row order restored; retained result unchanged.");
    }

    private void UpdateOrder()
    {
        var table = data!;
        var focused = positions.Keys.FirstOrDefault(control => control.IsFocused);
        var ordering = sortColumn is { } column
            ? $"Column {column + 1}, {(descending ? "descending" : "ascending")} ordinal text."
            : "Original row order.";
        summary.Text = $"Table {table.Number}: {table.Headers.Length} columns, {table.Rows.Length} data rows. "
            + ordering + " Arrow keys move between headers and cells; Home/End move across a row; "
            + "Ctrl+Home/End move to first/last row. Activate a header to sort; activate again to reverse. "
            + "Reset restores original order and the last focused cell. Read-only; no copy or editing.";
        AutomationProperties.SetName(this, $"Read-only table {table.Number}, {table.Headers.Length} columns, {table.Rows.Length} data rows");
        grid.Children.Clear();
        for (var col = 0; col < headers.Count; col++)
        {
            var header = table.Headers[col].Length == 0 ? "(empty header)" : table.Headers[col];
            var suffix = sortColumn == col ? descending ? "descending" : "ascending" : "unsorted";
            headerLabels[col].Text = $"{header} ({suffix})";
            AutomationProperties.SetName(headers[col], $"Table {table.Number}, column {col + 1}: {header}; {suffix}; sort ordinal text");
            Grid.SetColumn(headers[col], col);
            grid.Children.Add(headers[col]);
        }
        for (var index = 0; index < order.Length; index++)
        {
            var sourceRow = order[index];
            for (var col = 0; col < headers.Count; col++)
            {
                var cell = rows[sourceRow][col];
                var header = table.Headers[col].Length == 0 ? "empty header" : table.Headers[col];
                AutomationProperties.SetName(cell,
                    $"Table {table.Number}, row {index + 1}, original row {sourceRow + 1}, column {col + 1} ({header})");
                Grid.SetRow(cell, index + 1);
                Grid.SetColumn(cell, col);
                grid.Children.Add(cell);
            }
        }
        if (focused is not null) { FocusCell(positions[focused]); }
    }

    private void OnNavigationKey(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Control)
            || args.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))
        { return; }
        var source = args.Source as Control;
        var owner = source is not null && positions.ContainsKey(source) ? source
            : source?.GetVisualAncestors().OfType<Control>().FirstOrDefault(positions.ContainsKey);
        if (owner is null || !positions.TryGetValue(owner, out var position)) { return; }
        args.Handled = true;
        if (!Admit()) { return; }
        var row = position.SourceRow is { } sourceRow ? Array.IndexOf(order, sourceRow) + 1 : 0;
        var column = position.Column;
        switch (args.Key)
        {
            case Key.Left: column = Math.Max(0, column - 1); break;
            case Key.Right: column = Math.Min(headers.Count - 1, column + 1); break;
            case Key.Up: row = Math.Max(0, row - 1); break;
            case Key.Down: row = Math.Min(order.Length, row + 1); break;
            case Key.Home:
                column = 0;
                if (args.KeyModifiers == KeyModifiers.Control) { row = 0; }
                break;
            case Key.End:
                column = headers.Count - 1;
                if (args.KeyModifiers == KeyModifiers.Control) { row = order.Length; }
                break;
        }
        FocusCell(new(row == 0 ? null : order[row - 1], column));
    }

    private void FocusCell(CellPosition position)
    {
        if (!Admit()) { return; }
        Control target = position.SourceRow is { } row ? rows[row][position.Column] : headers[position.Column];
        if (TopLevel.GetTopLevel(target) is null || !target.IsEffectivelyVisible)
        {
            interaction.Report("Table cell focus unavailable in the current rendered view.");
            return;
        }
        target.BringIntoView();
        if (!Admit()) { return; }
        var focused = target.Focus();
        if (!Admit()) { return; }
        if (!focused) { interaction.Report("Table cell focus unavailable; no focus claimed."); }
    }

    internal void Retire()
    {
        data = null;
        selectedCell = null;
        order = [];
        foreach (var header in headers)
        {
            header.Content = null;
            header.IsEnabled = false;
            AutomationProperties.SetName(header, string.Empty);
        }
        foreach (var cell in rows.SelectMany(row => row))
        {
            cell.Text = string.Empty;
            AutomationProperties.SetName(cell, string.Empty);
            cell.ClearValue(AutomationProperties.LabeledByProperty);
        }
        positions.Clear();
        foreach (var label in headerLabels) { label.Text = string.Empty; }
        headerLabels.Clear();
        headers.Clear();
        rows.Clear();
        grid.Children.Clear();
        summary.Text = string.Empty;
        reset.IsEnabled = false;
        AutomationProperties.SetName(this, string.Empty);
        IsEnabled = false;
    }

    private sealed record CellPosition(int? SourceRow, int Column);
}
