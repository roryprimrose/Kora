using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using Kora.Application.Presentation;
using Kora.Core.Context;

namespace Kora.Controls;

public sealed partial class IdentityMetadataFindPane : UserControl, IDisposable
{
    private AdmittedMetadataFindState? state;
    private bool rendering;
    private bool disposed;

    public IdentityMetadataFindPane()
    {
        AvaloniaXamlLoader.Load(this);
        Query.ContextMenu = null;
        Query.ContextFlyout = null;
        Query.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty || rendering) { return; }
            state?.Edit(Query.Text ?? string.Empty);
            Render();
        };
        Button("IdentityFindFirst").Click += (_, _) => Find(SnapshotFindDirection.First);
        Button("IdentityFindNext").Click += (_, _) => Find(SnapshotFindDirection.Next);
        Button("IdentityFindPrevious").Click += (_, _) => Find(SnapshotFindDirection.Previous);
        Button("IdentityFindClear").Click += (_, _) => Clear();
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape) { Clear(); args.Handled = true; }
            else if (args.Key == Key.F3 || args.Key == Key.Enter && Query.IsFocused)
            {
                Find(args.Key == Key.Enter ? SnapshotFindDirection.First
                    : args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
                args.Handled = true;
            }
            else if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is Key.C or Key.X or Key.V or Key.Insert
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key is Key.Insert or Key.Delete
                || args.Key == Key.Apps || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.F10)
            { args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        AddHandler(TextBox.CopyingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AddHandler(TextBox.CuttingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AddHandler(TextBox.PastingFromClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        Render();
    }

    private TextBox Query => this.FindControl<TextBox>("IdentityFindQuery")!;
    private NamedTextBlock Match => this.FindControl<NamedTextBlock>("IdentityFindMatch")!;
    private Button Button(string name) => this.FindControl<Button>(name)!;

    internal void Admit(string displayedIdentity, Func<bool> current)
    {
        Retire();
        if (disposed) { return; }
        state = new(displayedIdentity, current);
        Render();
    }

    public void Dispose()
    {
        disposed = true;
        Retire();
        GC.SuppressFinalize(this);
    }

    internal void Retire()
    {
        state?.Dispose();
        state = null;
        Render();
    }

    private void Clear()
    {
        state?.Clear();
        Render();
    }

    private void Find(SnapshotFindDirection direction)
    {
        var admitted = state;
        admitted?.Find(direction);
        Render();
        if (!CurrentMatch(admitted)) { return; }
        UpdateLayout();
        if (!CurrentMatch(admitted)) { Render(); return; }
        Match.BringIntoView();
        if (!CurrentMatch(admitted)) { Render(); return; }
        if (!Match.Focus()) { admitted!.ReportFocusUnavailable(); }
        Render();
    }

    private bool CurrentMatch(AdmittedMetadataFindState? admitted) =>
        admitted is not null && ReferenceEquals(state, admitted) && admitted.Match is not null;

    private void Render()
    {
        rendering = true;
        try
        {
            var available = state?.Available == true;
            if (!available) { state?.Dispose(); state = null; }
            // Focus must not remain on a retired private match or disabled query.
            var hasMatch = state?.Match is not null;
            if ((!available && Query.IsFocused) || (!hasMatch && Match.IsFocused))
            { TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null); }
            Query.Text = state?.Query ?? string.Empty;
            Query.IsEnabled = available;
            Button("IdentityFindFirst").IsEnabled = available;
            Button("IdentityFindClear").IsEnabled = available;
            Button("IdentityFindNext").IsEnabled = state?.CanNavigate == true;
            Button("IdentityFindPrevious").IsEnabled = state?.CanNavigate == true;
            this.FindControl<NamedTextBlock>("IdentityFindStatus")!.Text = state?.Status ?? string.Empty;
            this.FindControl<NamedTextBlock>("IdentityFindLocation")!.Text = state?.Match is { } span
                ? $"UTF-16 [{span.Start}, {span.Start + span.Length}); end exclusive."
                : string.Empty;
            Match.Text = state?.ExactMatch ?? string.Empty;
            Match.IsVisible = hasMatch;
        }
        finally { rendering = false; }
    }

    private static void BlockTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
}
