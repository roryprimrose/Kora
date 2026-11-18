using Avalonia.Controls;

using Kora.Core.Presentation;

namespace Kora;

/// <summary>
/// Opens the read-only contents of an item under review beside the conversation. The originating
/// review supplies its own currency check, so retiring that review also retires the contents view.
/// Reviews register their link while current so the typed or spoken "show contents" phrase opens
/// the most recent one through the same route as the link itself.
/// </summary>
internal static class ReviewContentsPresentation
{
    internal const string ActionLabel = "View contents";

    private static readonly List<ReviewContentsLink> links = [];

    internal static PrivateReviewView Show(Window owner, ReviewContents contents, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(current);
        if (!current()) { throw new InvalidOperationException("The original review is no longer current. Review the item again before viewing its contents."); }
        // A standalone review window hosts the review itself, so its contents open in their own window.
        var host = owner is ResponseWindow ? owner : CreateStandaloneHost(owner);
        var review = new PrivateReviewView(host, new ReviewContentsView(contents), current);
        review.Show("Contents - " + contents.Title);
        return review;
    }

    private static Window CreateStandaloneHost(Window owner)
    {
        var host = new Window { Width = 960, Height = 680, MinWidth = 480, MinHeight = 320, Icon = owner.Icon };
        void CloseHost(object? sender, EventArgs args) => host.Close();
        owner.Closed += CloseHost;
        host.Closed += (_, _) => owner.Closed -= CloseHost;
        return host;
    }

    internal static bool TryShowLatest()
    {
        for (var index = links.Count - 1; index >= 0; index--)
        {
            var link = links[index];
            if (link.IsCurrent) { return link.Show(); }
            link.Retire();
        }
        return false;
    }

    internal static Button CreateButton()
    {
        var button = new Button { Content = ActionLabel, IsEnabled = false };
        button.Classes.Add("response-link");
        Avalonia.Automation.AutomationProperties.SetName(button, "View the exact read-only contents of the item under review");
        return button;
    }

    internal static void Register(ReviewContentsLink link)
    {
        links.Remove(link);
        links.Add(link);
    }

    internal static void Unregister(ReviewContentsLink link) => links.Remove(link);
}
