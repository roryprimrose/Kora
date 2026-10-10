using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Dependencies;

/// <summary>Exact immutable preview. Its question confirms content, not runtime or egress authority.</summary>
public sealed class ModelHandoffOffer
{
    internal ModelHandoffOffer(ModelTurnHost host, ModelProviderPolicy policy,
        ModelContextEnvelope context, ModelHandoffReason reason, HostQuestionRecord question,
        HostRevision generation, HostRevision taskRevision, long controlRevision)
    {
        Host = host;
        Policy = policy;
        Context = context;
        Reason = reason;
        Question = question;
        Generation = generation;
        TaskRevision = taskRevision;
        ControlRevision = controlRevision;
    }

    public ModelProviderPolicy Policy { get; }
    public ModelContextEnvelope Context { get; }
    public ModelProviderSelection Destination => Policy.Hosted;
    public ModelHandoffReason Reason { get; }
    public HostQuestionRecord Question { get; }
    internal ModelTurnHost Host { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public HostRevision Revision { get; } = new(1);
    public HostRevision Generation { get; }
    public HostRevision TaskRevision { get; }
    public long ControlRevision { get; }
    internal int Used;
    internal int Retired;
}
