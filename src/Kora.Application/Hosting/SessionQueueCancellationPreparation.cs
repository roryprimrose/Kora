using System.Collections.Immutable;

using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed record SessionQueueCancellationPreparation(
    SessionWorkSnapshot Work, ImmutableArray<SessionQueueSelection> Selection, long ControlRevision);
