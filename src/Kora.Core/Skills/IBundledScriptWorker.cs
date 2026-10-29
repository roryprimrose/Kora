using Kora.Core.Hosting;

namespace Kora.Core.Skills;

/// <summary>Executes immutable script snapshots, never their requested native effect.</summary>
public interface IBundledScriptWorker
{
    /// <summary>Identifies the exact independently qualified worker/runtime implementation, or no admission.</summary>
    string? QualifiedImplementationDigest { get; }
    bool IsQuiescent { get; }

    /// <summary>Runs and physically joins one worker, accepting one correlated fixed-effect request.</summary>
    Task<BundledScriptReceipt> RunAsync(BundledFixedScript script, HostRequest invocation,
        Func<bool> current, CancellationToken cancellationToken);
}