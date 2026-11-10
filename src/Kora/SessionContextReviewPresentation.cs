using Avalonia.Controls;
using Avalonia;

namespace Kora;

internal sealed class SessionContextReviewPresentation : IDisposable
{
    private readonly Control view;
    private readonly Window owner;
    private readonly Func<bool> sourceAvailable;
    private readonly Action retire;
    private readonly string title;
    private readonly NativeViewLifetime lifetime;
    private readonly Func<bool> session;
    private bool disposed;
    internal event EventHandler? Closed;
    internal event EventHandler? Retired;

    internal SessionContextReviewPresentation(Control view, Window owner, SessionsViewModel model,
        Func<bool> sourceAvailable, Action retire, string title)
    {
        this.view = view;
        this.owner = owner;
        this.sourceAvailable = sourceAvailable;
        this.retire = retire;
        this.title = title;
        session = model.CaptureNavigationEligibility();
        lifetime = new(view, owner, () => !disposed && model.CanPresentDetails && ReferenceEquals(view.DataContext, model));
        lifetime.PresentationChanged += (_, _) =>
        {
            if (!lifetime.IsPresented) { Retire(); }
        };
        owner.Closed += OnOwnerClosed;
        view.PropertyChanged += OnViewChanged;
    }

    internal void Show()
    {
        if (disposed || !session() || !sourceAvailable())
        { throw new InvalidOperationException("The original context review is retired. Explicitly open a fresh review from its exact session."); }
        if (owner is ResponseWindow primary)
        {
            if (lifetime.IsActive) { view.BringIntoView(); view.Focus(); return; }
            primary.ShowNativeReview(view, title, view, () => !disposed && session() && sourceAvailable(),
                shown => { if (!shown) { Retire(); } });
        }
        else { owner.Activate(); }
    }

    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();
    private void OnViewChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == StyledElement.DataContextProperty && !disposed)
        {
            lifetime.Refresh();
            Retire();
        }
    }
    private void Retire()
    {
        retire();
        Retired?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        owner.Closed -= OnOwnerClosed;
        view.PropertyChanged -= OnViewChanged;
        if (owner is ResponseWindow primary) { primary.RemoveNativeReview(view); }
        lifetime.Dispose();
        Retire();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
