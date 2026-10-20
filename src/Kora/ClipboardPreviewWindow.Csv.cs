using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private Func<CancellationToken, ClipboardCsvResult>? csvInspect;
    private NamedTextBlock? csvStatus;
    private CancellationTokenSource? csvCancellation;
    private long csvGeneration;

    public void BindCsvInspection(Func<CancellationToken, ClipboardCsvResult>? inspect) => csvInspect = inspect;

    private NamedTextBlock AddCsvInspection(StackPanel chrome, Func<bool> isCurrent)
    {
        var generation = csvGeneration;
        var status = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        csvStatus = status;
        AutomationProperties.SetName(status, "Native comma-separated syntax and row shape result");
        var button = new Button
        {
            Content = "Inspect this snapshot as comma-separated rows",
            IsEnabled = csvInspect is not null,
        };
        AutomationProperties.SetName(button, "Inspect this snapshot as comma-separated rows");
        ToolTip.SetTip(button, "Complete existing snapshot only, at most 256 KiB UTF-8. Comma delimiter; double quotes and doubled-quote escapes; "
            + "CRLF or LF records; literal spaces. No dialect, header or type guessing. Syntax does not make text spreadsheet-safe.");
        button.Command = new AsyncCommand(() =>
        {
            if (generation != csvGeneration || csvCancellation is not null) { return Task.CompletedTask; }
            if (!isCurrent()) { ClearAndClose(); return Task.CompletedTask; }
            status.Text = null;
            var inspect = csvInspect;
            if (inspect is null)
            {
                status.Text = "Native comma-separated inspection is unavailable.";
                return Task.CompletedTask;
            }
            using var cancellation = new CancellationTokenSource();
            csvCancellation = cancellation;
            try
            {
                var result = inspect(cancellation.Token);
                if (generation != csvGeneration || cancellation.IsCancellationRequested) { return Task.CompletedTask; }
                if (!isCurrent()) { ClearAndClose(); return Task.CompletedTask; }
                status.Text = DescribeCsv(result);
            }
            finally
            {
                if (ReferenceEquals(csvCancellation, cancellation)) { csvCancellation = null; }
            }
            return Task.CompletedTask;
        }, _ =>
        {
            // The production workflow logs a fixed diagnostic and returns Unavailable.
            // Never forward an arbitrary exception message from a native command.
            if (generation == csvGeneration)
            { status.Text = "Native comma-separated inspection failed internally. No syntax or shape success is claimed."; }
        });
        chrome.Children.Add(button);
        return status;
    }

    private static string DescribeCsv(ClipboardCsvResult result)
    {
        if (result.Outcome != ClipboardCsvOutcome.Inspected || result.Inspection is not { } inspection)
        {
            return result.Outcome == ClipboardCsvOutcome.Unavailable
                ? "Native comma-separated inspection failed internally. No syntax or shape success is claimed."
                : "Inspection cancelled or exact source unavailable. No result is retained; start a new explicit inspection.";
        }
        const string disclosure = " Syntax does not make text spreadsheet-safe; formula-like text remains inert literal content. "
            + "No header, names, types, trust or safety inferred. No formatting, export, copy or model use.";
        if (inspection.Syntax == CommaSeparatedSyntax.Empty)
        { return "Empty input: 0 records, no field range. Profile: " + CommaSeparatedProfile.Name + "." + disclosure; }
        if (inspection.Syntax != CommaSeparatedSyntax.Admitted)
        {
            return $"Profile mismatch: {inspection.Syntax}; UTF-16 offset {inspection.ErrorOffset}, logical record {inspection.ErrorRecord}. "
                + "Complete-source inspection refused; no partial counts." + disclosure;
        }
        return $"Syntactically admitted profile: {CommaSeparatedProfile.Name}. Records: {inspection.RecordCount}; "
            + $"fields per record: {inspection.MinimumFields} to {inspection.MaximumFields}; "
            + (inspection.IsRagged ? "ragged shape (not a schema violation)." : "uniform shape (not a schema).") + disclosure;
    }

    private void ClearCsvInspection()
    {
        csvGeneration++;
        csvCancellation?.Cancel();
        csvInspect = null;
        if (csvStatus is not null) { csvStatus.Text = null; }
        csvStatus = null;
    }
}
