using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Data;

namespace Kora;

internal sealed partial class NewRequestWindow : Window
{
    private readonly NewRequestViewModel model;
    private bool privateClose;
    private bool choosing;
    private bool openingPrivate;
    private Window? discardDialog;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Func<Task<ComposerDraftDecision>>? DecideDraft { get; set; }
    internal Func<Task<bool>>? ConfirmDraftSave { get; set; }

    internal NewRequestWindow(NewRequestViewModel model, Func<Task> navigate, Action<Exception> navigationFailure,
        Func<Func<bool>>? captureDraftSource = null)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        _ = new NativeDraftWrapping(this, NewRequestBody, WrapDraftText, model,
            captureDraftSource ?? (() => () => model.CanEdit));
        DecideDraft = ChooseDraft;
        ConfirmDraftSave = ConfirmSave;
        CreateTarget.Click += async (_, _) => await (Operation = model.Composer.Create());
        ReviewNewRequest.Click += (_, _) => model.Composer.Review();
        SubmitNewRequest.Click += async (_, _) => await (Operation = model.Composer.Submit());
        ReviewSaveNewRequest.Click += async (_, _) => await (Operation = model.Composer.ReviewSave());
        SaveNewRequest.Click += async (_, _) => await (Operation = model.Composer.Save());
        InspectNewRequest.Click += async (_, _) => await (Operation = model.Composer.Inspect());
        CancelNewRequest.Click += (_, _) => model.Composer.Cancel();
        OpenNewRequestSession.Click += async (_, _) =>
        {
            try { await (Operation = navigate()); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { navigationFailure(exception); }
        };
        Closing += async (_, args) =>
        {
            if (privateClose) { return; }
            if (!model.Composer.Dirty) { model.Composer.Cancel(); return; }
            args.Cancel = true;
            if (choosing) { return; }
            model.Composer.Cancel();
            choosing = true;
            try
            {
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
            ConfirmDraftSave = null;
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
        model.Dispose();
        discardDialog?.Close();
        if (!openingPrivate) { Close(); }
    }

    internal void OpenPrivate()
    {
        openingPrivate = true;
        try { Show(); }
        finally { openingPrivate = false; }
        if (privateClose) { Hide(); Close(); }
    }

    private async Task ProtectClose()
    {
        var decision = DecideDraft is null ? ComposerDraftDecision.Cancel : await DecideDraft();
        if (!IsVisible) { return; }
        if (decision == ComposerDraftDecision.Save)
        {
            if (!model.Composer.CanReviewSave) { return; }
            await model.Composer.ReviewSave();
            if (!model.Composer.CanSave || ConfirmDraftSave is null || !await ConfirmDraftSave())
            { model.Composer.Cancel(); return; }
            if (!await model.Composer.Save() || !model.Composer.CanCloseSaved) { return; }
        }
        else if (decision != ComposerDraftDecision.Discard) { return; }
        if (IsVisible) { privateClose = true; Close(); }
    }

    private async Task<ComposerDraftDecision> ChooseDraft()
    {
        var dialog = new Window { Title = "Close isolated New request draft?", Width = 660, Height = 280, DataContext = model };
        var content = new StackPanel { Margin = new(18), Spacing = 16 };
        content.Children.Add(new TextBlock
        {
            Text = "Save requires a separate frozen revision/body confirmation for the same confirmed fresh target. Otherwise Discard memory edits or Cancel closing. Existing session, saved draft and retained request remain; closing is not rollback.\n" + model.TargetLabel + "\n" + model.AttemptLabel,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        if (model.Composer.CanReviewSave)
        {
            var save = new Button { Name = "ReviewSaveBeforeClose", Content = "Review Save before closing" };
            save.Bind(IsEnabledProperty, new Binding(nameof(model.CanReviewSave)));
            save.Click += (_, _) => dialog.Close(ComposerDraftDecision.Save);
            buttons.Children.Add(save);
        }
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

    private async Task<bool> ConfirmSave()
    {
        var dialog = new Window { Title = "Confirm exact reviewed draft Save only", Width = 740, Height = 620, DataContext = model };
        var content = new StackPanel { Margin = new(18), Spacing = 12 };
        foreach (var property in new[] { nameof(model.TargetLabel), nameof(model.AttemptLabel), nameof(model.SaveLabel), nameof(model.BaselineBody) })
        {
            var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            text.Bind(TextBlock.TextProperty, new Binding(property));
            content.Children.Add(text);
        }
        content.Children.Add(new TextBlock { Text = "Complete frozen OLD saved body:" });
        var old = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        old.Bind(TextBlock.TextProperty, new Binding(nameof(model.SavedBodyReview)));
        content.Children.Add(old);
        content.Children.Add(new TextBlock { Text = "Complete frozen NEW proposed saved body:" });
        var proposed = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        proposed.Bind(TextBlock.TextProperty, new Binding(nameof(model.ProposedSavedBody)));
        content.Children.Add(proposed);
        var confirm = new Button { Name = "ConfirmSaveBeforeClose", Content = "Confirm Save draft only and close after exact receipt" };
        confirm.Bind(IsEnabledProperty, new Binding(nameof(model.CanSave)));
        confirm.Click += (_, _) => dialog.Close(true);
        var cancel = new Button { Name = "CancelSaveBeforeClose", Content = "Cancel; retain memory edits and window" };
        cancel.Click += (_, _) => dialog.Close(false);
        content.Children.Add(confirm);
        content.Children.Add(cancel);
        dialog.Content = new ScrollViewer { Content = content };
        discardDialog = dialog;
        try { return await dialog.ShowDialog<bool>(this); }
        finally { content.Children.Clear(); dialog.DataContext = null; discardDialog = null; }
    }
}
