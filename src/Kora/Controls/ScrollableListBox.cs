using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Kora.Controls;

public sealed class ScrollableListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new ScrollableListBoxAutomationPeer(this);

    private sealed class ScrollableListBoxAutomationPeer(ScrollableListBox owner) : ListBoxAutomationPeer(owner), IScrollProvider
    {
        // Avalonia 12.1.3's items peer advertises scrolling without initializing its scroller.
        // The native bridge discovers providers off-thread but invokes their members on the UI thread.
        // Resolve the current template only during those invocations, never during provider discovery.
        private IScrollProvider CurrentScroller => owner.Scroll is Control scroll
            && CreatePeerForElement(scroll)?.GetProvider<IScrollProvider>() is { } provider
                ? provider
                : throw new InvalidOperationException("The list template has no available scroll provider.");

        bool IScrollProvider.HorizontallyScrollable => CurrentScroller.HorizontallyScrollable;
        double IScrollProvider.HorizontalScrollPercent => CurrentScroller.HorizontalScrollPercent;
        double IScrollProvider.HorizontalViewSize => CurrentScroller.HorizontalViewSize;
        bool IScrollProvider.VerticallyScrollable => CurrentScroller.VerticallyScrollable;
        double IScrollProvider.VerticalScrollPercent => CurrentScroller.VerticalScrollPercent;
        double IScrollProvider.VerticalViewSize => CurrentScroller.VerticalViewSize;
        void IScrollProvider.Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount) =>
            CurrentScroller.Scroll(horizontalAmount, verticalAmount);
        void IScrollProvider.SetScrollPercent(double horizontalPercent, double verticalPercent) =>
            CurrentScroller.SetScrollPercent(horizontalPercent, verticalPercent);
    }
}
