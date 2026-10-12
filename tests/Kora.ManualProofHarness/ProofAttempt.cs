namespace Kora.ManualProofHarness;

internal sealed class ProofAttempt
{
    public required Guid AttemptId { get; set; }
    public required string ProofId { get; set; }
    public required DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public AttemptOutcome Outcome { get; set; }
    public int NextStepIndex { get; set; }
    public Dictionary<string, string> AutomatedEvidence { get; set; } = new(StringComparer.Ordinal);
    public List<string> StepNotes { get; set; } = [];
    public Dictionary<string, string> Feedback { get; set; } = new(StringComparer.Ordinal);
    public string? Summary { get; set; }
}
