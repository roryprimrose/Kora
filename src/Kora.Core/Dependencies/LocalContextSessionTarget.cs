using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

/// <summary>An exact, one-shot session target included in the caller's explicit source-use review.</summary>
public sealed class LocalContextSessionTarget
{
    private int consumed;

    private LocalContextSessionTarget(HostRequest sourceRequest, HostRequest request, HostRevision? generation)
    {
        ArgumentNullException.ThrowIfNull(sourceRequest);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || sourceRequest.Origin != request.Origin || generation is { Value: <= 0 }
            || !ReferenceEquals(sourceRequest, request)
                && (sourceRequest.RequestId == request.RequestId || sourceRequest.TaskId == request.TaskId))
        {
            throw new InvalidDataException("Local context requires original user input and a known session generation.");
        }
        Request = request;
        SourceRequest = sourceRequest;
        ExpectedGeneration = generation;
    }

    public const string FreshSessionName = "Local context";
    public const string FreshSessionDisclosure =
        "Create the reviewed Local context session for this one local-only, tool-free request. "
        + "Only host session/task receipts are retained; source text and the answer are not saved.";

    public HostRequest Request { get; }
    /// <summary>Original input/source lineage only; its volatile session/task identifiers never establish durable ownership.</summary>
    public HostRequest SourceRequest { get; }
    public HostRevision? ExpectedGeneration { get; }
    public bool CreatesSession => ExpectedGeneration is null;

    /// <summary>Requires the exact new session identity and creation disclosure to have been reviewed with the source.</summary>
    public static LocalContextSessionTarget ReviewedFresh(HostRequest request) => new(request, request, null);

    /// <summary>Reviews a distinct exact durable target, preserving the original source request as provenance only.</summary>
    public static LocalContextSessionTarget ReviewedFresh(HostRequest sourceRequest, HostRequest reviewedRequest) =>
        new(sourceRequest, reviewedRequest, null);

    /// <summary>Uses only the reviewed active session generation; never creates or reroutes on failure.</summary>
    public static LocalContextSessionTarget Existing(HostRequest request, HostRevision generation) => new(request, request, generation);

    /// <summary>Reviews a new task in the exact existing target generation without adopting volatile source identity.</summary>
    public static LocalContextSessionTarget Existing(HostRequest sourceRequest, HostRequest reviewedRequest, HostRevision generation) =>
        new(sourceRequest, reviewedRequest, generation);

    /// <summary>Consumes the reviewed decision once; this does not establish host or durable task authority.</summary>
    public bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0;
}