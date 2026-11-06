using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Application.Context;

/// <summary>One volatile original question and exact source; neither retained content nor reusable consent.</summary>
public sealed class ClipboardQuestionReview
{
    private readonly Func<bool> current;
    private int consumed;
    private int retired;

    internal ClipboardQuestionReview(ClipboardSnapshot source, string question, HostRequest request, Func<bool> current)
    {
        Source = source;
        Question = question;
        Request = request;
        this.current = current;
    }

    public ClipboardSnapshot Source { get; }
    public string Question { get; }
    public HostRequest Request { get; }
    public bool IsCurrent()
    {
        if (Volatile.Read(ref retired) != 0) { return false; }
        if (current()) { return Volatile.Read(ref retired) == 0; }
        Retire();
        return false;
    }
    public void Retire() => Interlocked.Exchange(ref retired, 1);
    internal bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0;
}
