namespace Kora.ManualProofHarness;

internal sealed record ProofCase(
    string Id,
    string Area,
    string Title,
    string Purpose,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> FeedbackPrompts,
    string Limitation,
    bool AutomationOnly = false);
