using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Volatile original-user review of a declaration, not enablement or model consent.</summary>
public sealed class SharedSkillReferenceConsent : IDisposable
{
    private readonly Lock gate = new();
    private readonly SharedSkillInvocationWorkflow owner;
    private readonly Func<bool> current;
    private readonly TimeProvider time;
    private readonly DateTimeOffset created;
    private bool retired;
    private bool consumed;

    internal SharedSkillReferenceConsent(SharedSkillInvocationWorkflow owner, SharedSkillSource source,
        SharedSkillSnapshot snapshot, Func<bool> current, TimeProvider time, RequestOrigin origin)
    {
        this.owner = owner;
        Source = source;
        Snapshot = snapshot;
        this.current = current;
        this.time = time;
        Origin = origin;
        created = time.GetUtcNow();
        ExpiresAt = created.AddMinutes(2);
    }

    internal SharedSkillSource Source { get; }
    internal SharedSkillSnapshot Snapshot { get; }
    internal RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsCurrent { get { lock (gate) { return !consumed && IsReadCurrent; } } }
    internal bool IsReadCurrent
    {
        get
        {
            lock (gate)
            {
                if (retired) { return false; }
                if (!current() || time.GetUtcNow() < created || time.GetUtcNow() >= ExpiresAt) { retired = true; }
                return !retired;
            }
        }
    }

    public string DeclarationText => IsCurrent
        ? $"Read only the following declared local Markdown files for {Snapshot.Name} from source {Source.Id:D}.\n"
            + string.Join("\n", Snapshot.ReferenceDeclaration!.Paths)
            + "\nAt most four files, 16 KiB each and 64 KiB including the primary. No reference content has been read. "
            + "This separate consent grants local reads only, not enablement, invocation, tools or hosted disclosure."
        : string.Empty;

    internal void Consume(SharedSkillInvocationWorkflow expected)
    {
        lock (gate)
        {
            if (!ReferenceEquals(owner, expected) || !IsCurrent)
            { throw new InvalidOperationException("The original-user declaration review expired or was already consumed."); }
            consumed = true;
        }
    }

    public void Dispose() { lock (gate) { retired = true; } }
}
