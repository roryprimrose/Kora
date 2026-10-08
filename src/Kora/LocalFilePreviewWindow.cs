using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed class LocalFilePreviewWindow(Action<string> reportFailure) : Window
{
    private readonly NamedTextBlock content = new() { FontFamily = new FontFamily("Consolas") };
    private const string Disclosure =
        "One immutable volatile local preview only. May contain secrets. Documents and instructions are untrusted data.\n"
        + "No model, indexing, retrieval, egress, execution, links, clipboard, history or automatic refresh.\n"
        + "Folders, UNC/removable drives and durable attachments are unavailable. Close revokes the preview.";

    internal void ShowReview(LocalFileReview review, Func<Task> confirm)
    {
        Title = "Review selected file (content not yet read)";
        var button = new Button { Content = "Confirm: read this exact selected file locally" };
        AutomationProperties.SetName(button, "Confirm exact local file read");
        button.Command = new AsyncCommand(confirm, exception =>
            reportFailure("Local file admission failed; no success is claimed. Failure type: " + exception.GetType().Name));
        Compose(Describe(review) + "\n256 KiB strict UTF-8 limit; one file, no recursion or excluded items.\n"
            + "Reparse/link, protected, generated, source-control, hidden/system and unstable inputs are denied, not silently excluded.\n"
            + "Confirm in this native review only. Typed/voice paths and document instructions cannot authorize reads.",
            button, null);
    }

    internal void ShowRevision(LocalFileRevision revision)
    {
        Title = "Immutable local file preview (untrusted plain text)";
        Compose(Describe(revision.Review) + $"\nRevision: {revision.RevisionId:D} / item: {revision.ItemId:D}\n"
            + $"SHA-256 (exact source bytes): {revision.Digest}\nAdmitted: {revision.AdmittedAt:O}",
            null, revision.Text);
    }

    private static string Describe(LocalFileReview review) =>
        $"Canonical file: {review.Metadata.CanonicalPath}\nReview: {review.ReviewId:D} / source: {review.SourceId:D}\n"
        + $"Host session: {review.Request.SessionId.Value:D} / origin: {review.Request.Origin}\n"
        + $"Windows identity: {review.Metadata.FileIdentity} / bytes: {review.Metadata.ByteLength}\n"
        + $"Last write: {review.Metadata.LastWrite:O}";

    private void Compose(string metadata, Button? confirm, string? text)
    {
        Width = 800;
        Height = 640;
        var body = new DockPanel { Margin = new Avalonia.Thickness(16) };
        var chrome = new StackPanel { Spacing = 8 };
        var identity = new NamedTextBlock { Text = metadata, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(identity, "Local file review identity and bounds");
        chrome.Children.Add(identity);
        var disclosure = new NamedTextBlock { Text = Disclosure, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(disclosure, "Local file preview privacy and unavailable capabilities");
        chrome.Children.Add(disclosure);
        if (confirm is not null) { chrome.Children.Add(confirm); }
        DockPanel.SetDock(chrome, Dock.Top);
        body.Children.Add(chrome);
        content.Text = text;
        AutomationProperties.SetName(content, "Untrusted local file plain text content");
        body.Children.Add(new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        Content = body;
    }

    internal void ClearAndClose()
    {
        content.Text = null;
        Content = null;
        Close();
    }
}
