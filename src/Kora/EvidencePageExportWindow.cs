using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed class EvidencePageExportWindow : Window
{
    private readonly CancellationTokenSource lifetime;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal EvidencePageExportOutcome? Outcome { get; private set; }
    internal bool IsRetired => lifetime.IsCancellationRequested;

    internal EvidencePageExportWindow(EvidencePageExportReview review, Func<bool> admitted, Action<string> reportFailure, CancellationToken token)
    {
        Title = "Export one exact bounded evidence page";
        Width = 820; Height = 720; MinWidth = 480; MinHeight = 400;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var source = new TextBox
        {
            Name = "ExportEvidenceSource", Text = review.Snapshot.Text, IsReadOnly = true,
            AcceptsReturn = true, IsUndoEnabled = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        AutomationProperties.SetName(source, "Every exact UTF-8 JSON export byte including record, source and gap provenance");
        ScrollViewer.SetVerticalScrollBarVisibility(source, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(source, ScrollBarVisibility.Disabled);
        source.CopyingToClipboard += (_, args) => args.Handled = true;
        source.CuttingToClipboard += (_, args) => args.Handled = true;
        source.PastingFromClipboard += (_, args) => args.Handled = true;
        source.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var metadata = new TextBlock
        {
            Text = "New destination: " + review.Destination + "\nFormat: UTF-8 JSON, no BOM; the complete bytes below are written unchanged.\n"
                + EvidencePageExportSnapshot.ExclusionDisclosure + "\n" + EvidencePageExportReview.RetentionDisclosure,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var status = new TextBlock { Name = "EvidenceExportStatus", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var confirm = new Button { Name = "ConfirmEvidencePageExport",
            Content = new TextBlock { Text = "Confirm this one new-file export", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var cancel = new Button { Name = "CancelEvidencePageExport",
            Content = new TextBlock { Text = "Cancel / Close", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        AutomationProperties.SetName(confirm, "Confirm this one exact evidence export");
        AutomationProperties.SetName(cancel, "Cancel and clear evidence export");
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        actions.Children.Add(confirm); Grid.SetColumn(cancel, 1); actions.Children.Add(cancel);
        var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("2*,3*,Auto,Auto"), RowSpacing = 10 };
        grid.Children.Add(new ScrollViewer { Content = metadata, Focusable = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(source, 1); grid.Children.Add(source);
        var statusViewport = new ScrollViewer { Content = status, MaxHeight = 100, Focusable = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(statusViewport, 2); grid.Children.Add(statusViewport);
        Grid.SetRow(actions, 3); grid.Children.Add(actions);
        Content = grid;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Storage);
        confirm.Click += async (_, _) =>
        {
            if (!confirm.IsEnabled) { return; }
            confirm.IsEnabled = false;
            Operation = Confirm();
            await Operation;
        };
        cancel.Click += (_, _) => Close();
        timer.Tick += (_, _) => { if (IsRetired || !admitted()) { Close(); } };
        Opened += (_, _) => { source.Focus(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); Clear(); lifetime.Cancel(); };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible) { Clear(); lifetime.Cancel(); }
        };

        async Task Confirm()
        {
            using var activity = continuation();
            activity.Complete(HostOperationOutcome.Failed);
            try
            {
                Outcome = await review.ConfirmAsync(lifetime.Token);
                source.Text = string.Empty;
                if (!IsRetired && admitted()) { status.Text = Describe(Outcome.Value, review.Destination); }
                activity.Complete(Outcome == EvidencePageExportOutcome.Saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                reportFailure(exception.GetType().Name);
                if (!IsRetired && admitted()) { status.Text = "Export unconfirmed. No automatic retry or rollback is claimed."; }
            }
        }

        void Clear() { source.Text = string.Empty; metadata.Text = string.Empty; status.Text = string.Empty; }
    }

    internal void Release() => lifetime.Dispose();
    internal static string Describe(EvidencePageExportOutcome outcome, string path) => outcome switch
    {
        EvidencePageExportOutcome.Saved => "Saved and exact UTF-8 JSON bytes verified: " + path + ". The file was not opened. Kora deletion does not remove this export.",
        EvidencePageExportOutcome.Cancelled => "Evidence export cancelled before publication. No evidence file was published.",
        EvidencePageExportOutcome.NotWritten => "Evidence export refused or failed before publication. No file was replaced. Start a fresh review with a new destination.",
        _ => "Evidence export effect uncertain. A file may exist at " + path + ". Do not retry automatically; inspect that destination. No successful save or rollback is claimed.",
    };
}
