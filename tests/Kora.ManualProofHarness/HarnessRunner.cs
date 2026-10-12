namespace Kora.ManualProofHarness;

internal sealed class HarnessRunner(
    IReadOnlyList<ProofCase> cases,
    ResultStore store,
    ProofAutomation automation,
    TextReader input,
    TextWriter output)
{
    internal async Task RunAsync(string? resumeProofId, string? startProofId, string? automateProofId)
    {
        var results = store.Load();
        output.WriteLine("Kora standalone manual proof harness");
        output.WriteLine("This is not the Kora application. Safe case setup is automated after START; consent-bound actions remain manual.");
        output.WriteLine($"Results are saved after every response to: {store.Path}");

        if (!string.IsNullOrWhiteSpace(automateProofId))
        {
            var proof = cases.SingleOrDefault(item =>
                string.Equals(item.Id, automateProofId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown proof ID '{automateProofId}'.");
            foreach (var invalid in results.Attempts.Where(item =>
                item.Outcome == AttemptOutcome.Failed
                && string.Equals(item.ProofId, proof.Id, StringComparison.Ordinal)
                && item.AutomatedEvidence.TryGetValue("li03.measurementPath", out var measurement)
                && File.Exists(measurement)
                && item.AutomatedEvidence.ContainsKey("li03.automatedDispositionPath")
                && !Li03AutomatedDisposition.HasConclusiveAutomatedResult(measurement)))
            {
                invalid.AutomatedEvidence.Remove("li03.automatedDispositionPath");
                invalid.Summary =
                    "Automation infrastructure failed before candidate gates completed; retained evidence is not a candidate rejection.";
                Complete(results, invalid, AttemptOutcome.Blocked);
            }

            var interrupted = results.Attempts.LastOrDefault(item =>
                item.Outcome == AttemptOutcome.InProgress
                && string.Equals(item.ProofId, proof.Id, StringComparison.Ordinal)
                && item.AutomatedEvidence.TryGetValue("li03.isolatedTeardownVerified", out var teardown)
                && string.Equals(teardown, bool.TrueString, StringComparison.Ordinal)
                && !item.AutomatedEvidence.ContainsKey("li03.automatedDispositionPath")
                && !item.AutomatedEvidence.ContainsKey("li03.reviewPath"));
            if (interrupted is not null)
            {
                interrupted.Summary =
                    "Automation was interrupted before a disposition or review was produced; isolated teardown was verified.";
                Complete(results, interrupted, AttemptOutcome.Blocked);
            }

            var now = DateTimeOffset.UtcNow;
            var attempt = new ProofAttempt
            {
                AttemptId = Guid.NewGuid(),
                ProofId = proof.Id,
                StartedAt = now,
                UpdatedAt = now,
                Outcome = AttemptOutcome.InProgress,
            };
            results.Attempts.Add(attempt);
            store.Save(results);
            ShowCase(proof);
            var preparationError = await automation.PrepareAsync(
                proof, attempt, () => Save(results, attempt), output);
            if (preparationError is not null)
            {
                attempt.Summary = preparationError;
                Complete(
                    results,
                    attempt,
                    attempt.AutomatedEvidence.ContainsKey("li03.automatedDispositionPath")
                        ? AttemptOutcome.Failed
                        : AttemptOutcome.Blocked);
            }
            else
            {
                if (proof.AutomationOnly)
                {
                    attempt.Summary = "All automation-only proof checks passed.";
                    Complete(results, attempt, AttemptOutcome.Passed);
                }
                else
                {
                    attempt.Summary = "Automated gates passed; independent human semantic review remains.";
                    Save(results, attempt);
                }
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(resumeProofId))
        {
            var proof = cases.SingleOrDefault(item =>
                string.Equals(item.Id, resumeProofId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown proof ID '{resumeProofId}'.");
            var pending = results.Attempts.LastOrDefault(attempt =>
                attempt.Outcome == AttemptOutcome.InProgress
                && string.Equals(attempt.ProofId, proof.Id, StringComparison.Ordinal));
            if (pending is not null)
            {
                await RunAttemptAsync(proof, pending, results);
            }
            else
            {
                output.WriteLine($"No in-progress {proof.Id} attempt was found; showing the main menu.");
            }
        }

        if (!string.IsNullOrWhiteSpace(startProofId))
        {
            var proof = cases.SingleOrDefault(item =>
                string.Equals(item.Id, startProofId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown proof ID '{startProofId}'.");
            var now = DateTimeOffset.UtcNow;
            var attempt = new ProofAttempt
            {
                AttemptId = Guid.NewGuid(),
                ProofId = proof.Id,
                StartedAt = now,
                UpdatedAt = now,
                Outcome = AttemptOutcome.InProgress,
            };
            results.Attempts.Add(attempt);
            store.Save(results);
            await RunAttemptAsync(proof, attempt, results);
        }

        while (true)
        {
            ShowMenu(results);
            var selection = ReadLine("Select a case number, R to resume, or Q to quit: ");
            if (selection.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (selection.Equals("r", StringComparison.OrdinalIgnoreCase))
            {
                var pending = results.Attempts.LastOrDefault(attempt => attempt.Outcome == AttemptOutcome.InProgress);
                if (pending is null)
                {
                    output.WriteLine("There is no in-progress attempt.");
                    continue;
                }

                await RunAttemptAsync(cases.Single(item =>
                    string.Equals(item.Id, pending.ProofId, StringComparison.Ordinal)), pending, results);
                continue;
            }

            if (!int.TryParse(selection, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var caseNumber)
                || caseNumber < 1 || caseNumber > cases.Count)
            {
                output.WriteLine("Enter a listed case number, R, or Q.");
                continue;
            }

            var proof = cases[caseNumber - 1];
            ShowCase(proof);
            if (!ReadLine("Type START to create and persist an attempt, or press Enter to return: ")
                .Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var attempt = new ProofAttempt
            {
                AttemptId = Guid.NewGuid(),
                ProofId = proof.Id,
                StartedAt = now,
                UpdatedAt = now,
                Outcome = AttemptOutcome.InProgress,
            };
            results.Attempts.Add(attempt);
            store.Save(results);
            await RunAttemptAsync(proof, attempt, results);
        }
    }

    private void ShowMenu(ProofResults results)
    {
        output.WriteLine();
        for (var index = 0; index < cases.Count; index++)
        {
            var proof = cases[index];
            var latest = results.Attempts.LastOrDefault(attempt =>
                string.Equals(attempt.ProofId, proof.Id, StringComparison.Ordinal));
            output.WriteLine($"{index + 1}. [{proof.Id}] {proof.Title} - {latest?.Outcome.ToString() ?? "NotRun"}");
        }
    }

    private void ShowCase(ProofCase proof)
    {
        output.WriteLine();
        output.WriteLine($"[{proof.Id}] {proof.Area}: {proof.Title}");
        output.WriteLine(proof.Purpose);
        output.WriteLine($"Automation: {automation.Describe(proof.Id)}");
        output.WriteLine("Prerequisites:");
        foreach (var prerequisite in proof.Prerequisites)
        {
            output.WriteLine($"- {prerequisite}");
        }

        output.WriteLine($"Boundary: {proof.Limitation}");
    }

    private async Task RunAttemptAsync(ProofCase proof, ProofAttempt attempt, ProofResults results)
    {
        ShowCase(proof);
        var preparationError = await automation.PrepareAsync(proof, attempt, () => Save(results, attempt), output);
        if (preparationError is not null)
        {
            attempt.Summary = preparationError;
            Complete(results, attempt, AttemptOutcome.Blocked);
            return;
        }
        if (proof.AutomationOnly)
        {
            attempt.Summary = "All automation-only proof checks passed.";
            Complete(results, attempt, AttemptOutcome.Passed);
            return;
        }

        while (attempt.NextStepIndex < proof.Steps.Count)
        {
            var stepNumber = attempt.NextStepIndex + 1;
            output.WriteLine();
            output.WriteLine($"Step {stepNumber}/{proof.Steps.Count}: {proof.Steps[attempt.NextStepIndex]}");
            var response = ReadLine("Press Enter when complete; enter N for a note, B for Blocked, F for Failed, or P to pause: ");
            if (response.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                Save(results, attempt);
                return;
            }

            if (response.Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                attempt.StepNotes.Add($"Step {stepNumber}: {ReadLine("Note: ")}");
                Save(results, attempt);
                continue;
            }

            if (response.Equals("b", StringComparison.OrdinalIgnoreCase)
                || response.Equals("f", StringComparison.OrdinalIgnoreCase))
            {
                attempt.Summary = ReadLine("Reason: ");
                Complete(results, attempt, response.Equals("b", StringComparison.OrdinalIgnoreCase)
                    ? AttemptOutcome.Blocked
                    : AttemptOutcome.Failed);
                return;
            }

            if (response.Length != 0)
            {
                output.WriteLine("Use Enter, N, B, F, or P.");
                continue;
            }

            attempt.NextStepIndex++;
            Save(results, attempt);
        }

        foreach (var prompt in proof.FeedbackPrompts)
        {
            if (!attempt.Feedback.ContainsKey(prompt))
            {
                attempt.Feedback[prompt] = ReadLine($"{prompt} ");
                Save(results, attempt);
            }
        }

        while (true)
        {
            var outcome = ReadLine("Final outcome: P=Passed, F=Failed, B=Blocked, N=Not run: ");
            var parsed = outcome.ToUpperInvariant() switch
            {
                "P" => AttemptOutcome.Passed,
                "F" => AttemptOutcome.Failed,
                "B" => AttemptOutcome.Blocked,
                "N" => AttemptOutcome.NotRun,
                _ => AttemptOutcome.InProgress,
            };
            if (parsed == AttemptOutcome.InProgress)
            {
                output.WriteLine("Enter P, F, B, or N.");
                continue;
            }

            attempt.Summary = ReadLine("Outcome summary: ");
            Complete(results, attempt, parsed);
            return;
        }
    }

    private void Complete(ProofResults results, ProofAttempt attempt, AttemptOutcome outcome)
    {
        attempt.Outcome = outcome;
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        Save(results, attempt);
        output.WriteLine($"Saved {outcome} outcome for {attempt.ProofId}.");
    }

    private void Save(ProofResults results, ProofAttempt attempt)
    {
        attempt.UpdatedAt = DateTimeOffset.UtcNow;
        store.Save(results);
    }

    private string ReadLine(string prompt)
    {
        output.Write(prompt);
        output.Flush();
        return input.ReadLine() ?? "p";
    }
}
