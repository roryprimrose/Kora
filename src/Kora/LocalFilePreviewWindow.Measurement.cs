using System.ComponentModel;
using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using Kora.Application.ViewModels;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Presentation;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private const string MeasurementLabel = "Decoded/admitted display text metrics for this exact item";
    private const string MeasurementUnavailable = "Decoded/admitted display text metrics unavailable; no current admitted text measurement.";
    private NamedTextBlock? measurementText;
    private LocalFileRevision? measuredSource;
    private Func<bool>? measurementCurrent;
    private IReadOnlyList<LocalFileRevision>? measurementSources;
    private long measurementGeneration;
    private int measurementRetired;

    private void ShowSourceMeasurement(DockPanel body, Func<bool> isCurrent, IReadOnlyList<LocalFileRevision> sources)
    {
        var hostCurrent = typographyHost?.CaptureLocalFilePresentationEligibility();
        measurementCurrent = () => isCurrent() && (hostCurrent is null || hostCurrent());
        measurementSources = sources;
        Volatile.Write(ref measurementRetired, 0);
        var label = new NamedTextBlock { Text = MeasurementUnavailable, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        measurementText = label;
        AutomationProperties.SetName(label, MeasurementLabel);
        DockPanel.SetDock(label, Dock.Top);
        body.Children.Insert(1, label);
        content.PropertyChanged += OnMeasuredContentChanged;
        if (typographyHost is not null)
        {
            typographyHost.PropertyChanged += OnMeasurementHostChanged;
            typographyHost.PrivacyClosureRequested += OnMeasurementPrivacyClosed;
            typographyHost.FilePreviewChanged += OnMeasurementSourceChanged;
        }
    }

    private void SetMeasurementSource(LocalFileRevision? file)
    {
        var generation = Interlocked.Increment(ref measurementGeneration);
        measuredSource = file;
        var label = measurementText;
        if (label is null) { return; }
        label.Text = MeasurementUnavailable;
        bool Current() => measurementCurrent?.Invoke() == true
            && generation == Volatile.Read(ref measurementGeneration)
            && Volatile.Read(ref measurementRetired) == 0
            && ReferenceEquals(measurementText, label) && ReferenceEquals(measuredSource, file)
            && file is not null && measurementSources?.Contains(file) == true
            && string.Equals(content.Text, file.Text, StringComparison.Ordinal);
        if (file is null) { return; }
        if (!Current())
        {
            if (generation == Volatile.Read(ref measurementGeneration)) { RetireSourceMeasurement(); }
            return;
        }
        var measure = DecodedTextMeasurement.Measure(file.Text);
        if (!Current())
        {
            if (generation == Volatile.Read(ref measurementGeneration)) { RetireSourceMeasurement(); }
            return;
        }
        label.Text = $"Decoded/admitted display text for this exact item only: UTF-16 code units: {Number(measure.Utf16CodeUnits)}; "
            + $"Unicode scalars: {Number(measure.UnicodeScalars)}; strict UTF-8 re-encoding bytes: {Number(measure.StrictUtf8Bytes)}.\n"
            + $"Original source bytes at admission (including any stripped leading BOM): {Number(file.Review.Metadata.ByteLength)}.\n"
            + "Text metrics are not original/on-disk encoding or bytes, retained storage bytes, folder totals or model tokens. "
            + "No text limit or model capacity is inferred.";
        if (!Current() && generation == Volatile.Read(ref measurementGeneration)) { RetireSourceMeasurement(); }
    }

    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable (invalid Unicode)";

    private void OnMeasuredContentChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBlock.TextProperty && measuredSource is { } file
            && !string.Equals(content.Text, file.Text, StringComparison.Ordinal)) { RetireSourceMeasurement(); }
    }

    private void OnMeasurementHostChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CommandText) or nameof(MainViewModel.CallPolicyRevision)
            || measurementCurrent?.Invoke() != true) { RetireSourceMeasurement(); }
    }

    private void OnMeasurementPrivacyClosed(object? sender, EventArgs args) => RetireSourceMeasurement();

    private void OnMeasurementSourceChanged(object? sender, EventArgs args)
    {
        if (measurementCurrent?.Invoke() != true) { RetireSourceMeasurement(); }
    }

    private void RetireSourceMeasurement()
    {
        Volatile.Write(ref measurementRetired, 1);
        Interlocked.Increment(ref measurementGeneration);
        var label = measurementText;
        // A queued retirement owns only its old control, never a replacement view or item.
        if (Dispatcher.UIThread.CheckAccess()) { Clear(); }
        else { Dispatcher.UIThread.Post(Clear); }
        void Clear()
        {
            if (ReferenceEquals(measurementText, label) && Volatile.Read(ref measurementRetired) != 0)
            {
                measuredSource = null;
                measurementCurrent = null;
                measurementSources = null;
            }
            if (label is not null) { label.Text = MeasurementUnavailable; }
        }
    }

    private void ClearSourceMeasurement()
    {
        Volatile.Write(ref measurementRetired, 1);
        Interlocked.Increment(ref measurementGeneration);
        var label = measurementText;
        content.PropertyChanged -= OnMeasuredContentChanged;
        if (typographyHost is not null)
        {
            typographyHost.PropertyChanged -= OnMeasurementHostChanged;
            typographyHost.PrivacyClosureRequested -= OnMeasurementPrivacyClosed;
            typographyHost.FilePreviewChanged -= OnMeasurementSourceChanged;
        }
        measuredSource = null;
        measurementCurrent = null;
        measurementSources = null;
        measurementText = null;
        if (label is not null) { label.Text = MeasurementUnavailable; }
    }
}
