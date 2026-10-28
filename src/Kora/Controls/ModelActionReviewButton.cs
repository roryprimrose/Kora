using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Kora.Application.Presentation;

namespace Kora.Controls;

public sealed class ModelActionReviewButton : Button
{
    private readonly TimeProvider time;
    private long presentedAt;
    private ModelActionReviewTarget? pressedTarget;

    public ModelActionReviewButton() : this(TimeProvider.System) { }

    internal ModelActionReviewButton(TimeProvider time) => this.time = time;

    protected override Type StyleKeyOverride => typeof(Button);

    public ModelActionReviewTarget? CapturedTarget =>
        this.IsAttachedToVisualTree() && time.GetElapsedTime(presentedAt) >= TimeSpan.FromMilliseconds(500)
        && ReferenceEquals(pressedTarget, DataContext) ? pressedTarget : null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == DataContextProperty)
        {
            presentedAt = time.GetTimestamp();
            pressedTarget = null;
        }
        base.OnPropertyChanged(change);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        pressedTarget = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        presentedAt = time.GetTimestamp();
        pressedTarget = null;
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        pressedTarget = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && time.GetElapsedTime(presentedAt) >= TimeSpan.FromMilliseconds(500)
            ? DataContext as ModelActionReviewTarget : null;
        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            pressedTarget = time.GetElapsedTime(presentedAt) >= TimeSpan.FromMilliseconds(500)
                ? DataContext as ModelActionReviewTarget : null;
        }
        base.OnKeyDown(e);
    }
}
