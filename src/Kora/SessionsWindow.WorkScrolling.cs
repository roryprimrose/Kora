using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private bool revealingWorkTarget;

    private void BindWorkScrolling()
    {
        SelectedSessionWorkPane.AddHandler(RequestBringIntoViewEvent, (_, request) =>
        {
            if (revealingWorkTarget || !request.Handled || request.TargetObject is not { } target
                || SelectedSessionWorkPane.Presenter is not ScrollContentPresenter outer)
            { return; }
            var presenters = target.GetVisualAncestors().OfType<ScrollContentPresenter>().ToArray();
            var boundary = Array.IndexOf(presenters, outer);
            if (boundary < 0) { return; }
            revealingWorkTarget = true;
            try
            {
                // An inner presenter can handle a request while its viewport is outside its parent.
                // Arrange each scroller before translating the same target into the next viewport.
                for (var index = 0; index <= boundary; index++)
                {
                    presenters[index].BringDescendantIntoView(target, request.TargetRect);
                    UpdateLayout();
                }
            }
            finally { revealingWorkTarget = false; }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
