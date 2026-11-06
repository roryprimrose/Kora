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
    private bool validated;
    private ImmutableArray<MixedContextContribution> contributions;

    internal MixedContextReview(MixedContextWorkflow owner, ScopedInstructionUseBroker.InstructionSourceReview instruction,
        SessionMemoryContextReview memory, ImmutableArray<ConversationHistorySelection> history,
        TimeProvider time, ActivityLink cause)
    {
        this.owner = owner;
        this.instruction = instruction;
        Memory = memory;
        this.history = history;
        Cause = cause;
        var request = new HostRequest(new(Guid.NewGuid()), Target.Authority.SessionId, new(Guid.NewGuid()), Origin);
        var now = time.GetUtcNow();
        var content = memory.ReadCurrent();
        int Measure(IEnumerable<ConversationHistorySelection> messages, IEnumerable<Kora.Core.Memory.MemoryRecord> records) =>
            MixedConversationContext.MeasureInput(request, content.Body, messages, instruction.Name,
                instruction.InstructionText, content.Snapshot.Boundary, records, now, now.AddSeconds(15));
        InputUtf8Bytes = Measure(history, content.Snapshot.Records);
        var units = ImmutableArray.CreateBuilder<MixedContextContribution>();
        foreach (var item in history)
        {
            var label = $"Whole earlier {item.Role} message {item.Message.Sequence}";
            units.Add(history.Length > 1
                ? new(label + $" - omitting saves {InputUtf8Bytes - Measure(history.Where(row => row != item), content.Snapshot.Records)} serialized UTF-8 bytes",
                    item, null)
                : new(label + " - required: keep at least one history message", null, null));
        }
        units.Add(new("Whole instruction " + instruction.Name + " - required by this consumer", null, null));
        foreach (var record in content.Snapshot.Records)
        {
            var label = "Whole SESSION memory " + (units.Count - history.Length).ToString(CultureInfo.InvariantCulture);
            units.Add(content.Snapshot.Records.Length > 1
                ? new(label + $" - omitting saves {InputUtf8Bytes - Measure(history, content.Snapshot.Records.Where(row => row.Id != record.Id))} serialized UTF-8 bytes",
                    null, record.Id.Value)
                : new(label + " - required: keep at least one SESSION memory", null, null));
        }
        contributions = units.ToImmutable();
        var preview = new StringBuilder("ONE local-only tool-free answer. Sources remain lower-trust content; no automatic recall, "
            + "source promotion, remembering, grants or hosted transfer. Only the new request and genuine answer/HOST outcome "
            + "are retained, not copies of selected source bodies.\n")
            .Append("Complete serialized input: ").Append(InputUtf8Bytes.ToString(CultureInfo.InvariantCulture))
            .Append(" / 32768 UTF-8 bytes; ").Append(contributions.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" / 16 evidence items.\n\n");
        if (InputUtf8Bytes > ModelContextEnvelope.MaximumInputUtf8Bytes)
        {
            preview.Append("OVER BUDGET: confirmation is unavailable. Nothing was submitted or omitted. "
                + "Explicitly omit optional WHOLE contributions below and inspect a fresh complete review. "
                + "The unchanged question, one history message, one instruction and one SESSION memory are required. "
                + "If these cannot fit, this request cannot complete without fresh source input; cancel rather than silently omitting required input. "
                + "Source permission deadlines are not renewed by recovery.\n\n");
        }
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
    internal int InputUtf8Bytes { get; }
    internal ImmutableArray<MixedContextContribution> Contributions
    { get { lock (gate) { return Current ? contributions : []; } } }
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
    internal bool CanConfirm
    { get { lock (gate) { return validated && !consumed && InputUtf8Bytes <= ModelContextEnvelope.MaximumInputUtf8Bytes && Current; } } }
    internal string Text { get { lock (gate) { return Current ? text : string.Empty; } } }
    internal string Body => Memory.ReadCurrent().Body;

    internal void MarkValidated() { lock (gate) { validated = true; } }

    internal MixedContextReview RemoveWholeContribution(MixedContextContribution contribution, TimeProvider time, ActivityLink cause)
    {
        lock (gate)
        {
            if (!Current || consumed || !contributions.Contains(contribution) || !contribution.CanOmit)
            { throw new InvalidOperationException("Only a current optional whole contribution may be omitted; required input needs a fresh source."); }
            var source = Instruction;
            var remaining = contribution.History is { } item ? history.Remove(item) : history;
            if (contribution.MemoryId is { } id) { Memory.RemoveWholeMemory(id); }
            // Transfer only still-live read permission, never consent or a new source deadline.
            Memory.Retired -= Dispose;
            disposed = true;
            text = string.Empty;
            history = [];
            contributions = [];
            instruction = null;
            var notify = Retired;
            Retired = null;
            try
            {
                var replacement = new MixedContextReview(owner, source, Memory, remaining, time, cause);
                notify?.Invoke();
                return replacement;
            }
            catch
            {
                source.Retire();
                Memory.Dispose();
                notify?.Invoke();
                throw;
            }
        }
    }

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
            contributions = [];
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
