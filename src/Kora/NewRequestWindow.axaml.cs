using Avalonia.Controls;
using Avalonia.Input;

namespace Kora;

internal sealed partial class NewRequestWindow : Window
{
    private readonly NewRequestViewModel model;
    private bool privateClose;
    private bool choosing;
    private Window? discardDialog;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Func<Task<bool>>? DecideDiscard { get; set; }

    internal NewRequestWindow(NewRequestViewModel model, Func<Task> navigate, Action<Exception> navigationFailure)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        DecideDiscard = ChooseDiscard;
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
            if (privateClose) { return; }
            model.Composer.Cancel();
            if (!model.Composer.Dirty) { return; }
            args.Cancel = true;
            if (choosing) { return; }
            choosing = true;
            try
            {
                if (DecideDiscard is not null && await DecideDiscard() && IsVisible)
                { privateClose = true; Close(); }
            }
            finally { choosing = false; }
        };
        Closed += (_, _) =>
        {
            model.Dispose();
            DecideDiscard = null;
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
        model.Dispose();
        discardDialog?.Close();
        Close();
    }

    private async Task<bool> ChooseDiscard()
    {
        var dialog = new Window { Title = "Discard isolated New request draft?", Width = 660, Height = 240 };
        var content = new StackPanel { Margin = new(18), Spacing = 16 };
        content.Children.Add(new TextBlock
        {
            Text = "Discard this memory-only draft or Cancel closing. No Save or Submit. Any created empty session and retained request remain; closing is not rollback.\n" + model.TargetLabel + "\n" + model.AttemptLabel,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        var discard = new Button { Content = "Discard memory draft and close" };
        var cancel = new Button { Content = "Cancel closing" };
        discard.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        buttons.Children.Add(discard);
        buttons.Children.Add(cancel);
        content.Children.Add(buttons);
        dialog.Content = content;
        discardDialog = dialog;
        try { return await dialog.ShowDialog<bool>(this); }
        finally { content.Children.Clear(); discardDialog = null; }
    }
}
