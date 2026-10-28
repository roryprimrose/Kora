using Kora.Core.Hosting;

namespace Kora.Application.Presentation;

public sealed class FinalDocumentNavigationRequest
{
    private int consumed;

    internal FinalDocumentNavigationRequest(FinalDocumentSource source, HostRequest action,
        int sourceStart, string sourceTitle, string sourceToken, string destination)
    {
        Source = source;
        UserAction = action;
        SourceStart = sourceStart;
        SourceTitle = sourceTitle;
        SourceToken = sourceToken;
        Destination = destination;
    }

    public FinalDocumentSource Source { get; }
    public HostRequest UserAction { get; }
    public int SourceStart { get; }
    public string SourceTitle { get; private set; }
    public string SourceToken { get; private set; }
    public string Destination { get; private set; }
    public bool IsCurrent => Volatile.Read(ref consumed) != 2 && Source.IsCurrent;
    internal bool TryConsume(HostRequest action) =>
        action == UserAction && Interlocked.CompareExchange(ref consumed, 1, 0) == 0;

    public void Retire()
    {
        Interlocked.Exchange(ref consumed, 2);
        SourceTitle = string.Empty;
        SourceToken = string.Empty;
        Destination = string.Empty;
    }
}
