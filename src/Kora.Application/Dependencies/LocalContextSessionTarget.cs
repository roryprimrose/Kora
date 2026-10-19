using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

/// <summary>An exact, one-shot session target included in the caller's explicit source-use review.</summary>
public sealed class LocalContextSessionTarget
{
    private int consumed;

    private LocalContextSessionTarget(HostRequest request, HostRevision? generation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || generation is { Value: <= 0 })
        {
            throw new InvalidDataException("Local context requires original user input and a known session generation.");
        }
        Request = request;
        ExpectedGeneration = generation;
    }

    public const string FreshSessionName = "Local context";
    public const string FreshSessionDisclosure =
        "Create the reviewed Local context session for this one local-only, tool-free request. "
        + "Only host session/task receipts are retained; source text and the answer are not saved.";

    public HostRequest Request { get; }
    public HostRevision? ExpectedGeneration { get; }
    public bool CreatesSession => ExpectedGeneration is null;

    /// <summary>Requires the exact new session identity and creation disclosure to have been reviewed with the source.</summary>
    public static LocalContextSessionTarget ReviewedFresh(HostRequest request) => new(request, null);

    /// <summary>Uses only the reviewed active session generation; never creates or reroutes on failure.</summary>
    public static LocalContextSessionTarget Existing(HostRequest request, HostRevision generation) => new(request, generation);

    internal bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0;
}