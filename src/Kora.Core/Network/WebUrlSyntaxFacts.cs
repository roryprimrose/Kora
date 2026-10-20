namespace Kora.Core.Network;

/// <summary>Parser facts only. No path, credentials, query/fragment values or source reference.</summary>
public sealed record WebUrlSyntaxFacts(
    string Scheme, string CanonicalAsciiHost, int EffectivePort, bool QueryPresent, bool FragmentPresent);
