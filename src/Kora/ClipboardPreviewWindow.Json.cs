using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private NamedTextBlock? jsonStatus;
    private Button? jsonButton;
    private CancellationTokenSource? jsonCancellation;
    private long jsonGeneration;

    private void AddJsonValidation(StackPanel chrome, Func<bool> isCurrent, Func<bool> isJsonCurrent,
        Func<CancellationToken, ClipboardJsonSyntaxResult> validate)
    {
        var generation = jsonGeneration;
        jsonCancellation = new();
        var token = jsonCancellation.Token;
        var status = new NamedTextBlock { Focusable = true, TextWrapping = TextWrapping.Wrap };
        jsonStatus = status;
        AutomationProperties.SetName(status, "Clipboard JSON syntax outcome only; original snapshot remains untrusted inert text");
        var button = new Button { Content = "Validate this snapshot as JSON" };
        jsonButton = button;
        AutomationProperties.SetName(button, "Validate this exact clipboard snapshot as JSON syntax only");
        bool Current()
        {
            if (generation != jsonGeneration) { return false; }
            if (!isCurrent()) { ClearAndClose(); return false; }
            if (!token.IsCancellationRequested && isJsonCurrent()) { return generation == jsonGeneration; }
            ClearJsonState();
            return false;
        }
        button.Command = new AsyncCommand(() =>
        {
            if (!Current()) { return Task.CompletedTask; }
            status.Text = null;
            var result = validate(token);
            if (!Current()) { return Task.CompletedTask; }
            status.Text = result.Outcome switch
            {
                ClipboardJsonSyntaxOutcome.Valid => $"Syntactically valid JSON. Root kind: {result.RootKind}.",
                ClipboardJsonSyntaxOutcome.InvalidSyntax =>
                    $"Invalid JSON syntax. Zero-based line: {result.LineNumber}; byte position in line: {result.BytePositionInLine}.",
                ClipboardJsonSyntaxOutcome.DepthLimit =>
                    $"JSON validation refused: nesting exceeds {ClipboardJsonSyntax.MaximumDepth}. Zero-based line: {result.LineNumber}; byte position in line: {result.BytePositionInLine}.",
                ClipboardJsonSyntaxOutcome.Cancelled => "JSON syntax validation cancelled. No result retained.",
                ClipboardJsonSyntaxOutcome.Unavailable => "Exact snapshot admission expired or is unavailable. Request a new preview explicitly.",
                _ => "JSON syntax validation failed internally. No syntax claim; close and explicitly retry the preview.",
            };
            UpdateLayout();
            if (!Current()) { return Task.CompletedTask; }
            status.BringIntoView();
            if (!Current()) { return Task.CompletedTask; }
            if (!status.Focus()) { throw new InvalidOperationException("Native JSON syntax outcome focus is unavailable."); }
            _ = Current();
            return Task.CompletedTask;
        }, _ =>
        {
            if (!Current()) { return; }
            status.Text = "Native JSON syntax validation unavailable. No syntax claim; close and explicitly retry the preview.";
            reportFailure(new InvalidOperationException("Native JSON syntax validation presentation failed; no source content is reported."));
        });
        chrome.Children.Add(button);
        chrome.Children.Add(new NamedTextBlock
        {
            Text = "Syntax only: one complete JSON value; strict comments/trailing-comma rules; maximum nesting 64. "
                + "Duplicate property names are not checked. Valid does not mean safe, trusted, secret-free, schema-approved or approved for model use/egress. "
                + "No formatting, source changes, clipboard access or review renewal.",
            TextWrapping = TextWrapping.Wrap,
        });
        chrome.Children.Add(status);
    }

    private void ClearJsonState()
    {
        ++jsonGeneration;
        var cancellation = jsonCancellation;
        jsonCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (jsonStatus is not null) { jsonStatus.Text = null; }
        jsonStatus = null;
        if (jsonButton is not null) { jsonButton.IsEnabled = false; }
        jsonButton = null;
    }

    public void ClearJsonValidation() => ClearJsonState();
}
