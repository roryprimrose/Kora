using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

/// <summary>A single-use host capability. Its provider, envelope and authority cannot be replaced by a caller.</summary>
public sealed class ModelTurn
{
    private int used;
    private bool running;
    internal bool IsRunning { get => Volatile.Read(ref running); set => Volatile.Write(ref running, value); }
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
    internal Guid Owner { get; }
    internal HostActivity Issuer { get; }
    internal ModelContextEnvelope Context { get; }
    internal byte[] Wire { get; }
    internal HostRevision SessionGeneration { get; }
    internal HostRevision TaskRevision { get; }
    internal long ControlRevision { get; }
    internal ModelProviderRegistration Registration { get; }
    internal ModelProviderPolicy? Policy { get; set; }
    internal ModelTurnResult? TerminalResult { get; set; }
    internal bool TryUse() => Interlocked.Exchange(ref used, 1) == 0;
}
