using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Agents;

/// <summary>Two independent original-user operations, never a scheduler or an intra-session workflow.</summary>
public sealed class AgentRunSet : IAsyncDisposable
{
    public enum AgentRunStatus { Reading, Reviewed, Resolving, Ready, Invoking, Completed, Stopping, Stopped, Failed }

    public sealed record AgentRunSnapshot(int Number, SessionWorkspaceEntry Target, AgentRunStatus Status,
        BoundedAgentReview? Review, ConversationSubmissionReceipt? Receipt);

    private sealed class Entry(int number, SessionWorkspaceEntry target, BoundedAgentWorkflow workflow,
        Dependencies.AgentLaneModelAdmission admission)
    {
        internal int Number { get; } = number;
        internal SessionWorkspaceEntry Target { get; } = target;
        internal BoundedAgentWorkflow Workflow { get; } = workflow;
        internal Dependencies.AgentLaneModelAdmission Admission { get; } = admission;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal Task Operation { get; set; } = Task.CompletedTask;
        internal Task? Stop { get; set; }
        internal BoundedAgentReview? Review { get; set; }
        internal ConversationSubmissionReceipt? Receipt { get; set; }
        internal AgentRunStatus Status { get; set; } = AgentRunStatus.Reading;
    }

    private readonly AgentRunSetFactory factory;
    private readonly Lock gate = new();
    private readonly Dictionary<int, Entry> entries = [];
    private bool closing;
    private Task? disposal;

    internal AgentRunSet(AgentRunSetFactory factory) => this.factory = factory;

    public AgentRunSnapshot? Read(int number)
    {
        RequireNumber(number);
        lock (gate)
        {
            return entries.TryGetValue(number, out var entry)
                ? new(number, entry.Target, entry.Status, entry.Review, entry.Receipt) : null;
        }
    }

    public Task ReviewAsync(int number, string path, SessionWorkspaceEntry target, string input,
        RequestOrigin origin, Func<bool> current, CancellationToken token)
    {
        RequireNumber(number);
        Entry entry;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (entries.ContainsKey(number)) { throw new InvalidOperationException("Stop and join this run before a fresh review."); }
            if (entries.Values.Any(item => item.Target.Authority.SessionId == target.Authority.SessionId))
            { throw new InvalidOperationException("Each independent run requires a distinct existing Active conversation."); }
            var created = factory.CreateWorkflow();
            entry = new(number, target, created.Workflow, created.Admission);
            entries.Add(number, entry);
        }
        return Start(entry, async () =>
        {
            var review = await entry.Workflow.ReviewAsync(path, target, input, origin,
                () => !entry.Cancellation.IsCancellationRequested && current(), entry.Cancellation.Token).ConfigureAwait(false);
            try { entry.Admission.Bind(review); }
            catch { await entry.Workflow.RevokeAsync(review).ConfigureAwait(false); throw; }
            lock (gate)
            {
                entry.Review = review;
                entry.Status = review.IsInvocationReady ? AgentRunStatus.Ready : AgentRunStatus.Reviewed;
            }
        }, token);
    }

    public Task ResolveAsync(int number, RequestOrigin origin, CancellationToken token)
    {
        var entry = Get(number);
        return Start(entry, async () =>
        {
            lock (gate) { entry.Status = AgentRunStatus.Resolving; }
            await entry.Workflow.ResolveAsync(RequireReview(entry), origin, entry.Cancellation.Token).ConfigureAwait(false);
            lock (gate) { entry.Status = AgentRunStatus.Ready; }
        }, token);
    }

    public Task InvokeAsync(int number, RequestOrigin origin, CancellationToken token)
    {
        var entry = Get(number);
        return Start(entry, async () =>
        {
            lock (gate) { entry.Status = AgentRunStatus.Invoking; }
            var receipt = await entry.Workflow.InvokeAsync(RequireReview(entry), origin, entry.Cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                entry.Receipt = receipt;
                entry.Status = AgentRunStatus.Completed;
            }
        }, token);
    }

    public void Revoke(int number)
    {
        RequireNumber(number);
        lock (gate)
        {
            if (!entries.TryGetValue(number, out var entry)) { return; }
            entry.Review?.Dispose();
            entry.Status = AgentRunStatus.Stopping;
            entry.Stop ??= StopCoreAsync(entry);
        }
    }

    public async Task StopAsync(int number)
    {
        RequireNumber(number);
        Entry? entry;
        lock (gate) { entries.TryGetValue(number, out entry); }
        if (entry is null) { return; }
        Revoke(number);
        try
        {
#pragma warning disable VSTHRD003 // This entry's retained stop joins both the operation and its canonical source owner.
            await entry.Stop!.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        finally
        {
            lock (gate) { if (entries.GetValueOrDefault(number) == entry) { entries.Remove(number); } }
        }
    }

    private Entry Get(int number)
    {
        RequireNumber(number);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            return entries.TryGetValue(number, out var entry) ? entry
                : throw new InvalidOperationException("Review this independent run first.");
        }
    }

    private static BoundedAgentReview RequireReview(Entry entry) =>
        entry.Review ?? throw new InvalidOperationException("Review the exact source, task and target first.");

    private Task Start(Entry entry, Func<Task> action, CancellationToken token)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (entry.Stop is not null || !entry.Operation.IsCompleted)
            { throw new InvalidOperationException("This exact run is still owned; wait for its actual resource join."); }
            entry.Operation = (completion = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        _ = CompleteAsync();
        return completion.Task;

        async Task CompleteAsync()
        {
            using var registration = token.Register(() => Revoke(entry.Number));
            try
            {
                token.ThrowIfCancellationRequested();
                await action().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                lock (gate) { if (entry.Stop is null) { entry.Status = AgentRunStatus.Failed; } }
                completion.TrySetException(exception);
            }
        }
    }

    private async Task StopCoreAsync(Entry entry)
    {
        List<Exception> failures = [];
        try { await entry.Cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        try
        {
#pragma warning disable VSTHRD003 // Join the exact operation published before callbacks or cancellation can run.
            await entry.Operation.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) { failures.Add(exception); }
        try { await entry.Workflow.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        finally
        {
            entry.Cancellation.Dispose();
            lock (gate)
            {
                entry.Status = failures.Count == 0 ? AgentRunStatus.Stopped : AgentRunStatus.Failed;
            }
        }
        if (failures.Count != 0) { throw new AggregateException("Independent run closure was not successful.", failures); }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (gate)
        {
            closing = true;
            return new(disposal ??= CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        Task[] pending;
        lock (gate)
        {
            var captured = entries.Values.ToArray();
            foreach (var entry in captured)
            {
                entry.Review?.Dispose();
                entry.Status = AgentRunStatus.Stopping;
                entry.Stop ??= StopCoreAsync(entry);
            }
#pragma warning disable VSTHRD003 // Select this owner's exact retained stop handles, not unrelated tasks.
            pending = captured.Select(item => item.Stop!).ToArray();
#pragma warning restore VSTHRD003
        }
#pragma warning disable VSTHRD003 // Aggregate every exact retained stop; none is an unrelated UI-dependent task.
        var joined = Task.WhenAll(pending);
#pragma warning restore VSTHRD003
        try { await joined.ConfigureAwait(false); }
        catch { throw joined.Exception!; }
    }

    private static void RequireNumber(int number)
    {
        if (number is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(number), "Select run 1 or run 2."); }
    }
}
