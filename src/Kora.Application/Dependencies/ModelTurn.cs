using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>A single-use host capability. Its provider, envelope and authority cannot be replaced by a caller.</summary>
public sealed class ModelTurn
{
    private readonly Lock observationGate = new();
    private readonly List<Kora.Core.Conversation.RetainedLocalObservation> observations = [];
    internal bool RetainLocalObservations { get; set; }
    internal bool RetainsLocalObservations => RetainLocalObservations && ConversationTarget is not null
        && Context.ToolMode == ModelToolMode.ReadOnly && Provenance.Selection.Provider == ModelProviderIdentity.Ollama
        && Provenance.Request.InvocationId is null;

    internal void CaptureObservation(string id, string input, Kora.Core.Tools.CapabilityReply reply, DateTimeOffset observedAt)
    {
        lock (observationGate)
        {
            if (!IsRunning || observations.Count >= ModelToolProtocol.MaximumCalls)
            { throw new InvalidDataException("The owning observation turn is closed or its bound was exceeded."); }
            observations.Add(Kora.Core.Conversation.RetainedLocalObservation.Capture(id, input, reply, observedAt));
        }
    }

    internal Kora.Core.Conversation.RetainedLocalObservations? SnapshotObservations()
    {
        lock (observationGate)
        {
            return observations.Count == 0 ? null : new(1, [.. observations]);
        }
    }

    private int used;
    private bool running;
    private int retirementReason;
    private int forbiddenToolAttempt;
    private int toolCalls;
    internal bool TryAdmitTool() => Interlocked.Increment(ref toolCalls) <=
        (Context.WebPages?.MaximumCalls ?? Context.Constraints?.MaximumCalls ?? ModelToolProtocol.MaximumCalls);
    internal bool HasForbiddenToolAttempt => Volatile.Read(ref forbiddenToolAttempt) != 0;
    internal void RejectToolAttempt() => Interlocked.Exchange(ref forbiddenToolAttempt, 1);
    internal ModelTurnReason RetirementReason => (ModelTurnReason)Volatile.Read(ref retirementReason);
    internal Action? AuthorityRetired { get; set; }
    internal void Retire(ModelTurnReason reason)
    {
        if (Interlocked.CompareExchange(ref retirementReason, (int)reason, (int)ModelTurnReason.None)
            == (int)ModelTurnReason.None)
        { AuthorityRetired?.Invoke(); }
    }
    internal bool IsRunning
    {
        get => Volatile.Read(ref running) && RetirementReason == ModelTurnReason.None;
        set => Volatile.Write(ref running, value);
    }
    internal ModelTurn(Guid owner, HostActivity issuer, ModelTurnProvenance provenance, ModelContextEnvelope context,
        byte[] wire, HostRevision sessionGeneration, HostRevision taskRevision,
        long controlRevision, ModelProviderRegistration registration)
    {
        Owner = owner;
        Issuer = issuer;
        Provenance = provenance;
        Context = context;
        Wire = wire;
        SessionGeneration = sessionGeneration;
        TaskRevision = taskRevision;
        ControlRevision = controlRevision;
        Registration = registration;
    }

    public ModelTurnProvenance Provenance { get; }
    public ProviderProfileTuple? Profile { get; internal set; }
    public long? SessionPolicyRevision { get; internal set; }
    internal Guid Owner { get; }
    internal HostActivity Issuer { get; }
    internal ModelContextEnvelope Context { get; }
    internal byte[] Wire { get; }
    internal HostRevision SessionGeneration { get; }
    internal HostRevision TaskRevision { get; set; }
    internal Kora.Core.Storage.SessionWorkspaceEntry? ConversationTarget { get; set; }
    internal long ControlRevision { get; }
    internal ModelProviderRegistration Registration { get; set; }
    internal ModelProviderPolicy? Policy { get; set; }
    internal ModelTurnResult? TerminalResult { get; set; }
    internal LocalUnavailableObservation? LocalUnavailable { get; set; }
    internal RuntimeResourceAllocator.Lease? ResourceLease { get; set; }
    internal bool ResourceDispatched { get; set; }
    internal Kora.Application.Hosting.ConversationQuestionContinuation? QuestionContinuation { get; set; }
    internal Func<ModelAnswerUpdate, CancellationToken, ValueTask>? PublishAnswer { get; set; }
    internal Func<CancellationToken, ValueTask<ModelTurnReason>>? ValidateAnswerCommit { get; set; }
    internal Action<Kora.Core.Conversation.ConversationExecutionStep>? PublishExecutionStep { get; set; }
    internal bool TryUse() => Interlocked.Exchange(ref used, 1) == 0;
}