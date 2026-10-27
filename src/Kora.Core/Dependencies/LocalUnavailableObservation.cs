using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

/// <summary>Volatile evidence issued by the owning local runtime after its exact physical resource join.</summary>
public sealed class LocalUnavailableObservation
{
    private readonly Func<bool> current;
    private readonly Lock gate = new();
    private bool revoked;
    private int successorUsed;

    internal LocalUnavailableObservation(ModelTurnResult result, ModelContextEnvelope context,
        HostRevision generation, HostRevision taskRevision, long controlRevision, Func<bool> current)
    {
        Result = result;
        Context = context;
        Generation = generation;
        TaskRevision = taskRevision;
        ControlRevision = controlRevision;
        this.current = current;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public ModelTurnResult Result { get; }
    public ModelContextEnvelope Context { get; }
    public HostRevision Generation { get; }
    public HostRevision TaskRevision { get; }
    public long ControlRevision { get; }
    public bool IsCurrent
    {
        get
        {
            lock (gate)
            {
                if (revoked) { return false; }
                if (current()) { return true; }
                revoked = true;
                return false;
            }
        }
    }
    internal bool TryUseSuccessor() => Interlocked.Exchange(ref successorUsed, 1) == 0;
    internal void Revoke()
    {
        lock (gate) { revoked = true; }
    }

    public bool Admits(ModelContextEnvelope context) =>
        context.Request == Context.Request && string.Equals(context.SystemPolicy, Context.SystemPolicy, StringComparison.Ordinal)
        && string.Equals(context.UserRequest, Context.UserRequest, StringComparison.Ordinal) && context.CreatedAt == Context.CreatedAt
        && context.ExpiresAt == Context.ExpiresAt && context.ToolMode == Context.ToolMode
        && context.Constraints == Context.Constraints && context.McpTool == Context.McpTool
        && context.WebPages == Context.WebPages
        && context.Evidence.SequenceEqual(Context.Evidence.Where(item => context.Evidence.Contains(item)));
}
