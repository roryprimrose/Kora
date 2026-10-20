using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private Func<ClipboardSnapshot, CancellationToken, ClipboardBase64Inspection>? inspectBase64;
    private NamedTextBlock? base64Status;
    private CancellationTokenSource? base64Cancellation;
    private long base64Generation;

    public void SetBase64Inspection(Func<ClipboardSnapshot, CancellationToken, ClipboardBase64Inspection> inspect) =>
        inspectBase64 = inspect;

    private void AddBase64(StackPanel chrome, ClipboardSnapshot snapshot, Func<bool> isCurrent)
    {
        var generation = ++base64Generation;
        base64Cancellation = new CancellationTokenSource();
        var token = base64Cancellation.Token;
        base64Status = new NamedTextBlock { TextWrapping = TextWrapping.Wrap, Focusable = true };
        AutomationProperties.SetName(base64Status, "Native standard Base64 syntax and exact decoded byte count only");
        var status = base64Status;
        var button = new Button { Content = "Inspect this snapshot as standard Base64" };
        AutomationProperties.SetName(button, "Inspect this snapshot as standard Base64");
        button.Command = new AsyncCommand(() =>
        {
            status.Text = null;
            if (generation != base64Generation) { return Task.CompletedTask; }
            if (!isCurrent()) { ClearAndClose(); return Task.CompletedTask; }
            try
            {
                var inspect = inspectBase64 ?? throw new InvalidOperationException("Native Base64 inspection is unavailable.");
                var result = inspect(snapshot, token);
                if (generation != base64Generation) { return Task.CompletedTask; }
                if (!isCurrent()) { ClearAndClose(); return Task.CompletedTask; }
                status.Text = result switch
                {
                    ClipboardBase64Inspection.AdmittedProfile admitted =>
                        $"AdmittedProfile: canonical standard Base64 syntax; exact decoded byte count: {admitted.DecodedByteCount}. No bytes decoded.",
                    ClipboardBase64Inspection.InvalidProfile invalid =>
                        $"InvalidProfile: {invalid.Reason}; UTF-16 offset {invalid.Utf16Offset} (zero-based; end position permitted). No decoded count.",
                    _ => throw new InvalidOperationException("Unknown native Base64 inspection outcome."),
                };
            }
            catch (OperationCanceledException) { status.Text = null; }
            return Task.CompletedTask;
        }, exception =>
        {
            status.Text = null;
            if (generation != base64Generation) { return; }
            if (!isCurrent()) { ClearAndClose(); return; }
            status.Text = "Base64 inspection unavailable. No profile or decoded count is claimed; start a new explicit request.";
            reportFailure(exception);
        });
        chrome.Children.Add(button);
        chrome.Children.Add(new NamedTextBlock
        {
            Text = "Syntax and size only, not a decoder/export or security tool. Standard RFC 4648 alphabet and canonical padding only; "
                + "not Base64URL. No whitespace, prefixes, repair or format guessing. Original encoded text is unchanged and may contain secrets. "
                + "Admitted does not mean safe, trusted, secret-free, encrypted, signature/schema verified, or approved for any use or egress.",
            TextWrapping = TextWrapping.Wrap,
        });
        chrome.Children.Add(status);
    }

    private void ClearBase64State()
    {
        ++base64Generation;
        base64Cancellation?.Cancel();
        base64Cancellation?.Dispose();
        base64Cancellation = null;
        if (base64Status is not null) { base64Status.Text = null; }
        base64Status = null;
        inspectBase64 = null;
    }
}
