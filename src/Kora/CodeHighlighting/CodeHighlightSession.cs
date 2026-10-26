namespace Kora.CodeHighlighting;

internal sealed class CodeHighlightSession(
    Func<CodeSource, CancellationToken, CodeProjection>? tokenize = null)
{
    private readonly Func<CodeSource, CancellationToken, CodeProjection> tokenize =
        tokenize ?? new OfflineCodeHighlighter().Highlight;
    private readonly List<Task<CodeProjection>> workers = [];
    private CancellationTokenSource? cancellation;
    private long generation;

    // Called on the owning UI dispatcher. Retired workers remain joinable until they actually stop.
    internal async Task<CodeProjection?> RenderAsync(CodeSource source)
    {
        Retire();
        var admittedGeneration = generation;
        var ownedCancellation = new CancellationTokenSource();
        cancellation = ownedCancellation;
        var worker = Task.Run(() => Execute(source, ownedCancellation.Token), CancellationToken.None);
        workers.Add(worker);
        try
        {
            var result = await worker;
            return admittedGeneration == generation ? result : null;
        }
        finally
        {
            workers.Remove(worker);
            if (ReferenceEquals(cancellation, ownedCancellation)) { cancellation = null; }
            ownedCancellation.Dispose();
        }
    }

    internal void Retire()
    {
        generation++;
        var previous = cancellation;
        cancellation = null;
        previous?.Cancel();
    }

    internal async Task JoinAsync()
    {
        Retire();
        await Task.WhenAll(workers.ToArray());
    }

    private CodeProjection Execute(CodeSource source, CancellationToken token)
    {
        try { return tokenize(source, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(source, CodeHighlightOutcome.Cancelled, []); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Failure is a visible typed outcome, never a successful render or a source-bearing log.
            return new(source, CodeHighlightOutcome.Failed, []);
        }
    }
}
