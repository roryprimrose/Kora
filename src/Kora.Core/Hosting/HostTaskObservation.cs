using Kora.Core.Interaction;

namespace Kora.Core.Hosting;

public sealed record HostTaskObservation(
    HostTaskRecord Task, HostRevision Generation, string Source,
    bool CurrentSource, HostQuestionRecord? Question);
