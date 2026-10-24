using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Owns one pending review and joins all source, metadata and model work before release.</summary>
public sealed class InstructionUseWorkflow(ScopedInstructionUseBroker broker, SessionWorkspaceService workspace,
    SessionConversationService conversation, IModelTurnExecution execution, TimeProvider time) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private InstructionUseReview? pending;
    private CancellationTokenSource? cancellation;
    private Task running = Task.CompletedTask;
    private long revision;
    private bool closed;

    public Task<InstructionUseReview> ReviewAsync(string sourceName, string targetName, string body,
        RequestOrigin origin, Func<bool> eligible, CancellationToken token) =>
        RunAsync(async (current, cancellationToken) =>
        {
            SessionConversationService.RequireOriginalSource(origin);
            ConversationText.Validate(body, submission: true);
            var choices = broker.Choices.Where(item => string.Equals(item.OwnedInstructionName, sourceName,
                StringComparison.OrdinalIgnoreCase) || string.Equals(item.Name, sourceName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (choices.Length != 1)
            { throw new InvalidOperationException("Choose one uniquely named available enabled owned instruction; no source was substituted."); }
            SessionWorkspaceEntry? selected = null;
            Guid? after = null;
            HashSet<Guid> cursors = [];
            do
            {
                Require(current, cancellationToken);
                var page = await workspace.ReadMetadataAsync(after, 25, cancellationToken).ConfigureAwait(false);
                Require(current, cancellationToken);
                foreach (var candidate in page.Records.Where(item => item.Authority.IsActive
                    && string.Equals(item.Metadata?.Name.Value, targetName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (selected is not null)
                    { throw new InvalidOperationException("The Active conversation name is ambiguous. Give the intended conversation a unique name first."); }
                    selected = candidate;
                }
                after = page.Next;
                if (after is { } cursor && !cursors.Add(cursor))
                { throw new InvalidDataException("Conversation metadata paging did not advance."); }
            } while (after is not null);
            var target = selected ?? throw new InvalidOperationException("Choose an existing uniquely named Active conversation; none was created or resumed.");
            var exact = await workspace.ReadExactMetadataAsync(target.Authority.SessionId, cancellationToken).ConfigureAwait(false);
            Require(current, cancellationToken);
            if (exact != target) { throw new InvalidOperationException("The selected conversation changed. Review again; no replacement was chosen."); }
            var expires = time.GetUtcNow().AddMinutes(2);
            var source = await broker.ReviewAsync(exact, choices[0], body, current, cancellationToken, origin).ConfigureAwait(false);
            lock (gate)
            {
                if (!current() || cancellationToken.IsCancellationRequested)
                {
                    source.Retire();
                    throw new OperationCanceledException("Original instruction review admission was retired.");
                }
                pending = new(source, expires);
                return pending;
            }
        }, eligible, token, reviewing: true);

    public Task<ConversationSubmissionReceipt> ConfirmAsync(Guid expectedReview, Func<bool> eligible, CancellationToken token) =>
        RunAsync(async (current, cancellationToken) =>
        {
            SessionConversationService.RequireOriginalSource(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
            InstructionUseReview review;
            lock (gate)
            {
                review = pending is { } value && value.Id == expectedReview && value.CanConfirm
                    ? value : throw new InvalidOperationException("The exact pending instruction review is stale, absent or already consumed.");
                pending = null;
            }
            ActivityLink[] links = HostActivity.Current?.Activity is { } parent ? [new(parent.Context)] : [];
            using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Source.Target.Authority.SessionId,
                new(Guid.NewGuid()), review.Source.Origin), HostActivityLayer.Application, HostOperation.Request, links);
            try
            {
                ConversationSubmissionReceipt receipt;
                try
                {
                    receipt = await conversation.SubmitInstructionAsync(review.Source, current, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    review.Source.Retire();
                    await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false);
                }
                Require(current, cancellationToken);
                activity.Complete(receipt.Message.Outcome == ModelTurnOutcome.Succeeded
                    ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
                return receipt;
            }
            catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
            catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        }, eligible, token);

    private Task<T> RunAsync<T>(Func<Func<bool>, CancellationToken, Task<T>> operation, Func<bool> eligible,
        CancellationToken token, bool reviewing = false)
    {
        TaskCompletionSource completion;
        CancellationTokenSource source;
        long captured;
        lock (gate)
        {
            if (closed || cancellation is not null || reviewing && pending is not null)
            { throw new InvalidOperationException("Finish or cancel the current instruction operation before starting another."); }
            source = CancellationTokenSource.CreateLinkedTokenSource(token);
            cancellation = source;
            captured = revision;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            running = completion.Task;
        }
        bool Current() { lock (gate) { return !closed && revision == captured && eligible(); } }
        return ExecuteAsync();

        async Task<T> ExecuteAsync()
        {
            try
            {
                Require(Current, source.Token);
                return await operation(Current, source.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (gate)
                {
                    cancellation = null;
                    source.Dispose();
                    completion.SetResult();
                }
            }
        }
    }

    public Task StopAsync()
    {
        lock (gate)
        {
            revision++;
            pending?.Source.Retire();
            pending = null;
            cancellation?.Cancel();
#pragma warning disable VSTHRD003 // Return only the completion published by this workflow before starting its owned work.
            return running;
#pragma warning restore VSTHRD003
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) { closed = true; }
        await StopAsync().ConfigureAwait(false);
    }

    private static void Require(Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("Original private instruction-use admission expired or was revoked."); }
    }
}
