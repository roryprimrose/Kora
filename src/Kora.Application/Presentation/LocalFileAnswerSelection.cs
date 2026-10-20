using Kora.Core.Context;

namespace Kora.Application.Presentation;

public sealed class LocalFileAnswerSelection : IDisposable
{
    private readonly Lock sync = new();
    private long generation;
    private CancellationTokenSource? pending;
    private LocalFileAnswerResult? result;
    private bool disposed;

    public void Invalidate()
    {
        lock (sync)
        {
            generation++;
            pending?.Cancel();
            result = null;
        }
    }

    public async Task<LocalFileAnswerResult?> Run(string question,
        Func<string, Func<bool>, CancellationToken, Task<LocalFileAnswerResult>> answer, Func<bool> isCurrent)
    {
        Invalidate();
        CancellationTokenSource cancellation;
        long captured;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            captured = generation;
            pending = cancellation = new();
        }
        bool Current()
        {
            bool active;
            lock (sync) { active = !disposed && captured == generation; }
            return active && isCurrent();
        }
        try
        {
            var response = await answer(question, Current, cancellation.Token).ConfigureAwait(false);
            if (!Current()) { return null; }
            lock (sync)
            {
                if (disposed || captured != generation) { return null; }
                result = response;
                return response;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        finally
        {
            lock (sync) { if (ReferenceEquals(pending, cancellation)) { pending = null; } }
            cancellation.Dispose();
        }
    }

    public LocalFileCitationSpan Resolve(LocalFileCitation citation, IReadOnlyList<LocalFileRevision> files, Func<bool> isCurrent)
    {
        if (!isCurrent()) { throw new InvalidOperationException("The file answer source is no longer current."); }
        lock (sync)
        {
            if (disposed || result is not { Outcome: LocalFileAnswerOutcome.Succeeded }
                || !result.Claims.SelectMany(claim => claim.Citations).Any(row => ReferenceEquals(row, citation)))
            {
                throw new InvalidOperationException("Select an exact citation from the current successful file answer.");
            }
            var file = files.SingleOrDefault(item => item.Reference == citation.Source)
                ?? throw new InvalidOperationException("The exact cited file revision is no longer admitted.");
            return LocalFileLexicalRetrieval.ResolveCitation(file, citation, LocalFileRetrievalMode.LexicalLines);
        }
    }

    public void Dispose()
    {
        lock (sync) { disposed = true; }
        Invalidate();
    }
}
