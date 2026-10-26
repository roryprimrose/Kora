using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Kora.Application.ViewModels;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class EvidenceWindow : Window
{
    internal async Task<bool> PrepareTraceAsync(string traceId, Func<bool> admitted)
    {
        if (!admitted() || !IsVisible || DataContext is not EvidenceViewModel model || model.IsClosed)
        {
            return false;
        }
        var revision = model.FilterRevision;
        bool Current() => admitted() && ReferenceEquals(DataContext, model) && !model.IsClosed
            && model.FilterRevision == revision;
        var activation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnActivated(object? sender, EventArgs args) => activation.TrySetResult(true);
        void OnClosed(object? sender, EventArgs args) => activation.TrySetResult(false);
        Activated += OnActivated;
        Closed += OnClosed;
        try
        {
            Activate();
            if (!IsActive && !await activation.Task.WaitAsync(TimeSpan.FromSeconds(5))) { return false; }
            UpdateLayout();
            if (!Current() || !IsActive || !TraceId.Focus() || !Current()) { return false; }
            model.PrepareTraceFilter(traceId);
            return admitted() && ReferenceEquals(DataContext, model) && !model.IsClosed
                && model.FilterRevision == revision + 1 && IsActive && TraceId.IsFocused
                && string.Equals(model.TraceFilter, traceId, StringComparison.Ordinal)
                && string.Equals(TraceId.Text, traceId, StringComparison.Ordinal);
        }
        catch (TimeoutException) { return false; }
        finally
        {
            Activated -= OnActivated;
            Closed -= OnClosed;
        }
    }

    internal EvidenceWindow(EvidenceViewModel model, MainViewModel? typographyHost = null)
    {
        typographyHost?.EnsureReaderTypographyAvailable();
        InitializeComponent();
        DataContext = model;
        var scaleBody = NativeReaderTypography.Capture(EvidenceBody);
        var scaleMatch = NativeReaderTypography.Capture(LiteralMatch);
        var retired = false;
        var updatingTypography = false;
        model.PropertyChanged += OnPageChanged;
        if (typographyHost is not null) { typographyHost.PropertyChanged += OnPreferenceChanged; }
        Closed += (_, _) =>
        {
            retired = true;
            model.PropertyChanged -= OnPageChanged;
            if (typographyHost is not null) { typographyHost.PropertyChanged -= OnPreferenceChanged; }
            scaleBody(ReaderTextScaleSettings.DefaultPercent);
            scaleMatch(ReaderTextScaleSettings.DefaultPercent);
        };
        UpdateTypography();
        FindFirst.Click += (_, _) => Find(SnapshotFindDirection.First);
        FindNext.Click += (_, _) => Find(SnapshotFindDirection.Next);
        FindPrevious.Click += (_, _) => Find(SnapshotFindDirection.Previous);
        ClearLiteral.Click += (_, _) => { if (model.EnsureFindAdmission()) { model.ClearFind(); } };
        Activated += (_, _) => { model.EnsureFindAdmission(); UpdateTypography(); };
        Search.Click += async (_, _) => await model.SearchAsync();
        Next.Click += async (_, _) => await model.NextAsync();
        ClearAdvanced.Click += (_, _) => model.ClearAdvancedFilters();
        Trace.Click += async (_, _) => await model.ReadTraceAsync();
        ReadRequest.Click += async (_, _) => await model.ReadRequestAsync();
        ReadActivity.Click += async (_, _) => await model.ReadActivityAsync();
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

        void OnPageChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (string.Equals(args.PropertyName, nameof(EvidenceViewModel.CanFind), StringComparison.Ordinal)) { UpdateTypography(); }
        }

        void OnPreferenceChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (string.Equals(args.PropertyName, nameof(MainViewModel.ReaderTextScalePercent), StringComparison.Ordinal)) { UpdateTypography(); }
        }

        void UpdateTypography()
        {
            if (retired || updatingTypography) { return; }
            updatingTypography = true;
            try
            {
                if (typographyHost?.IsReaderTypographyUpdatePending == true)
                {
                    // Existing content keeps its last confirmed typography during an owned write.
                    // New windows still require confirmed evidence at constructor admission.
                    if (!model.EnsureFindAdmission() || !model.CanFind)
                    {
                        scaleBody(ReaderTextScaleSettings.DefaultPercent);
                        scaleMatch(ReaderTextScaleSettings.DefaultPercent);
                    }
                    return;
                }
                try { typographyHost?.EnsureReaderTypographyAvailable(); }
                catch (InvalidOperationException)
                {
                    retired = true;
                    model.Close();
                    scaleBody(ReaderTextScaleSettings.DefaultPercent);
                    scaleMatch(ReaderTextScaleSettings.DefaultPercent);
                    Close();
                    typographyHost!.ReportEvidencePresentationUnavailable();
                    return;
                }
                var admitted = model.EnsureFindAdmission();
                var percent = admitted && model.CanFind && typographyHost is not null
                    ? typographyHost.ReaderTextScalePercent : ReaderTextScaleSettings.DefaultPercent;
                scaleBody(percent);
                scaleMatch(model.HasFindSelection ? percent : ReaderTextScaleSettings.DefaultPercent);
                if (!model.EnsureFindAdmission() || !model.CanFind)
                {
                    scaleBody(ReaderTextScaleSettings.DefaultPercent);
                    scaleMatch(ReaderTextScaleSettings.DefaultPercent);
                }
                else if (!model.HasFindSelection) { scaleMatch(ReaderTextScaleSettings.DefaultPercent); }
            }
            finally { updatingTypography = false; }
        }

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
