using System.Text.Json;

namespace ContainmentProof;

internal static class ProofTests
{
    internal static int SelfTest()
    {
        var receipt = new Receipt("expected", 1, new(false, "synthetic", null), [], []);
        Require(Wire.EffectOutcome("expected", null) == "Unknown", "lost receipt stays Unknown");
        Require(Wire.EffectOutcome("other", receipt) == "Unknown", "uncorrelated receipt stays Unknown");
        Require(Wire.EffectOutcome("expected", receipt) == "Observed", "correlated observations retained");
        Require(Worker.Access("denial", () => throw new UnauthorizedAccessException()).Outcome == "Denied",
            "only actual access-denied exceptions classify as Denied");
        Require(Worker.Access("missing", () => throw new IOException()).Outcome == "Unknown",
            "I/O failure is not denial evidence");
        Console.WriteLine("PASS: 5 deterministic receipt/classification assertions (not OS containment evidence)");
        return 0;
    }

    internal static int Verify(List<Trial> trials, string report)
    {
        var errors = new List<string>();
        int assertions = 0;
        void Check(bool condition, string name)
        {
            assertions++;
            if (!condition) errors.Add(name);
        }
        var baseline = trials.Single(t => t.Profile == "job-only-complete");
        Check(baseline.Receipt is { Token.AppContainer: false }, "baseline token and receipt");
        Check(baseline.State == "Exited" && baseline.ExitCode == 0 && baseline.TreeStopped,
            "completed baseline and stopped descendants");
        foreach (var name in new[] { "filesystem.protected.write", "filesystem.protected.delete",
            "filesystem.protected.rename", "filesystem.hardlink.write", "filesystem.traversal.write",
            "network.loopback", "network.owned-interface", "credential.synthetic", "child.normal" })
            Check(baseline.Receipt?.Probes.Single(p => p.Name == name).Outcome == "Allowed",
                $"job-only control demonstrates ambient access: {name}");
        Check(!baseline.ProtectedBytesUnchanged, "job-only protected mutation is detected");
        foreach (var trial in trials.Where(t => t.Profile.StartsWith("appcontainer", StringComparison.Ordinal)))
        {
            Check(trial.LaunchError is null, $"{trial.Profile}: native launch");
            Check(trial.ProtectedBytesUnchanged, $"{trial.Profile}: protected bytes unchanged");
            Check(trial.TreeStopped && trial.JobPids.Count >= 2, $"{trial.Profile}: tracked tree stopped");
            Check(trial.ShutdownMilliseconds < 5000, $"{trial.Profile}: bounded shutdown");
            Check(trial.ChildTokens.Count >= 2 && trial.ChildTokens.All(t => t.AppContainer),
                $"{trial.Profile}: child and grandchild inherit AppContainer");
            if (trial.Profile.EndsWith("malformed-receipt", StringComparison.Ordinal))
            {
                Check(trial.State == "Aborted" && trial.EffectOutcome == "Unknown" && trial.EffectMarkerExists
                    && trial.Failure?.StartsWith("Unverifiable receipt", StringComparison.Ordinal) == true,
                    "malformed receipt after an effect is an explicit failure with Unknown outcome");
                continue;
            }
            Check(trial.Failure is null, $"{trial.Profile}: no lifecycle/identity failure");
            if (trial.Profile.EndsWith("lost-receipt", StringComparison.Ordinal))
            {
                Check(trial.State == "TimedOut" && trial.EffectOutcome == "Unknown" && trial.EffectMarkerExists,
                    "lost receipt after an effect is Unknown, not cancelled/successful/retryable");
                continue;
            }
            Check(trial.Receipt is { Token.AppContainer: true }, $"{trial.Profile}: actual AppContainer token");
            Check(trial.Profile.EndsWith("-complete", StringComparison.Ordinal)
                ? trial.State == "Exited" && trial.ExitCode == 0
                : trial.State == "Cancelled", $"{trial.Profile}: truthful lifecycle state");
            foreach (var name in new[] { "filesystem.protected.read", "filesystem.protected.write",
                "filesystem.protected.delete", "filesystem.protected.rename", "filesystem.hardlink.write",
                "filesystem.traversal.write", "network.loopback", "network.owned-interface", "child.breakaway" })
                Check(trial.Receipt?.Probes.Single(p => p.Name == name).Outcome == "Denied",
                    $"{trial.Profile}: OS denies {name}");
            Check(trial.Receipt?.Probes.Single(p => p.Name == "filesystem.allowed.write").Outcome == "Allowed",
                $"{trial.Profile}: allowed write");
            Check(trial.Receipt?.Probes.Single(p => p.Name == "credential.synthetic").Outcome is "Denied" or "Isolated",
                $"{trial.Profile}: synthetic credential unavailable");
            var ps = trial.Receipt?.Probes.Single(p => p.Name == "powershell.fixed");
            Check(ps?.Outcome == "Observed", $"{trial.Profile}: fixed PowerShell receipt");
            if (ps?.Outcome == "Observed")
            {
                using var json = JsonDocument.Parse(ps.Detail!);
                Check(json.RootElement.GetProperty("protectedWrite").GetString() == "Denied",
                    $"{trial.Profile}: PowerShell protected write denied");
                Check(json.RootElement.GetProperty("network").GetString() == "Denied",
                    $"{trial.Profile}: PowerShell loopback denied");
                Check(json.RootElement.GetProperty("networkLan").GetString() == "Denied",
                    $"{trial.Profile}: PowerShell owned-interface denied");
            }
        }
        foreach (string error in errors) Console.Error.WriteLine($"FAIL/UNSUPPORTED: {error}");
        Wire.Write(report, new
        {
            Assertions = assertions, Passed = assertions - errors.Count,
            UnmetAssertions = errors, FullProductionProfileCertified = false,
        });
        Console.WriteLine($"OS proof verification: {errors.Count} unmet assertions; full production profile NOT certified.");
        return errors.Count == 0 ? 0 : 2;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    }
}
