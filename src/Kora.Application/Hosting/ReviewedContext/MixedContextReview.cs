using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Kora.Application.Memory;
using Kora.Core.Conversation;
using Kora.Core.Conversation.ReviewedContext;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.ReviewedContext;

internal sealed class MixedContextReview : IDisposable
{
    private readonly Lock gate = new();
    private readonly MixedContextWorkflow owner;
    private ScopedInstructionUseBroker.InstructionSourceReview? instruction;
    private ImmutableArray<ConversationHistorySelection> history;
    private string text = string.Empty;
    private bool disposed;
    private bool consumed;

    internal MixedContextReview(MixedContextWorkflow owner, ScopedInstructionUseBroker.InstructionSourceReview instruction,
        SessionMemoryContextReview memory, ImmutableArray<ConversationHistorySelection> history,
        TimeProvider time, ActivityLink cause)
    {
        this.owner = owner;
        this.instruction = instruction;
        Memory = memory;
        this.history = history;
        Cause = cause;
        var envelope = CreateEnvelope(new(new(Guid.NewGuid()), Target.Authority.SessionId, new(Guid.NewGuid()), Origin),
            time.GetUtcNow());
        var preview = new StringBuilder("ONE local-only tool-free answer. Sources remain lower-trust content; no automatic recall, "
            + "source promotion, remembering, grants or hosted transfer. Only the new request and genuine answer/HOST outcome "
            + "are retained, not copies of selected source bodies.\n")
            .Append("Complete serialized input: ").Append(envelope.Serialize().Length.ToString(CultureInfo.InvariantCulture))
            .Append(" / 32768 UTF-8 bytes; ").Append(envelope.Evidence.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" / 16 evidence items.\n\n");
        foreach (var item in history)
        {
            preview.Append(item.Role == ConversationHistoryRole.User ? "EARLIER USER\n" : "EARLIER ASSISTANT\n")
                .Append(item.Role == ConversationHistoryRole.User ? item.Message.Body : item.Message.AssistantResponse!.Body)
                .Append("\n\n");
        }
        preview.Append("COMPLETE SELECTED INSTRUCTION: ").Append(instruction.Name).Append('\n')
            .Append(instruction.InstructionText).Append("\n\n");
        foreach (var record in memory.Records)
        {
            preview.Append("COMPLETE SESSION MEMORY\n")
                .Append(JsonSerializer.Serialize(new { record.Scope, record.Candidate })).Append("\n\n");
        }
        text = preview.Append("UNCHANGED NEW USER REQUEST\n").Append(memory.UserRequest).ToString();
        memory.Retired += Dispose;
    }

    internal ScopedInstructionUseBroker.InstructionSourceReview Instruction => instruction
        ?? throw new InvalidOperationException("The private instruction source was retired.");
    internal SessionMemoryContextReview Memory { get; }
    internal ActivityLink Cause { get; }
    internal event Action? Retired;
    internal SessionWorkspaceEntry Target => Memory.Target;
    internal RequestOrigin Origin => Memory.Origin;
    internal ImmutableArray<ConversationHistorySelection> History { get { lock (gate) { return history; } } }
    internal bool Current
    {
        get
        {
            lock (gate)
            {
                if (disposed) { return false; }
                if (Memory.Current && Instruction.IsCurrent()) { return true; }
                Dispose();
                return false;
            }
        }
    }
    internal bool CanConfirm { get { lock (gate) { return !consumed && Current; } } }
    internal string Text { get { lock (gate) { return Current ? text : string.Empty; } } }
    internal string Body => Memory.ReadCurrent().Body;

    internal void Consume(MixedContextWorkflow workflow)
    {
        lock (gate)
        {
            if (!ReferenceEquals(owner, workflow) || !CanConfirm)
            { throw new InvalidOperationException("Review the complete unchanged sources and request again; consent is single-use."); }
            consumed = true;
        }
    }

    internal ModelContextEnvelope CreateEnvelope(HostRequest request, DateTimeOffset now)
    {
        lock (gate)
        {
            if (!Current || request.SessionId != Target.Authority.SessionId || request.Origin != Origin)
            { throw new InvalidOperationException("The exact original mixed-context session/channel is no longer current."); }
            var content = Memory.ReadCurrent();
            return MixedConversationContext.CreateEnvelope(request, content.Body, history, Instruction.Name,
                Instruction.InstructionText, content.Snapshot.Boundary, content.Snapshot.Records, now, now.AddSeconds(15));
        }
    }

    public void Dispose()
    {
        ScopedInstructionUseBroker.InstructionSourceReview source;
        lock (gate)
        {
            if (disposed) { return; }
            source = Instruction;
            disposed = true;
            text = string.Empty;
            history = [];
            instruction = null;
        }
        Memory.Retired -= Dispose;
        source.Retire();
        Memory.Dispose();
        var notify = Retired;
        Retired = null;
        notify?.Invoke();
    }
}
