using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class EvidencePageExportWindowController(IEvidencePageExportWriter writer, ISecurityAuditLog audit,
    ILogger<EvidencePageExportReview> logger, Func<Window, CancellationToken, Task<string?>> selectDestination)
{
    internal async Task<(EvidencePageExportOutcome Outcome, string Message)> ShowAsync(
        Window owner, EvidencePageExportSource source, Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("The original evidence page/private presentation is unavailable."); }
        await source.ValidateAsync(token);
        var path = await selectDestination(owner, token);
        token.ThrowIfCancellationRequested();
        if (path is null) { return (EvidencePageExportOutcome.Cancelled, "Destination selection cancelled. No evidence file was written."); }
        if (!admitted()) { throw new InvalidOperationException("Evidence admission retired during destination selection."); }
        var destination = await writer.SelectAsync(path, token);
        EvidencePageExportWindow? view = null;
        var open = true;
        bool Live() => open && admitted() && !token.IsCancellationRequested && view?.IsRetired != true;
        EvidencePageExportReview created;
        try { created = new(source, destination, Live, audit, logger); }
        catch
        {
            await destination.DisposeAsync();
            throw;
        }
        await using var review = created;
        await review.ValidateAsync(token);
        view = new(review, Live, error => EvidencePageExportDiagnostics.Report(logger, error), token);
        try
        {
            if (!Live()) { throw new InvalidOperationException("Evidence admission retired before export disclosure."); }
            await view.ShowDialog(owner);
            open = false;
            await view.Operation;
            return view.Outcome is { } outcome ? (outcome, EvidencePageExportWindow.Describe(outcome, path))
                : (EvidencePageExportOutcome.Cancelled, "Evidence export review cancelled. No file was published.");
        }
        finally
        {
            open = false;
            view.Close();
            await view.Operation;
            view.Release();
        }
    }

    internal static async Task<string?> SelectDestinationAsync(Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var selected = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose a NEW file for this exact evidence page",
            SuggestedFileName = "evidence-page.json",
            ShowOverwritePrompt = false,
            FileTypeChoices = [new("UTF-8 JSON") { Patterns = ["*.json"] }],
        });
        token.ThrowIfCancellationRequested();
        return selected is null ? null : selected.TryGetLocalPath()
            ?? throw new InvalidOperationException("Only a new local fixed-drive evidence destination is supported.");
    }
}
