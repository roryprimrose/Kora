using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class ExactAnswerExportWindowController(IExactAnswerExportWriter writer, ISecurityAuditLog audit,
    ILogger<ExactAnswerExportReview> logger, Func<Window, CancellationToken, Task<string?>> selectDestination)
{
    internal async Task<string> ShowAsync(Window owner, AdmittedDetailContent source, Func<bool> admitted,
        Func<CancellationToken, Task<AdmittedDetailContent>> resolve, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("The original export row is unavailable."); }
        var current = await resolve(token);
        if (!admitted() || !source.IsSameSnapshot(current))
        { throw new InvalidOperationException("The exact export source changed before destination selection."); }
        var path = await selectDestination(owner, token);
        token.ThrowIfCancellationRequested();
        if (path is null) { return "Destination selection cancelled. No answer file was written."; }
        if (!admitted()) { throw new InvalidOperationException("Export source/private admission changed during the picker."); }
        var destination = await writer.SelectAsync(path, token);
        var open = true;
        var shown = false;
        ExactAnswerExportWindow? view = null;
        bool Live() => admitted() && open && !token.IsCancellationRequested && view?.IsRetired != true;
        ExactAnswerExportReview created;
        try { created = new(source, destination, Live, resolve, audit, logger); }
        catch
        {
            await destination.DisposeAsync();
            throw;
        }
        await using var review = created;
        await review.ValidateAsync(token);
        view = new ExactAnswerExportWindow(review, Live, error => ReportFailure(logger, error), token);
        try
        {
            if (!Live()) { throw new InvalidOperationException("Export review admission changed before disclosure."); }
            shown = true;
            await view.ShowDialog(owner);
            open = false;
            await view.Operation;
            return view.Outcome is { } outcome ? ExactAnswerExportWindow.Describe(outcome, path)
                : "Export review cancelled. No answer file was published.";
        }
        finally
        {
            open = false;
            if (!shown) { view.Close(); }
            await view.Operation;
            view.Release();
        }
    }

    internal static async Task<string?> SelectDestinationAsync(Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var selected = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose a NEW file for this exact answer",
            SuggestedFileName = "answer.md",
            ShowOverwritePrompt = false,
            FileTypeChoices =
            [
                new("UTF-8 Markdown") { Patterns = ["*.md", "*.markdown"] },
                new("UTF-8 plain text") { Patterns = ["*.txt"] },
            ],
        });
        token.ThrowIfCancellationRequested();
        return selected is null ? null : selected.TryGetLocalPath()
            ?? throw new InvalidOperationException("Only a new local fixed-drive destination is supported.");
    }

    private static void ReportFailure(ILogger<ExactAnswerExportReview> logger, string error) =>
        ExactAnswerExportDiagnostics.Report(logger, error);
}
