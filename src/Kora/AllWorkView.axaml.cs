using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AllWorkView : UserControl, IDisposable
{
    private readonly NativeViewLifetime lifetime;
    private bool disposed;
    internal AllWorkViewModel Model { get; }
    internal event EventHandler? CloseRequested;
    private event EventHandler? Disposed;
    internal Func<bool> CapturePresentation() => lifetime.Capture();

    public AllWorkView() => throw new InvalidOperationException("Work overview requires an explicit host-owned view.");

    internal AllWorkView(AllWorkViewModel model, Window owner)
    {
        Model = model;
        InitializeComponent();
        DataContext = model;
        lifetime = new(this, owner, () => !disposed && model.CanEdit);
        model.SetPresentationOpen(lifetime.IsPresented);
        lifetime.PresentationChanged += (_, _) => model.SetPresentationOpen(lifetime.IsPresented);
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Refresh.Click += async (_, _) => await model.RefreshAsync();
        Search.Click += async (_, _) => await model.RefreshAsync();
        ClearQuery.Click += (_, _) => { model.ClearQuery(); MetadataQuery.Focus(); };
        ClearLoadedPageOrder.Click += (_, _) => { model.ClearOrder(); LoadedPageOrder.Focus(); };
        MetadataQuery.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                model.ClearQuery();
                args.Handled = true;
            }
            else if (args.Key == Key.Enter && model.CanSearch)
            {
                args.Handled = true;
                await model.RefreshAsync();
            }
        };
        Next.Click += async (_, _) => await model.RefreshAsync(next: true);
        CancelRead.Click += (_, _) => model.Cancel();
        StopAllSubmittedWork.Click += async (_, _) => await model.StopAllAsync();
        Navigate.Click += async (_, _) => await model.NavigateAsync();
        Records.SelectionChanged += (_, _) => model.Select(Records.SelectedItem as SessionWorkOverviewRow);
        CloseView.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        KeyDown += (_, args) =>
        {
            if (!args.Handled && args.Key == Key.Escape)
            { CloseRequested?.Invoke(this, EventArgs.Empty); args.Handled = true; }
        };
        InitializeFind(model);
    }

    private void UpdateResponsiveLayout()
    {
        WorkLayoutRoot.Height = Math.Max(600, Bounds.Height - 48);
        var narrow = Bounds.Width < 720;
        WorkPanes.ColumnDefinitions = new(narrow ? "*" : "360,*");
        WorkPanes.RowDefinitions = new(narrow ? "*,*" : "*");
        Grid.SetColumn(WorkMetadataPane, narrow ? 0 : 1);
        Grid.SetRow(WorkMetadataPane, narrow ? 1 : 0);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        lifetime.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
        Model.Close();
        DataContext = Model;
    }
}
