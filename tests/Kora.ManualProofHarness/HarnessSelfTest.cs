namespace Kora.ManualProofHarness;

internal static class HarnessSelfTest
{
    internal static int Run()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kora-proof-harness-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = System.IO.Path.Combine(root, "results.json");
            var store = new ResultStore(path);
            var results = store.Load();
            Assert(results.Attempts.Count == 0, "A missing result file must load as an empty document.");
            Assert(ProofCatalog.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == ProofCatalog.Cases.Count,
                "Proof IDs must be unique.");
            Assert(ProofCatalog.Cases.Where(item => !item.AutomationOnly)
                    .All(item => item.Steps.Count > 0 && item.FeedbackPrompts.Count > 0),
                "Every operator-driven proof must have steps and feedback prompts.");
            Assert(ProofCatalog.Cases.Where(item => item.AutomationOnly)
                    .All(item => item.Steps.Count == 0 && item.FeedbackPrompts.Count == 0),
                "Automation-only proofs must not request operator steps or feedback.");

            var now = DateTimeOffset.UtcNow;
            results.Attempts.Add(new()
            {
                AttemptId = Guid.NewGuid(),
                ProofId = "HISTORICAL",
                StartedAt = now,
                UpdatedAt = now,
                Outcome = AttemptOutcome.InProgress,
                NextStepIndex = 2,
                AutomatedEvidence = { ["source.revision"] = "abc123" },
                StepNotes = ["Step 1: verified"],
            });
            store.Save(results);
            var reloaded = store.Load();
            Assert(reloaded.Attempts.Count == 1, "An attempt must survive reload.");
            Assert(reloaded.Attempts[0].NextStepIndex == 2, "Step progress must survive reload.");
            Assert(string.Equals(reloaded.Attempts[0].AutomatedEvidence["source.revision"], "abc123", StringComparison.Ordinal),
                "Automated evidence must survive reload.");
            Assert(string.Equals(reloaded.Attempts[0].StepNotes.Single(), "Step 1: verified", StringComparison.Ordinal),
                "Notes must survive reload.");
            var fixturesPath = System.IO.Path.Combine(root, "fixtures.json");
            var assessmentPath = System.IO.Path.Combine(root, "assessment.json");
            var reviewPath = System.IO.Path.Combine(root, "review.html");
            File.WriteAllText(fixturesPath, """
                [{
                  "id": "fixture-1",
                  "question": "What is the value?",
                  "expectedAnswer": "The value is 4.",
                  "critical": true
                }]
                """);
            File.WriteAllText(assessmentPath, """
                {
                  "inputs": [{
                    "answers": [
                      {
                        "fixtureId": "fixture-1",
                        "state": "Completed",
                        "answer": "The value is 4.",
                        "answerReference": "streamTrials[0]",
                        "factual": { "Status": "Supported subchecks; human review required" }
                      },
                      {
                        "fixtureId": "fixture-1",
                        "state": "Completed",
                        "answer": "The value is 4.",
                        "answerReference": "streamTrials[1]",
                        "factual": { "Status": "Supported subchecks; human review required" }
                      }
                    ]
                  }]
                }
                """);
            Assert(Li03ReviewReport.Create(assessmentPath, fixturesPath, reviewPath) == 1,
                "Identical LI03 answers must be grouped into one review item.");
            var review = File.ReadAllText(reviewPath);
            Assert(review.Contains("streamTrials[0], streamTrials[1]", StringComparison.Ordinal),
                "The LI03 report must retain every grouped occurrence reference.");
            var measurementPath = System.IO.Path.Combine(root, "measurement.json");
            var dispositionPath = System.IO.Path.Combine(root, "disposition.json");
            File.WriteAllText(measurementPath, """
                {
                  "testedProfile": { "runtimeVersion": "0.40.2", "comparisonRuntime": true },
                  "automatedQualityAndClientChecksPassed": false,
                  "streamTrials": [{
                    "fixtureId": "fixture-1",
                    "generation": { "State": "Completed" },
                    "quality": { "Passed": false },
                    "cpuOnlyVerified": true
                  }],
                  "productionPairedTrials": [],
                  "cleanup": "Selected test model confirmed unloaded."
                }
                """);
            Li03AutomatedDisposition.Create(measurementPath, dispositionPath);
            using var disposition = System.Text.Json.JsonDocument.Parse(File.ReadAllText(dispositionPath));
            Assert(string.Equals(
                    disposition.RootElement.GetProperty("disposition").GetString(),
                    "RejectedByAutomatedGates",
                    StringComparison.Ordinal),
                "An automated LI03 failure must skip human qualification review.");
            Console.WriteLine("Manual proof harness self-test passed.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
