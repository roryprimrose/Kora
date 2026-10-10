using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora;

public sealed class SpeechCaptionWindowController : ObservableObject, IDisposable
{
    private readonly ISpeechCaptionPresentation presentation;
    private readonly ICaptionDisplaySource displays;
    private readonly SpeechCaptionWindow window;
    private readonly DispatcherTimer timer;
    private CaptionDisplayLifetime? explicitTarget;
    private long observedRevision = -1;
    private long selectionRevision;
    private bool targetUnavailable;
    private bool disposed;
    private bool failed;
    private bool canSelect;
    private IReadOnlyList<CaptionDisplayChoice> choices = [];
    private CaptionDisplayChoice? selectedChoice;
    private string displayStatus = "Primary display (run-only default).";

    public SpeechCaptionWindowController(ISpeechCaptionPresentation presentation, SpeechCaptionWindow window,
        ICaptionDisplaySource displays)
    {
        this.presentation = presentation;
        this.window = window;
        this.displays = displays;
        window.BindDisplaySelection(this);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, OnFrame);
        presentation.PrivacyClosureRequested += OnPrivacyClosure;
        displays.Changed += OnDisplaysChanged;
        window.Closed += OnClosed;
        timer.Start();
    }

    public IReadOnlyList<CaptionDisplayChoice> Choices => choices;
    public CaptionDisplayChoice? SelectedChoice
    {
        get => selectedChoice;
        set => SetProperty(ref selectedChoice, value);
    }
    public string DisplayStatus => displayStatus;
    public bool CanSelect => canSelect;

    private void OnFrame(object? sender, EventArgs args) => Refresh();
    private void OnDisplaysChanged(object? sender, EventArgs args)
    {
        if (disposed || failed) { return; }
        window.Hide();
        Refresh();
    }

    public void Refresh()
    {
        if (disposed || failed) { return; }
        try
        {
            var snapshot = displays.Capture();
            UpdateChoices(snapshot);
            var target = ResolveTarget(snapshot);
            if (target is null)
            {
                window.Hide();
                if (!targetUnavailable)
                {
                    targetUnavailable = true;
                    SetProperty(ref displayStatus, "Caption display unavailable. Explicitly choose a live display or Return to primary; retired text will not return.", nameof(DisplayStatus));
                    presentation.ReportSpeechCaptionDisplayUnavailable();
                }
                else { presentation.ReportSpeechCaptionDisplayUnavailable(reportRecovery: false); }
                return;
            }
            if (targetUnavailable)
            {
                window.Hide();
                presentation.ReportSpeechCaptionDisplayUnavailable(reportRecovery: false);
                return;
            }
            presentation.RefreshSpeechPlaybackFrame();
            if (!presentation.IsSpeechCaptionVisible)
            {
                window.Hide();
                return;
            }
            Place(target);
            // Recheck source privacy after layout and before exposing the native text surface.
            if (presentation.IsSpeechCaptionVisible) { if (!window.IsVisible) { window.Show(); } }
            else { window.Hide(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailPresentation(exception);
        }
    }

    private void UpdateChoices(CaptionDisplaySnapshot snapshot)
    {
        SetProperty(ref canSelect, presentation.CanChooseSpeechCaptionDisplay && !failed, nameof(CanSelect));
        if (observedRevision == snapshot.Revision) { return; }
        observedRevision = snapshot.Revision;
        choices = snapshot.Displays.Where(display => IsAvailable(snapshot, display))
            .Select((display, ordinal) => new CaptionDisplayChoice(display, snapshot.Revision, selectionRevision,
                string.Create(CultureInfo.InvariantCulture,
                    $"Display {ordinal + 1}{(display.IsPrimary ? " (primary)" : "")}: {display.WorkingArea.Width} × {display.WorkingArea.Height}, {display.WorkingArea.X}, {display.WorkingArea.Y}")))
            .ToArray();
        SelectedChoice = null;
        OnPropertyChanged(nameof(Choices));
    }

    private CaptionDisplayObservation? ResolveTarget(CaptionDisplaySnapshot snapshot)
    {
        var matches = snapshot.Displays.Where(display => explicitTarget is null
            ? display.IsPrimary : ReferenceEquals(display.Lifetime, explicitTarget)).ToArray();
        return matches.Length == 1 && IsAvailable(snapshot, matches[0]) ? matches[0] : null;
    }

    private static bool IsValid(CaptionDisplayObservation display) => double.IsFinite(display.Scaling)
        && display.Scaling > 0 && display.WorkingArea.Width > 0 && display.WorkingArea.Height > 0
        && (long)display.WorkingArea.X + display.WorkingArea.Width <= int.MaxValue
        && (long)display.WorkingArea.Y + display.WorkingArea.Height <= int.MaxValue;

    private static bool IsAvailable(CaptionDisplaySnapshot snapshot, CaptionDisplayObservation display) =>
        IsValid(display) && snapshot.Displays.Count(other => ReferenceEquals(other.Lifetime, display.Lifetime)) == 1
        && !snapshot.Displays.Any(other => !ReferenceEquals(other.Lifetime, display.Lifetime)
            && other.WorkingArea.Intersects(display.WorkingArea));

    internal void SelectFromNative(Window owner, bool reset)
    {
        try
        {
            HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
            {
                if (disposed || failed || !owner.IsVisible || !presentation.CanChooseSpeechCaptionDisplay) { return; }
                var choice = SelectedChoice;
                var snapshot = displays.Capture();
                UpdateChoices(snapshot);
                if (!reset && (choice is null || choice.Revision != snapshot.Revision || choice.SelectionRevision != selectionRevision
                    || !choices.Any(current => ReferenceEquals(current, choice))))
                {
                    SetProperty(ref displayStatus, "Display choice expired. Inspect and select a current display.", nameof(DisplayStatus));
                    return;
                }
                var target = reset ? snapshot.Displays.Where(display => display.IsPrimary).ToArray()
                    : snapshot.Displays.Where(display => ReferenceEquals(display.Lifetime, choice!.Display.Lifetime)).ToArray();
                if (target.Length != 1 || !IsAvailable(snapshot, target[0]))
                {
                    Refresh();
                    return;
                }
                if (!owner.IsVisible || !presentation.CanChooseSpeechCaptionDisplay) { return; }
                explicitTarget = reset ? null : target[0].Lifetime;
                selectionRevision = checked(selectionRevision + 1);
                observedRevision = -1;
                targetUnavailable = false;
                SetProperty(ref displayStatus, reset ? "Primary display (run-only default)."
                    : "Explicit display selected for this run only. Caption mode and original dismissal deadline are unchanged.", nameof(DisplayStatus));
                Refresh();
            }, HostActivityLayer.Desktop, HostOperation.Presentation);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailPresentation(exception);
        }
    }

    internal void InvalidateNativeChoices()
    {
        if (disposed) { return; }
        selectionRevision = checked(selectionRevision + 1);
        observedRevision = -1;
        SelectedChoice = null;
    }

    private void FailPresentation(Exception exception)
    {
        failed = true;
        timer.Stop();
        window.Hide();
        SetProperty(ref canSelect, false, nameof(CanSelect));
        presentation.ReportSpeechCaptionPresentationFailure(exception.GetType().Name);
    }

    private void Place(CaptionDisplayObservation target)
    {
        var placement = presentation.SpeechCaptionPlacement
            ?? throw new InvalidOperationException("Caption placement is unconfirmed.");
        var area = target.WorkingArea;
        // Reduce the inset only when necessary to keep at least one physical pixel on tiny working areas.
        var marginX = Math.Min(Math.Ceiling(24 * target.Scaling), (area.Width - 1) / 2);
        var marginY = Math.Min(Math.Ceiling(24 * target.Scaling), (area.Height - 1) / 2);
        var width = Math.Min(520, (area.Width - 2 * marginX) / target.Scaling);
        var height = (area.Height - 2 * marginY) / target.Scaling;
        var content = window.Content as Control
            ?? throw new InvalidOperationException("The caption native surface is unavailable.");
        content.Measure(new Size(width, height));
        var measuredHeight = Math.Min(height, content.DesiredSize.Height);
        var pixelWidth = Math.Min(area.Width, (int)Math.Ceiling(width * target.Scaling));
        var pixelHeight = Math.Min(area.Height, (int)Math.Ceiling(measuredHeight * target.Scaling));
        var position = new PixelPoint(
            placement is SpeechCaptionPlacement.TopLeft or SpeechCaptionPlacement.BottomLeft
                ? area.X + (int)marginX : area.Right - pixelWidth - (int)marginX,
            placement is SpeechCaptionPlacement.TopLeft or SpeechCaptionPlacement.TopRight
                ? area.Y + (int)marginY : area.Bottom - pixelHeight - (int)marginY);
        if (window.Width != width || window.Height != measuredHeight || window.Position != position
            || window.MaxWidth != width || window.MaxHeight != height)
        {
            // Native resize/reposition is not atomic; hide before either to prevent transient monitor spill.
            window.Hide();
            window.MaxWidth = width;
            window.MaxHeight = height;
            window.Width = width;
            window.Height = measuredHeight;
            window.Position = position;
        }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            window.Hide();
            InvalidateNativeChoices();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed) { window.Hide(); InvalidateNativeChoices(); }
            });
        }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        timer.Stop();
        timer.Tick -= OnFrame;
        presentation.PrivacyClosureRequested -= OnPrivacyClosure;
        displays.Changed -= OnDisplaysChanged;
        displays.Dispose();
        window.Closed -= OnClosed;
        window.DataContext = null;
        window.Close();
        choices = [];
        selectedChoice = null;
        explicitTarget = null;
        presentation.ReportSpeechCaptionDisplayUnavailable();
    }
}