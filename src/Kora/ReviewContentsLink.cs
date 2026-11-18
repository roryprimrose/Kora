using Avalonia.Controls;

using Kora.Core.Presentation;

namespace Kora;

/// <summary>
/// The "View contents" link owned by one review surface. It is enabled only while that review's
/// exact contents are current, and it never approves, enables or runs anything. The contents open
/// beside the conversation in the window that hosts the link.
/// </summary>
internal sealed class ReviewContentsLink : IDisposable
{
    private ReviewContents? contents;
    private Func<bool>? current;
    private PrivateReviewView? shown;
    private bool offered;

    internal ReviewContentsLink()
    {
        Button = ReviewContentsPresentation.CreateButton();
        Button.Click += (_, _) => Show();
    }

    internal Button Button { get; }
    internal ReviewContents? Contents => contents;
    internal PrivateReviewView? Shown => shown;
    internal bool IsCurrent => contents is not null && current?.Invoke() == true && TopLevel.GetTopLevel(Button) is Window;

    internal void Offer(ReviewContents reviewContents, Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(reviewContents);
        ArgumentNullException.ThrowIfNull(isCurrent);
        Retire();
        contents = reviewContents;
        current = isCurrent;
        ReviewContentsPresentation.Register(this);
        offered = true;
        Button.IsEnabled = isCurrent();
    }

    internal void Retire()
    {
        if (offered) { ReviewContentsPresentation.Unregister(this); offered = false; }
        shown?.Dispose();
        shown = null;
        contents = null;
        current = null;
        Button.IsEnabled = false;
    }

    internal bool Show()
    {
        if (!IsCurrent || TopLevel.GetTopLevel(Button) is not Window owner)
        {
            Retire();
            return false;
        }
        shown?.Dispose();
        shown = ReviewContentsPresentation.Show(owner, contents!, current!);
        return true;
    }

    public void Dispose() => Retire();
}
