using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Kora.Core.Diagnostics;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class EvidenceWindow : Window
{
    internal EvidenceWindow(EvidenceViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        FindFirst.Click += (_, _) => Find(SnapshotFindDirection.First);
        FindNext.Click += (_, _) => Find(SnapshotFindDirection.Next);
        FindPrevious.Click += (_, _) => Find(SnapshotFindDirection.Previous);
        ClearLiteral.Click += (_, _) => { if (model.EnsureFindAdmission()) { model.ClearFind(); } };
        Activated += (_, _) => model.EnsureFindAdmission();
        Search.Click += async (_, _) => await model.SearchAsync();
        Next.Click += async (_, _) => await model.NextAsync();
        ClearAdvanced.Click += (_, _) => model.ClearAdvancedFilters();
        Trace.Click += async (_, _) => await model.ReadTraceAsync();
        OpenSegment.Click += async (_, _) =>
        {
            if (Segments.SelectedItem is EvidenceSegment segment) { await model.NavigateAsync(segment); }
        };
        Records.SelectionChanged += (_, _) => model.Select(Records.SelectedItem as EvidenceRecord);
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Close();
        foreach (var input in this.GetLogicalDescendants().OfType<TextBox>())
        {
            input.CopyingToClipboard += (_, args) => args.Handled = true;
            input.CuttingToClipboard += (_, args) => args.Handled = true;
        }
        LiteralQuery.PastingFromClipboard += (_, args) => args.Handled = true;
        AddHandler(ContextRequestedEvent, (_, args) =>
        {
            if (ReferenceEquals(args.Source, LiteralQuery) || ReferenceEquals(args.Source, LiteralMatch))
            {
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is Key.C or Key.X or Key.Insert
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.Delete
                || ReferenceEquals(args.Source, LiteralQuery) && (
                    args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key == Key.V
                    || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.Insert))
            {
                args.Handled = true;
                return;
            }
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key == Key.F)
            {
                args.Handled = true;
                if (model.EnsureFindAdmission() && model.CanFind) { LiteralQuery.Focus(); }
                return;
            }
            if (args.Key == Key.F3 || args.Key == Key.Enter && ReferenceEquals(args.Source, LiteralQuery))
            {
                args.Handled = true;
                Find(args.Key == Key.F3
                    ? args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next
                    : SnapshotFindDirection.First);
                return;
            }
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        }, RoutingStrategies.Tunnel);

        void Find(SnapshotFindDirection direction)
        {
            model.FindLiteral(direction);
            var revision = model.FindRevision;
            if (!model.IsFindCurrent(revision) || !model.HasFindSelection) { return; }
            UpdateLayout();
            if (!model.IsFindCurrent(revision) || !model.HasFindSelection) { return; }
            LiteralMatch.BringIntoView();
            if (!model.IsFindCurrent(revision) || !model.HasFindSelection) { return; }
            if (!LiteralMatch.Focus()) { model.FindFocusFailed(); }
            model.IsFindCurrent(revision);
        }
    }
}
