using Kora.Core.KnowledgeSources;

namespace Kora.Application.KnowledgeSources;

/// <summary>Retains native source ownership through actual model completion, including cancellation-ignoring work.</summary>
internal sealed class KnowledgeSourceAnswerUse(KnowledgeSource source, KnowledgeSourceRevision revision,
    IKnowledgeSourceSelection access, IKnowledgeSourceStore store, Func<bool> eligible, TimeProvider time)
    : IDisposable, IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly CancellationTokenSource revocation = new();
    private TaskCompletionSource? work;
    private bool revoked;
    private bool disposed;
    internal KnowledgeSourceRevision Revision { get; } = revision;
    internal KnowledgeSourceAnswerReview? Review { get; set; }
    internal bool IsCurrent
    {
        get { lock (sync) { return !revoked && eligible() && (Review is null || Review.IsCurrent(time.GetUtcNow())); } }
    }

    internal async Task ValidateAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, revocation.Token);
        token = linked.Token;
        try
        {
            RequireCurrent(token);
            if (source.State != KnowledgeSourceState.Enabled || Revision.Reference.SourceId != source.Id
                || !source.Folder.Matches(Revision.Review.Metadata))
            { throw new InvalidDataException("The exact enabled retained source is required."); }
            await access.ValidateAsync(token).ConfigureAwait(false);
            RequireCurrent(token);
            var current = await store.ReadAsync(source, token).ConfigureAwait(false);
            RequireCurrent(token);
            if (current.Reference != Revision.Reference)
            { throw new InvalidDataException("The retained source revision changed."); }
        }
        catch
        {
            Revoke();
            throw;
        }
    }

    internal async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        TaskCompletionSource joined;
        lock (sync)
        {
            RequireCurrent(token);
            if (work is not null) { throw new InvalidOperationException("This source use is already executing."); }
            work = joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, revocation.Token);
        try { return await action(linked.Token).ConfigureAwait(false); }
        finally
        {
            lock (sync) { work = null; }
            joined.SetResult();
        }
    }

    private void RequireCurrent(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrent) { throw new InvalidOperationException("Named source use was revoked."); }
    }

    private void Revoke()
    {
        lock (sync)
        {
            if (disposed) { return; }
            revoked = true;
            Review?.Revoke();
            revocation.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Revoke();
        Task? completion;
        lock (sync) { completion = work?.Task; }
#pragma warning disable VSTHRD003 // Joins only the owned worker; no caller or dispatcher work is awaited here.
        if (completion is not null) { await completion.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
        Dispose();
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) { return; }
            if (work is not null) { throw new InvalidOperationException("Join the model use before releasing source ownership."); }
            Revoke();
            access.Dispose();
            revocation.Dispose();
            disposed = true;
        }
    }
}
