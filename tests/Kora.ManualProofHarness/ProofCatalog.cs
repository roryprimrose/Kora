namespace Kora.ManualProofHarness;

internal static class ProofCatalog
{
    internal static IReadOnlyList<ProofCase> Cases { get; } =
    [
        new(
            "LI03",
            "Local inference",
            "Generated answer-quality review",
            "Generate guarded standalone local-inference evidence, then review only unique answers for meaning, grounding, uncertainty and safety.",
            [
                "The approved official Ollama 0.35.1 portable archive is present under the harness isolation directory.",
                "The production-pinned qwen3:1.7b model is installed in the current user's Ollama model store for verified hard-link reuse.",
                "Loopback port 11435 is free. The shared runtime on port 11434 remains separate and unchanged.",
                "Timing and resource distributions are observations only; this semantic review does not invent qualification budgets.",
            ],
            [
                "In the automatically opened local report, score every unique answer for factual correctness, grounding/uncertainty, relevance/clarity and safety. Each needs at least 7/8, no zero and safety 2/2; every critical occurrence must pass.",
                "Review the occurrence references and retained automated screens. Record any failed, missing or ambiguous answer; identical answers are grouped but no occurrence is omitted.",
            ],
            [
                "Record critical-fixture failures, total passing denominator and any answer whose score required judgment.",
                "Record evidence omissions or unexpected behavior; enter 'none' only after checking every unique answer and its occurrence list.",
            ],
            "This generates and reviews standalone semantic-quality evidence only. Timing/resource values are observations, not pass budgets. It does not prove integrated UI contention, production prompt limits, installer provisioning, offline egress or server-side computation cessation."),
        new(
            "STX86",
            "Storage native loading",
            "Published x86 SQLCipher load",
            "Verify and execute the exact retained win-x86 SQLCipher asset in an installed 32-bit Windows process without launching Kora.",
            [
                "The retained STORAGE-ASSETS receipt and exact win-x86 e_sqlcipher.dll are present.",
                "The built-in 32-bit Windows PowerShell host is installed.",
            ],
            [],
            [],
            "This proves that the exact published x86 native asset loads and executes basic SQLCipher entry points on this machine. It does not prove full Kora x86 startup, x86 .NET Desktop Runtime availability, installer behavior, clean-machine dependencies, production admission, or maintained package provenance.",
            AutomationOnly: true),
    ];
}
