using Avalonia.Controls;

namespace Kora;

internal sealed partial class SessionPermissionControls : UserControl
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public SessionPermissionControls()
    {
        InitializeComponent();
#pragma warning disable VSTHRD101 // Framework Click boundary; model operations catch, log and present failures through RunAsync.
        ReadPolicy.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ReadPermissionAsync()); }
        };
        ReviewPolicy.Click += (_, _) => (DataContext as SessionsViewModel)?.ReviewPermission();
        ConfirmPolicy.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ConfirmPermissionAsync()); }
        };
#pragma warning restore VSTHRD101
        CancelPolicy.Click += (_, _) => (DataContext as SessionsViewModel)?.CancelPermissionReview();
        DetachedFromVisualTree += (_, _) => (DataContext as SessionsViewModel)?.CancelPermissionReview();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            { (DataContext as SessionsViewModel)?.CancelPermissionReview(); }
        };
    }
}
