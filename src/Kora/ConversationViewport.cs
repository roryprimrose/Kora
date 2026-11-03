using Avalonia;
using Avalonia.Controls;

using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora;

internal sealed class ConversationViewport
{
    private const double TailThreshold = 24;
    private readonly SessionInteractionView view;
    private readonly ScrollViewer scroll;
    private readonly ItemsControl messages;
    private readonly ItemsControl updates;
    private readonly TextBlock status;
    private readonly Dictionary<HostId<SessionIdentity>, ReadingPosition> positions = [];
    private SessionsViewModel? model;
    private TopLevel? host;
    private HostId<SessionIdentity>? session;
    private MessageAnchor[] source = [];
    private long tailRevision;
    private long presentation;
    private long controlRevision;
    private bool pending;
    private bool applying;
    private bool anchorUnavailable;
    private Vector? restoredOffset;

    private sealed record MessageAnchor(HostRequest Request, HostRevision Generation, long Sequence, long Revision)
    {
        internal static MessageAnchor From(ConversationMessage message) =>
            new(message.Request, message.Generation, message.Sequence, message.Revision);
    }

    private sealed record ReadingPosition(MessageAnchor? Anchor, double Offset, bool Following);

    internal ConversationViewport(SessionInteractionView view, ScrollViewer scroll, ItemsControl messages,
        ItemsControl updates, TextBlock status)
    {
        this.view = view;
        this.scroll = scroll;
        this.messages = messages;
        this.updates = updates;
        this.status = status;
        scroll.ScrollChanged += (_, args) =>
        {
            Refresh();
            if (applying || !view.IsConversationPresented) { return; }
            if (args.ExtentDelta != default || args.ViewportDelta != default) { pending = true; return; }
            if (restoredOffset == scroll.Offset) { return; }
            restoredOffset = null;
            if (!pending) { anchorUnavailable = false; Capture(); }
        };
        scroll.LayoutUpdated += (_, _) => Restore();
        view.AttachedToVisualTree += (_, _) =>
        {
            host = TopLevel.GetTopLevel(view);
            if (host is not null) { host.PropertyChanged += HostChanged; }
            Suspend();
        };
        view.DetachedFromVisualTree += (_, _) =>
        {
            Suspend();
            if (host is not null) { host.PropertyChanged -= HostChanged; }
            host = null;
        };
    }

    internal void Bind(SessionsViewModel? value)
    {
        if (ReferenceEquals(model, value)) { return; }
        positions.Clear();
        session = null;
        source = [];
        model = value;
        Suspend();
        Refresh();
    }

    internal void Suspend()
    {
        presentation++;
        pending = true;
    }

    internal Func<bool> CaptureAdmission()
    {
        var epoch = presentation;
        var original = model;
        var target = original?.SelectedSessionRecord;
        var revision = view.ConversationPresentationRevision;
        return () => view.IsConversationPresented && epoch == presentation && revision == view.ConversationPresentationRevision
            && ReferenceEquals(model, original) && original?.CanPresentDetails == true
            && target is not null && original.SelectedSessionRecord == target;
    }

    internal void Refresh()
    {
        if (model is not null && controlRevision != model.ConversationReaderControl)
        {
            positions.Clear();
            session = null;
            source = [];
            controlRevision = model.ConversationReaderControl;
            Suspend();
        }
        if (model?.CanPresentDetails != true)
        {
            positions.Clear();
            session = null;
            source = [];
            status.Text = string.Empty;
            Suspend();
            return;
        }
        var nextSession = model.SelectedSessionRecord?.Authority.SessionId;
        var next = model.ObservedConversationMessages.Select(MessageAnchor.From).ToArray();
        if (session != nextSession || !source.SequenceEqual(next) || tailRevision != model.ConversationTailRevision)
        {
            var deliberateTail = session == nextSession && tailRevision != model.ConversationTailRevision;
            session = nextSession;
            source = next;
            tailRevision = model.ConversationTailRevision;
            pending = true;
            anchorUnavailable = false;
            if (session is { } id && (deliberateTail || !positions.ContainsKey(id)))
            { positions[id] = new(null, 0, true); }
            status.Text = source.Length == 0 && session is { } current && positions[current].Anchor is not null
                ? "Read this exact conversation to restore its earlier reading position. No content was fetched on selection."
                : string.Empty;
        }
    }

    private void HostChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty) { Suspend(); }
    }

    private IEnumerable<Control> Containers()
    {
        foreach (var list in new[] { messages, updates })
        {
            for (var index = 0; index < list.ItemCount; index++)
            {
                if (list.ContainerFromIndex(index) is { } container) { yield return container; }
            }
        }
    }

    private void Capture()
    {
        if (session is not { } id || source.Length == 0 || !view.IsConversationPresented || anchorUnavailable) { return; }
        var following = scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y <= TailThreshold;
        foreach (var container in Containers())
        {
            if (container.DataContext is not ConversationMessage message
                || container.TranslatePoint(default, scroll) is not { } point
                || point.Y + container.Bounds.Height <= 0 || point.Y >= scroll.Viewport.Height) { continue; }
            positions[id] = new(MessageAnchor.From(message), point.Y, following);
            status.Text = following ? "Following the admitted conversation tail."
                : "Earlier reading position kept. New updates can be read deliberately without moving focus.";
            return;
        }
        if (positions.TryGetValue(id, out var previous)) { positions[id] = previous with { Following = following }; }
    }

    private void Restore()
    {
        Refresh();
        if (!pending || applying || !view.IsConversationPresented || session is not { } id || source.Length == 0
            || scroll.Viewport.Height <= 0 || !positions.TryGetValue(id, out var position)) { return; }
        applying = true;
        try
        {
            if (position.Following)
            {
                scroll.ScrollToEnd();
            }
            else if (position.Anchor is { } anchor)
            {
                var container = Containers().FirstOrDefault(candidate =>
                    candidate.DataContext is ConversationMessage message && MessageAnchor.From(message) == anchor);
                if (container?.TranslatePoint(default, scroll) is { } point)
                { scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + point.Y - position.Offset); }
                else
                {
                    anchorUnavailable = true;
                    status.Text = "The exact earlier message/revision is not on this admitted page. "
                        + "Read its page again or choose New updates; no replacement anchor was selected.";
                }
            }
            pending = false;
            restoredOffset = scroll.Offset;
            if (!anchorUnavailable)
            {
                status.Text = position.Following ? "Following the admitted conversation tail."
                    : "Earlier reading position kept. New updates can be read deliberately without moving focus.";
            }
        }
        finally { applying = false; }
    }
}
