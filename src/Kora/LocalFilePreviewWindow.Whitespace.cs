using System.ComponentModel;
using System.Diagnostics;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private NamedTextBlock? whitespaceStatus;
    private Func<bool>? whitespaceCurrent;
    private long whitespaceGeneration;
    private bool inspectingWhitespace;

    private void ShowWhitespace(DockPanel body, Func<bool> sourceCurrent)
    {
        var hostCurrent = captureWhitespaceAdmission?.Invoke()
            ?? typographyHost?.CaptureLocalFileInspectionEligibility() ?? (static () => false);
        var root = Content;
        var status = new NamedTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        whitespaceStatus = status;
        AutomationProperties.SetName(status, "Decoded current admitted text whitespace counts");
        bool Current() => ReferenceEquals(whitespaceStatus, status) && ReferenceEquals(Content, root)
            && IsVisible && IsEnabled && sourceCurrent() && hostCurrent();
        whitespaceCurrent = Current;
        var button = new Button { Content = "Inspect whitespace in this admitted text" };
        AutomationProperties.SetName(button, "Inspect whitespace in this admitted text");
        button.Click += (_, _) => InspectWhitespace(status, Current);
        var clear = new Button { Content = "Clear whitespace counts" };
        clear.Click += (_, _) =>
        {
            if (ReferenceEquals(whitespaceStatus, status)) { ClearWhitespace(); }
        };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(button);
        panel.Children.Add(clear);
        panel.Children.Add(new NamedTextBlock
        {
            Text = "Explicit decoded current text counts only: CRLF pairs, standalone LF, standalone CR, "
                + "TAB (U+0009), ordinary SPACE (U+0020). CRLF is not counted again as standalone CR or LF. "
                + "Nonbreaking spaces and other Unicode whitespace are not counted. "
                + "Not original encoding/bytes, model tokens, logical/visual/wrapped lines, formatting errors or a safety assessment. "
                + "No edits, conversion, source reads or model-use permission.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(status);
        DockPanel.SetDock(panel, Dock.Top);
        body.Children.Insert(1, panel);
        content.PropertyChanged += OnWhitespaceContentChanged;
        PropertyChanged += OnWhitespaceWindowChanged;
        if (typographyHost is not null) { typographyHost.PropertyChanged += OnWhitespaceHostChanged; }
    }

    private void InspectWhitespace(NamedTextBlock status, Func<bool> current)
    {
        if (!ReferenceEquals(status, whitespaceStatus)) { return; }
        ClearWhitespace();
        if (inspectingWhitespace) { return; }
        inspectingWhitespace = true;
        var generation = whitespaceGeneration;
        var file = findSource;
        var displayed = content;
        var text = displayed.Text;
        var owner = Owner;
        bool Current() => generation == whitespaceGeneration && current() && ReferenceEquals(content, displayed)
            && ReferenceEquals(findSource, file) && file is not null && ReferenceEquals(displayed.Text, text)
            && string.Equals(text, file.Text, StringComparison.Ordinal) && ReferenceEquals(Owner, owner)
            && generation == whitespaceGeneration;
        try
        {
            if (file is null || text is null || !Current())
            {
                status.Text = "Whitespace inspection unavailable; no counts claimed. Inspect the current source again.";
                return;
            }
            using var activity = BeginWhitespaceActivity(file);
            var counts = LocalFileWhitespaceInspection.Count(file);
            if (!Current()) { ClearWhitespace(); activity.Complete(HostOperationOutcome.Cancelled); return; }
            status.Text = $"Decoded current admitted text only: CRLF pairs: {counts.CrLfPairs}; "
                + $"standalone LF: {counts.StandaloneLf}; standalone CR: {counts.StandaloneCr}; "
                + $"TAB (U+0009): {counts.Tabs}; ordinary SPACE (U+0020): {counts.Spaces}.";
            if (!Current()) { ClearWhitespace(); activity.Complete(HostOperationOutcome.Cancelled); return; }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ClearWhitespace();
            if (ReferenceEquals(whitespaceStatus, status))
            {
                status.Text = "Whitespace inspection failed; no counts claimed. Close and inspect the current source again.";
            }
            using var failure = BeginWhitespaceActivity(file!);
            failure.Complete(HostOperationOutcome.Failed);
            WhitespaceInspectionFailed(logger ?? NullLogger<LocalFilePreviewWindow>.Instance, exception.GetType().Name);
        }
        finally { inspectingWhitespace = false; }
    }

    private static HostActivity BeginWhitespaceActivity(LocalFileRevision file)
    {
        var request = file.Review.Request;
        var cause = file.Review.Cause;
        ActivityLink[] links = cause.TraceId != default && cause.SpanId != default ? [new(cause)] : [];
        return HostActivity.BeginRoot(new(new(Guid.NewGuid()), request.SessionId, request.TaskId, RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Presentation, links);
    }

    private void OnWhitespaceContentChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == NamedTextBlock.TextProperty) { ClearWhitespace(); }
    }

    private void OnWhitespaceHostChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var status = whitespaceStatus;
            Interlocked.Increment(ref whitespaceGeneration);
            Dispatcher.UIThread.Post(() =>
            {
                if (status is not null) { status.Text = null; }
            });
            return;
        }
        if (whitespaceCurrent?.Invoke() != true) { ClearWhitespace(); }
    }

    private void OnWhitespaceWindowChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == ContentProperty || args.Property == IsVisibleProperty || args.Property == IsEnabledProperty)
        {
            ClearWhitespace();
        }
    }

    private void ClearWhitespace()
    {
        Interlocked.Increment(ref whitespaceGeneration);
        if (whitespaceStatus is not null) { whitespaceStatus.Text = null; }
    }

    private void RemoveWhitespace()
    {
        ClearWhitespace();
        content.PropertyChanged -= OnWhitespaceContentChanged;
        PropertyChanged -= OnWhitespaceWindowChanged;
        if (typographyHost is not null) { typographyHost.PropertyChanged -= OnWhitespaceHostChanged; }
        whitespaceStatus = null;
        whitespaceCurrent = null;
    }
}
