using Kora.Application.Presentation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

public sealed partial class ResponseWindowController : IAutomaticDetailPresentation
{
    private long automaticWindowEpoch;

    private void ObserveAutomaticDetailsWindow(ResponseWindow current)
    {
        current.AddHandler(InputElement.KeyDownEvent, (_, _) => automaticWindowEpoch++, RoutingStrategies.Tunnel, handledEventsToo: true);
        current.AddHandler(InputElement.PointerPressedEvent, (_, _) => automaticWindowEpoch++, RoutingStrategies.Tunnel, handledEventsToo: true);
        current.PropertyChanged += (_, args) =>
        {
            if (args.Property == Window.IsActiveProperty || args.Property == Visual.IsVisibleProperty)
            { automaticWindowEpoch++; }
        };
    }

    AutomaticDetailLease? IAutomaticDetailPresentation.CaptureAutomaticDetails()
    {
        if (disposed || details is null || window is not { IsVisible: true, IsActive: true, IsSessionListPresented: true } current
            || current.SessionView.DataContext is not SessionsViewModel model || !viewModel.CanRevealPrivatePresentation)
        { return null; }
        var host = viewModel.CaptureAutomaticDetailsAdmission();
        var native = current.SessionView.CaptureAutomaticDetails(model);
        var epoch = automaticWindowEpoch;
        if (native is null || !host()) { return null; }
        return native with { Current = () => !disposed && ReferenceEquals(window, current)
            && current.IsVisible && current.IsActive && current.IsSessionListPresented
            && epoch == automaticWindowEpoch
            && viewModel.CanRevealPrivatePresentation && host() && native.Current(),
            SourceCurrent = () => !disposed && ReferenceEquals(window, current) && current.IsVisible
                && viewModel.CanRevealPrivatePresentation && native.SourceCurrent() };
    }

    bool IAutomaticDetailPresentation.PublishAutomaticDetails(AutomaticDetailIntent intent)
    {
        if (disposed || details is null || window is not { IsVisible: true, IsActive: true } current
            || !intent.TryConsume()) { return false; }
        return details.PublishAutomaticDetails(intent, current);
    }
}
