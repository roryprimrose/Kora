using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Kora.Application.Presentation;
using Kora.Core.Diagnostics;
using Kora.Core.Presentation;

namespace Kora;

internal sealed class ExactAnswerExportWindow : Window
{
    private readonly TextBox source;
    private readonly Button confirm;
    private readonly TextBlock status;
    private readonly CancellationTokenSource lifetime;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal ExactAnswerExportOutcome? Outcome { get; private set; }
    internal bool IsRetired => lifetime.IsCancellationRequested;

    internal ExactAnswerExportWindow(ExactAnswerExportReview review, Func<bool> admitted,
        Action<string> reportFailure, CancellationToken token)
    {
        Title = "Export one exact retained ASSISTANT answer";
        Width = 820;
        Height = 720;
        MinWidth = 480;
        MinHeight = 400;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        source = new TextBox
        {
            Name = "ExportAnswerSource", Text = review.Source.Source, IsReadOnly = true,
            AcceptsReturn = true, IsUndoEnabled = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        source.CopyingToClipboard += (_, args) => args.Handled = true;
        source.CuttingToClipboard += (_, args) => args.Handled = true;
        source.PastingFromClipboard += (_, args) => args.Handled = true;
        source.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        status = new TextBlock { Name = "ExportStatus", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        confirm = new Button { Name = "ConfirmExactAnswerExport", Content = "Confirm this one new-file export" };
        var cancel = new Button { Name = "CancelExactAnswerExport", Content = "Cancel / Close" };
        var chrome = new StackPanel { Spacing = 8 };
        chrome.Children.Add(new TextBlock
        {
            Text = "Source conversation: " + review.Source.ConversationMessage!.Request.SessionId.Value.ToString("D")
                + "\nASSISTANT result: " + review.Source.Reference.ItemId.Value.ToString("D")
                + "\nNew destination: " + review.Destination + "\nFormat: " + review.Format,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        chrome.Children.Add(new TextBlock { Text = ExactAnswerExportReview.RetentionDisclosure,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } };
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 10 };
        layout.Children.Add(chrome);
        Grid.SetRow(source, 1); layout.Children.Add(source);
        Grid.SetRow(status, 2); layout.Children.Add(status);
        Grid.SetRow(actions, 3); layout.Children.Add(actions);
        Content = layout;
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Storage);
        confirm.Click += async (_, _) =>
        {
            if (!confirm.IsEnabled) { return; }
            confirm.IsEnabled = false;
            Operation = Confirm();
            await Operation;
        };
        cancel.Click += (_, _) => Close();
        timer.Tick += (_, _) =>
        {
            if (lifetime.IsCancellationRequested || !admitted()) { Close(); }
        };
        Closed += (_, _) => { timer.Stop(); ClearPrivateText(); lifetime.Cancel(); };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            { ClearPrivateText(); lifetime.Cancel(); }
        };
        Opened += (_, _) => { source.Focus(); timer.Start(); };

        async Task Confirm()
        {
            using var activity = continuation();
            try
            {
                Outcome = await review.ConfirmAsync(lifetime.Token);
                source.Text = string.Empty;
                status.Text = !IsRetired && admitted() ? Describe(Outcome.Value, review.Destination) : string.Empty;
                activity.Complete(Outcome == ExactAnswerExportOutcome.Saved
                    ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                reportFailure(exception.GetType().Name);
                status.Text = !IsRetired && admitted()
                    ? "Export unconfirmed. No automatic retry or rollback is claimed. Inspect the selected destination before any fresh review."
                    : string.Empty;
                activity.Complete(HostOperationOutcome.Failed);
            }
        }

        void ClearPrivateText()
        {
            source.Text = string.Empty;
            status.Text = string.Empty;
            foreach (var label in chrome.Children.OfType<TextBlock>()) { label.Text = string.Empty; }
        }
    }

    internal void Release() => lifetime.Dispose();

    internal static string Describe(ExactAnswerExportOutcome outcome, string path) => outcome switch
    {
        ExactAnswerExportOutcome.Saved => "Saved and exact UTF-8 bytes verified: " + path
            + ". The file was not opened. Kora deletion does not remove this export.",
        ExactAnswerExportOutcome.Cancelled => "Export cancelled before publication. No answer file was published.",
        ExactAnswerExportOutcome.NotWritten => "Export refused or failed before publication. Choose a new destination through a fresh review; no file was replaced.",
        _ => "Export effect uncertain. A file may exist at " + path
            + ". Do not retry automatically. Inspect that destination; no successful save or rollback is claimed.",
    };
}
