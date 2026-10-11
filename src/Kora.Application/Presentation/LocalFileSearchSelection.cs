using Kora.Core.Context;

namespace Kora.Application.Presentation;

public sealed class LocalFileSearchSelection : IDisposable
{
    private readonly Lock sync = new();
    private CancellationTokenSource? pending;
    private long generation;
    private bool disposed;
    private LocalFileSearchResult? currentResult;
    public LocalFileRetrievalMode Mode { get; private set; }

    public void Select(LocalFileRetrievalMode mode)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Invalidate();
            Mode = mode;
        }
        if (!LocalFileRetrievalPolicy.TryVersion(mode, out _))
        {
            throw new InvalidOperationException("This retrieval mode is unsupported; no fallback search was selected.");
        }
    }

    public void Invalidate()
    {
        lock (sync)
        {
            generation++;
            pending?.Cancel();
            currentResult = null;
        }
    }

    public async Task<LocalFileSearchResult?> Run(string query,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search, Func<bool> isCurrent)
    {
        Invalidate();
        CancellationTokenSource cancellation;
        long captured;
        LocalFileRetrievalMode mode;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            captured = ++generation;
            mode = Mode;
            if (!LocalFileRetrievalPolicy.TryVersion(mode, out _))
            {
                throw new InvalidOperationException("Select a supported retrieval mode before searching.");
            }
            pending = cancellation = new();
        }
        try
        {
            var result = await search(query, mode, cancellation.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (disposed || captured != generation || cancellation.IsCancellationRequested || !isCurrent()) { return null; }
                _ = LocalFileRetrievalPolicy.TryVersion(mode, out var version);
                if (result.Mode != mode || !string.Equals(result.PolicyVersion, version, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The search policy does not match this exact selected run.");
                }
                currentResult = result;
                return result;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        catch (Exception) when (IsRetired(captured, isCurrent)) { return null; }
        finally
        {
            lock (sync) { if (ReferenceEquals(pending, cancellation)) { pending = null; } }
            cancellation.Dispose();
        }
    }

    public bool IsCurrent(LocalFileSearchResult result, Func<bool> isCurrent)
    {
        lock (sync) { return !disposed && ReferenceEquals(currentResult, result) && isCurrent(); }
    }

    public LocalFileCitationSpan Resolve(LocalFileCitation citation, IReadOnlyList<LocalFileRevision> files,
        Func<bool> isCurrent)
    {
        lock (sync)
        {
            if (disposed || !isCurrent() || currentResult is not { Outcome: LocalFileSearchOutcome.Matched } result
                || !result.Citations.Any(row => ReferenceEquals(row, citation)))
            {
                throw new InvalidOperationException("Select a citation from the current successful admitted search.");
            }
            var file = files.SingleOrDefault(item => item.Reference == citation.Source)
                ?? throw new InvalidOperationException("The exact cited admitted item is no longer available.");
            return LocalFileLexicalRetrieval.ResolveCitation(file, citation, Mode);
        }
    }

    private bool IsRetired(long captured, Func<bool> isCurrent)
    {
        lock (sync) { return disposed || generation != captured || !isCurrent(); }
    }

    public void Dispose()
    {
        lock (sync) { disposed = true; }
        Invalidate();
    }
}
