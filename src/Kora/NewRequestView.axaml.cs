using Avalonia.Controls;
using Avalonia.Input;

namespace Kora;

internal sealed partial class NewRequestView : UserControl, IDisposable
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime presentation;
    private NativeDraftWrapping? wrapping;
    private bool disposed;
    internal bool IsPresented => presentation.IsPresented;
    internal event EventHandler? Closed;
    internal event EventHandler<WindowClosingEventArgs>? Closing;
    internal event EventHandler? CloseRequested;
    internal void Activate() => Owner.Activate();
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

    public NewRequestView() => throw new InvalidOperationException("New request requires an isolated composer and its actual native owner.");

    internal NewRequestView(NewRequestViewModel model, Window owner, Func<Task> navigate, Action<Exception> navigationFailure,
        Func<Func<bool>>? captureDraftSource = null)
    {
        Owner = owner;
        this.model = model;
        captureSource = captureDraftSource ?? (() => static () => true);
        reportFailure = navigationFailure;
        InitializeComponent();
        DataContext = model;
        presentation = new(this, owner, () => !disposed);
        presentation.PresentationChanged += OnPresentationChanged;
        presentation.ActivationChanged += (_, _) => { if (!presentation.IsActive) { CancelLiteralDictation(); } };
        AttachedToVisualTree += (_, _) =>
        {
            wrapping?.Dispose();
            wrapping = new(Owner, NewRequestBody, WrapDraftText, model, captureSource);
        };
        DetachedFromVisualTree += (_, _) => { wrapping?.Dispose(); wrapping = null; CancelLiteralDictation(); };
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
        CloseNewRequest.Click += (_, _) => RunDictation(RequestCloseAsync);
        Owner.Closing += OnOwnerClosing;
        Owner.Closed += OnOwnerClosed;
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
            if (!args.Handled && args.Key == Key.Escape) { RunDictation(RequestCloseAsync); args.Handled = true; }
        };
    }

    internal void ClosePrivate()
    {
        privateClose = true;
        if (Owner is NewRequestWindow) { Owner.Hide(); }
        DisposeDraftFind();
        CancelLiteralDictation();
        model.Dispose();
        discardDialog?.Close();
        if (!openingPrivate)
        {
            IsVisible = false;
            RunDictation(CloseAfterDictationRelease);
        }
    }

    internal async Task<bool> PrepareShutdown()
    {
        if (privateClose || disposed || !Owner.IsVisible) { return false; }
        if (choosing || model.Composer.Busy)
        { throw new InvalidOperationException("Finish the pending New request operation before closing."); }
        if (!captureSource()() || !model.Composer.CaptureUnsentCloseEligibility()())
        { throw new InvalidOperationException("Private New request source is unavailable; no voluntary close was confirmed."); }
        var source = captureSource();
        choosing = true;
        try
        {
            CancelLiteralDictation();
#pragma warning disable VSTHRD003 // Shutdown keeps the exact native view owned until its capture resources have physically joined.
            if (model.Dictation is { } dictation) { await dictation.Quiescence; }
#pragma warning restore VSTHRD003
            if (!source() || privateClose || disposed) { return false; }
            if (!model.Composer.Dirty) { CompleteClose(); return disposed; }
            model.Composer.Cancel();
            return await ProtectClose();
        }
        finally { choosing = false; }
    }

    internal void OpenPrivate()
    {
        openingPrivate = true;
        try { IsVisible = true; }
        finally { openingPrivate = false; }
        if (privateClose) { IsVisible = false; RunDictation(CloseAfterDictationRelease); }
    }

    private async Task CloseAfterDictationRelease()
    {
#pragma warning disable VSTHRD003 // Private closure waits for the owned native release while the window stays hidden.
        if (model.Dictation is { } dictation) { await dictation.Quiescence; }
#pragma warning restore VSTHRD003
        CompleteClose();
    }

    private async Task<bool> ProtectClose()
    {
        var current = model.Composer.CaptureUnsentCloseEligibility();
        var source = captureSource();
        var decision = DecideDraft is null ? ComposerDraftDecision.Cancel : await DecideDraft();
        if (!Owner.IsVisible || privateClose || disposed) { return false; }
        if (decision != ComposerDraftDecision.Discard) { return false; }
        if (!current() || !source())
        { throw new OperationCanceledException("New request input or private source changed during discard review; the current input remains open."); }
        privateClose = true;
        CompleteClose();
        return disposed;
    }

    private async Task<ComposerDraftDecision> ChooseDraft()
    {
        if (Owner is ResponseWindow primary)
        {
            var body = new StackPanel { Margin = new(12), Spacing = 12 };
            body.Children.Add(new TextBlock
            {
                Text = "Discard this isolated unsent memory input and close, or Cancel to keep editing. No saved text, session or retained history is removed.\n"
                    + model.TargetLabel + "\n" + model.AttemptLabel,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            var discardInline = new Button { Name = "DiscardBeforeClose", Content = "Discard memory draft and close" };
            var cancelInline = new Button { Name = "CancelClose", Content = "Cancel closing" };
            body.Children.Add(discardInline);
            body.Children.Add(cancelInline);
            var decision = new TaskCompletionSource<ComposerDraftDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            discardInline.Click += (_, _) => decision.TrySetResult(ComposerDraftDecision.Discard);
            cancelInline.Click += (_, _) => decision.TrySetResult(ComposerDraftDecision.Cancel);
            var admitted = captureSource();
            try
            {
                primary.ShowNativeReview(body, "Discard isolated New request input", body, admitted,
                    shown => { if (!shown) { decision.TrySetResult(ComposerDraftDecision.Cancel); } });
                return await decision.Task;
            }
            finally { primary.RemoveNativeReview(body); body.Children.Clear(); }
        }
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
        try { return await dialog.ShowDialog<ComposerDraftDecision?>(Owner) ?? ComposerDraftDecision.Cancel; }
        finally { content.Children.Clear(); dialog.DataContext = null; discardDialog = null; }
    }

    private void OnOwnerClosing(object? sender, WindowClosingEventArgs args)
    {
        Closing?.Invoke(this, args);
        if (args.Cancel || disposed || privateClose) { return; }
        if (NativeShutdownPolicy.RequiresImmediateRetirement(args.CloseReason))
        {
            privateClose = true;
            DisposeDraftFind();
            model.Dispose();
            discardDialog?.Close();
            return;
        }
        CancelLiteralDictation();
        if (!model.Composer.Dirty && model.Dictation?.Quiescence.IsCompletedSuccessfully != false)
        { model.Composer.Cancel(); return; }
        args.Cancel = true;
        if (!choosing) { RunDictation(CloseFromOwnerAsync); }
    }

    private async Task CloseFromOwnerAsync()
    {
        await RequestCloseAsync();
        if (disposed && Owner.IsVisible) { Owner.Close(); }
    }
    private void OnOwnerClosed(object? sender, EventArgs args) => ClosePrivate();

    private async Task RequestCloseAsync()
    {
        if (disposed || choosing) { return; }
        CancelLiteralDictation();
        model.Composer.Cancel();
        choosing = true;
        try
        {
#pragma warning disable VSTHRD003 // This view joins the original dictation owner before discarding its isolated memory input.
            if (model.Dictation is { } dictation) { await dictation.Quiescence; }
#pragma warning restore VSTHRD003
            if (!model.Composer.Dirty) { privateClose = true; CompleteClose(); return; }
            await ProtectClose();
        }
        finally { choosing = false; }
    }

    private void OnPresentationChanged(object? sender, EventArgs args)
    {
        if (presentation.IsPresented) { return; }
        CancelLiteralDictation();
        RetireDraftFind();
        model.Composer.Cancel();
    }

    private void CompleteClose()
    {
        if (disposed) { return; }
        Dispose();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        IsVisible = false;
        CancelLiteralDictation();
        wrapping?.Dispose();
        wrapping = null;
        presentation.Dispose();
        Owner.Closing -= OnOwnerClosing;
        Owner.Closed -= OnOwnerClosed;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
