using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Tools.Readiness;

public sealed class ReadinessGet(DependencyBootstrapper dependencies)
{
    private static readonly string[] DependencyIds =
        ["kora.storage", "kora.sqlite", "powershell.runtime", "local.inference", "windows.voice", "windows.tts"];

    internal CapabilityReply Execute(CapabilityPageInput input)
    {
        var total = DependencyIds.Length;
        if (CapabilityPagePolicy.Resolve(input, total) is not { } page) { return new(CapabilityOutcome.Denied, "page-out-of-range"); }
        var observations = dependencies.Observations;
        return new(CapabilityOutcome.Succeeded, "recorded-observations",
            Readiness: new(DependencyIds.Skip(input.Offset).Take(page.Count)
                .Select(id => RecordedDependencyObservation.Read(id, observations)).ToArray(), total, page.NextOffset));
    }
}
