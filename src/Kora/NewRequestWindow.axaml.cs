using Avalonia.Controls;
using Avalonia.Input;

namespace Kora;

internal sealed partial class NewRequestWindow : Window
{
    private readonly NewRequestViewModel model;
    private readonly Func<Func<bool>> captureSource;
    private bool privateClose;
    private bool choosing;
    private bool openingPrivate;
    private Window? discardDialog;
    private long dictationArm;
    private bool dictationHeld;
    private readonly Action<Exception> reportFailure;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Func<Task<ComposerDraftDecision>>? DecideDraft { get; set; }

    internal NewRequestWindow(NewRequestViewModel model, Func<Task> navigate, Action<Exception> navigationFailure,
        Func<Func<bool>>? captureDraftSource = null)
    {
        this.model = model;
        captureSource = captureDraftSource ?? (() => static () => true);
        reportFailure = navigationFailure;
        InitializeComponent();
        DataContext = model;
        _ = new NativeDraftWrapping(this, NewRequestBody, WrapDraftText, model,
            captureDraftSource ?? (() => () => model.CanEdit));
        DecideDraft = ChooseDraft;
        BindDictation();
        CreateTarget.Click += async (_, _) => await (Operation = model.Composer.Create());
        ReviewNewRequest.Click += (_, _) => model.Composer.Review();
        SubmitNewRequest.Click += async (_, _) => await (Operation = model.Composer.Submit());
        InspectNewRequest.Click += async (_, _) => await (Operation = model.Composer.Inspect());
        CancelNewRequest.Click += (_, _) => model.Composer.Cancel();
        OpenNewRequestSession.Click += async (_, _) =>
        {
            try { await (Operation = navigate()); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { navigationFailure(exception); }
        };
        Closing += async (_, args) =>
        {
            if (NativeShutdownPolicy.RequiresImmediateRetirement(args.CloseReason))
            {
                privateClose = true;
                DisposeDraftFind();
                model.Dispose();
                discardDialog?.Close();
                return;
            }
            if (privateClose) { return; }
            CancelLiteralDictation();
            if (!model.Composer.Dirty && model.Dictation?.Quiescence.IsCompletedSuccessfully != false)
            { model.Composer.Cancel(); return; }
            args.Cancel = true;
            if (choosing) { return; }
            model.Composer.Cancel();
            choosing = true;
            try
            {
#pragma warning disable VSTHRD003 // Native closing joins the exact owned capture release without blocking the dispatcher.
                if (model.Dictation is { } dictation) { await dictation.Quiescence; }
#pragma warning restore VSTHRD003
                if (!model.Composer.Dirty) { privateClose = true; Close(); return; }
                await (Operation = ProtectClose());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { model.Composer.Cancel(); navigationFailure(exception); }
            finally { choosing = false; }
        };
        Closed += (_, _) =>
        {
            DisposeDraftFind();
            model.Dispose();
            DecideDraft = null;
            discardDialog?.Close();
            DataContext = null;
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }

    internal void ClosePrivate()
    {
        privateClose = true;
        DisposeDraftFind();
        CancelLiteralDictation();
        model.Dispose();
        discardDialog?.Close();
        if (!openingPrivate)
        {
            Hide();
            RunDictation(CloseAfterDictationRelease);
        }
    }

    internal async Task<bool> PrepareShutdown()
    {
        if (privateClose || !IsVisible) { return false; }
        if (choosing || model.Composer.Busy)
        { throw new InvalidOperationException("Finish the pending New request operation before closing."); }
        if (!captureSource()() || !model.Composer.CaptureUnsentCloseEligibility()())
        { throw new InvalidOperationException("Private New request source is unavailable; no voluntary close was confirmed."); }
        if (!model.Composer.Dirty) { Close(); return !IsVisible; }
        model.Composer.Cancel();
        choosing = true;
        try { return await ProtectClose(); }
        finally { choosing = false; }
    }

    internal void OpenPrivate()
    {
        openingPrivate = true;
        try { Show(); }
        finally { openingPrivate = false; }
        if (privateClose) { Hide(); RunDictation(CloseAfterDictationRelease); }
    }

    private async Task CloseAfterDictationRelease()
    {
#pragma warning disable VSTHRD003 // Private closure waits for the owned native release while the window stays hidden.
        if (model.Dictation is { } dictation) { await dictation.Quiescence; }
#pragma warning restore VSTHRD003
        Close();
    }

    private async Task<bool> ProtectClose()
    {
        var current = model.Composer.CaptureUnsentCloseEligibility();
        var source = captureSource();
        var decision = DecideDraft is null ? ComposerDraftDecision.Cancel : await DecideDraft();
        if (!IsVisible || privateClose) { return false; }
        if (decision != ComposerDraftDecision.Discard) { return false; }
        if (!current() || !source())
        { throw new OperationCanceledException("New request input or private source changed during discard review; the current input remains open."); }
        if (IsVisible) { privateClose = true; Close(); }
        return !IsVisible;
    }

    private async Task<ComposerDraftDecision> ChooseDraft()
    {
        var dialog = new Window { Title = "Close isolated New request draft?", Width = 660, Height = 280, DataContext = model };
        var content = new StackPanel { Margin = new(18), Spacing = 16 };
        content.Children.Add(new TextBlock
        {
            Text = "Unsent text is memory-only. Discard it and close, or Cancel to keep editing. Existing session and retained history remain; closing is not rollback.\n" + model.TargetLabel + "\n" + model.AttemptLabel,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        var discard = new Button { Name = "DiscardBeforeClose", Content = "Discard memory draft and close" };
        var cancel = new Button { Name = "CancelClose", Content = "Cancel closing" };
        discard.Click += (_, _) => dialog.Close(ComposerDraftDecision.Discard);
        cancel.Click += (_, _) => dialog.Close(ComposerDraftDecision.Cancel);
        buttons.Children.Add(discard);
        buttons.Children.Add(cancel);
        content.Children.Add(buttons);
        dialog.Content = content;
        discardDialog = dialog;
        try { return await dialog.ShowDialog<ComposerDraftDecision?>(this) ?? ComposerDraftDecision.Cancel; }
        finally { content.Children.Clear(); dialog.DataContext = null; discardDialog = null; }
    }

}
