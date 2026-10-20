using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Network;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private Func<CancellationToken, WebUrlSyntaxResult>? inspectWebUrl;
    private NamedTextBlock? webUrlResult;
    private CancellationTokenSource? webUrlCancellation;
    private long webUrlGeneration;
    private long webUrlOperation;
    private bool webUrlRunning;

    public void BindWebUrlInspection(Func<CancellationToken, WebUrlSyntaxResult> inspect) => inspectWebUrl = inspect;

    private void AddWebUrlInspection(Avalonia.Controls.StackPanel chrome, Func<bool> isCurrent)
    {
        if (inspectWebUrl is not { } inspect) { return; }
        var generation = webUrlGeneration;
        var result = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        webUrlResult = result;
        AutomationProperties.SetName(result, "Local web URL syntax facts; not a verified destination");
        var button = new Button { Content = "Inspect this snapshot as a web URL (no fetch)" };
        AutomationProperties.SetName(button, "Inspect this snapshot as a web URL (no fetch)");
        button.Command = new AsyncCommand(() =>
        {
            if (generation != webUrlGeneration) { return Task.CompletedTask; }
            var operation = ++webUrlOperation;
            result.Text = null;
            if (webUrlRunning) { return Task.CompletedTask; }
            if (!isCurrent()) { ClearAndClose(); return Task.CompletedTask; }
            using var cancellation = new CancellationTokenSource();
            webUrlCancellation = cancellation;
            webUrlRunning = true;
            try
            {
                var inspected = inspect(cancellation.Token);
                if (generation != webUrlGeneration || operation != webUrlOperation || cancellation.IsCancellationRequested)
                { return Task.CompletedTask; }
                if (!isCurrent()) { ClearAndClose(); return Task.CompletedTask; }
                result.Text = DescribeWebUrl(inspected);
                if (generation != webUrlGeneration || !isCurrent()) { result.Text = null; }
            }
            finally
            {
                webUrlCancellation = null;
                webUrlRunning = false;
            }
            return Task.CompletedTask;
        }, _ =>
        {
            // Never forward parser/gate exception messages or values into generic host responses.
            if (generation == webUrlGeneration && isCurrent())
            {
                result.Text = "Local URL syntax inspection failed unexpectedly. No result is available; close and make a new explicit request.";
            }
        });
        chrome.Children.Add(button);
        chrome.Children.Add(result);
    }

    private static string DescribeWebUrl(WebUrlSyntaxResult result) => result.Outcome == WebUrlSyntaxOutcome.Accepted && result.Facts is { } facts
        ? "Syntax accepted only. Scheme: " + facts.Scheme
            + "; parser-canonical ASCII/IDN host (not verified destination): " + facts.CanonicalAsciiHost
            + "; effective port: " + facts.EffectivePort.ToString(CultureInfo.InvariantCulture)
            + "; query present: " + facts.QueryPresent + "; fragment present: " + facts.FragmentPresent
            + ". No fetch, DNS, certificate, redirect, IDN-confusable or security screening. "
            + "Not a safe, trusted, allowed, resolved, public or reachable destination claim."
        : result.Outcome switch
        {
            WebUrlSyntaxOutcome.OutsideBounds => "URL syntax refused: the whole snapshot must fit the 2048 UTF-8 byte URL limit. Nothing was trimmed or truncated.",
            WebUrlSyntaxOutcome.InvalidSyntax => "URL syntax refused: requires one exact absolute HTTP/HTTPS URL without whitespace, controls, format characters, backslashes or credentials.",
            WebUrlSyntaxOutcome.Failed => "Local URL syntax inspection failed unexpectedly. No result is available; close and make a new explicit request.",
            _ => "URL inspection cancelled, busy or no longer current. No derived result is available.",
        };

    private void ClearWebUrlInspection()
    {
        ++webUrlGeneration;
        webUrlCancellation?.Cancel();
        if (webUrlResult is not null) { webUrlResult.Text = null; }
        webUrlResult = null;
        inspectWebUrl = null;
    }
}
