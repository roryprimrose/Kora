namespace Kora.ManualProofHarness;

internal sealed class ProofResults
{
    public int FormatVersion { get; set; } = 1;
    public List<ProofAttempt> Attempts { get; set; } = [];
}
