namespace Kora.Application.Tools;

/// <summary>Host-owned explicit UI/activated-voice review; never a model decision callback.</summary>
public delegate Task<SessionDiscoveryDecision> SessionDiscoveryPresenter(
    SessionDiscoveryReview review, CancellationToken token);
