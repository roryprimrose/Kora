using Avalonia.Automation;
using Avalonia.Controls;

namespace Kora;

internal sealed partial class NativeTableControl
{
    private readonly Button inspect = new() { Content = "Inspect focused row", IsEnabled = false };
    private readonly Button returnToCell = new() { Content = "Return to inspected cell", IsVisible = false };
    private readonly TableReader inspection;
    private CellPosition? inspectedCell;

    private void InitializeInspection()
    {
        inspect.Click += (_, _) =>
        {
            if (!Admit()) { return; }
            if (selectedCell is not { } selected)
            {
                interaction.Report("Focus a data cell before inspecting its row.");
                return;
            }
            InspectRow(selected);
        };
        returnToCell.Click += (_, _) =>
        {
            if (!Admit()) { return; }
            if (inspectedCell is not { } selected) { return; }
            ClearInspection();
            FocusCell(selected);
        };
        Children.Add(inspect);
        Children.Add(inspection);
        Children.Add(returnToCell);
    }

    private void InspectRow(CellPosition selected)
    {
        if (!Admit()) { return; }
        inspectedCell = selected;
        UpdateInspection();
        FocusReader(inspection);
    }

    private void UpdateInspection()
    {
        if (inspectedCell?.SourceRow is not { } row) { return; }
        if (!Admit()) { return; }
        var table = data!;
        var values = table.Rows[row];
        var cells = table.Headers.Select((header, column) =>
            $"Column {column + 1} ({(header.Length == 0 ? "empty header" : header)}): "
            + (values[column].Length == 0 ? "(empty cell)" : values[column]));
        inspection.Text = $"Table {table.Number}, row {Array.IndexOf(order, row) + 1}, original row {row + 1}."
            + Environment.NewLine + string.Join(Environment.NewLine, cells);
        if (!Admit()) { return; }
        inspection.IsVisible = true;
        if (!Admit()) { return; }
        returnToCell.IsVisible = true;
        AutomationProperties.SetName(inspection, "Inspected table row");
        _ = Admit();
    }

    private void ClearInspection()
    {
        inspectedCell = null;
        inspection.Text = string.Empty;
        AutomationProperties.SetName(inspection, string.Empty);
        inspection.IsVisible = false;
        returnToCell.IsVisible = false;
    }
}
