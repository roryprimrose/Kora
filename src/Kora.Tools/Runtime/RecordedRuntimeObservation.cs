using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Kora.Tools.Readiness;

namespace Kora.Tools.Runtime;

public sealed class RecordedRuntimeObservation(DependencyBootstrapper dependencies)
{
    internal RuntimeObservation Read()
    {
        var observation = RecordedDependencyObservation.Read("local.inference", dependencies.Observations);
        return new(observation.Id, RuntimeLocality.Local, observation.Availability,
            observation.Readiness, observation.Reason, observation.ObservedAt, ToolLoopQualified: false);
    }
}
