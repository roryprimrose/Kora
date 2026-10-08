namespace ContainmentProof;

internal static class InvocationPolicy
{
    internal const string OwnedTrialConsent = "consent-owned-scratch-local-network-synthetic-credential";

    internal static void RequireOwnedTrialConsent(string consent)
    {
        if (consent != OwnedTrialConsent)
            throw new ArgumentException("Explicit owned-scratch/local-network/synthetic-credential consent required; no trial started.");
    }
}
