using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kora.ManualProofHarness;

internal sealed class ProofAutomation
{
    internal string Describe(string proofId) => proofId switch
    {
        "LI03" => "Captures context, binds the exact loopback Ollama PID, runs deterministic checks and 30 cold/warm trials, verifies model cleanup, derives factual checks, deduplicates answers, and opens a local review sheet.",
        "STX86" => "Verifies the retained x86 SQLCipher receipt and binary, executes it in installed 32-bit Windows PowerShell, and writes a machine-readable native-load receipt.",
        _ => "Captures machine/source context.",
    };

    internal async Task<string?> PrepareAsync(ProofCase proof, ProofAttempt attempt, Action persist, TextWriter output)
    {
        try
        {
            var repositoryRoot = FindRepositoryRoot();
            await CaptureContextAsync(repositoryRoot, attempt, persist);
            return proof.Id switch
            {
                "LI03" => await PrepareLi03Async(repositoryRoot, attempt, persist, output),
                "STX86" => await StorageX86Automation.RunAsync(attempt, persist, output),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or InvalidDataException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException
            or System.Text.Json.JsonException or HttpRequestException or OperationCanceledException)
        {
            return $"Automatic preparation failed: {exception.Message}";
        }
    }

    private async Task<string?> PrepareLi03Async(
        string repositoryRoot,
        ProofAttempt attempt,
        Action persist,
        TextWriter output)
    {
        if (attempt.AutomatedEvidence.TryGetValue("li03.reviewPath", out var existingReview)
            && File.Exists(existingReview))
        {
            OpenReview(existingReview);
            output.WriteLine($"Reopened the retained LI03 review sheet: {existingReview}");
            return null;
        }

        await using var isolatedRuntime = await IsolatedOllamaRuntime.StartAsync(attempt, persist, output);
        var processId = isolatedRuntime.ProcessId;
        var startedTicks = isolatedRuntime.StartedUtcTicks;
        const string runtimeVersion = IsolatedOllamaRuntime.RuntimeVersion;

        var proofDirectory = Path.Combine(repositoryRoot, "experiments", "r02-local-inference-proof");
        var project = Path.Combine(proofDirectory, "Proof.csproj");
        output.WriteLine("Building the standalone inference proof without package restore...");
        var build = await RunAsync(
            repositoryRoot,
            "dotnet",
            "build",
            project,
            "--configuration",
            "Release",
            "--no-restore");
        attempt.AutomatedEvidence["li03.buildExitCode"] = build.ExitCode.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        attempt.AutomatedEvidence["li03.buildOutput"] = Tail(build.Output);
        persist();
        if (build.ExitCode != 0)
        {
            return "The inference proof did not build. No inference was started; see li03.buildOutput.";
        }

        var proofExecutable = Path.Combine(proofDirectory, "bin", "Release", "net10.0", "Proof.exe");
        if (!File.Exists(proofExecutable))
        {
            return $"The proof executable was not found at {proofExecutable}.";
        }

        var evidenceDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kora",
            "ManualProofHarness",
            "evidence",
            $"LI03-{attempt.AttemptId:N}");
        Directory.CreateDirectory(evidenceDirectory);
        var selfTestPath = Path.Combine(evidenceDirectory, "self-test.json");
        var measurementPath = Path.Combine(evidenceDirectory, "measurement.json");
        var assessmentPath = Path.Combine(evidenceDirectory, "assessment.json");
        var reviewPath = Path.Combine(evidenceDirectory, "human-review.html");
        var dispositionPath = Path.Combine(evidenceDirectory, "automated-disposition.json");
        attempt.AutomatedEvidence["li03.evidenceDirectory"] = evidenceDirectory;
        persist();

        output.WriteLine("Running deterministic proof checks...");
        var selfTest = await RunAsync(
            repositoryRoot,
            proofExecutable,
            "self-test",
            "--output",
            selfTestPath);
        attempt.AutomatedEvidence["li03.selfTestExitCode"] = selfTest.ExitCode.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        attempt.AutomatedEvidence["li03.selfTestOutput"] = Tail(selfTest.Output);
        persist();
        if (selfTest.ExitCode != 0)
        {
            return "The deterministic inference proof checks failed. No live inference was started.";
        }

        output.WriteLine("Generating LI03 evidence. Progress from all cold, warm, cancellation and recovery trials follows.");
        var measurementArguments = new List<string>
        {
            "measure",
            "--output",
            measurementPath,
            "--server-pid",
            processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--server-started-ticks",
            startedTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--exclusive-runtime",
            "--runtime-version",
            runtimeVersion,
            "--endpoint",
            IsolatedOllamaRuntime.Endpoint,
            "--trials",
            "30",
        };

        var measurementExit = await RunVisibleAsync(
            repositoryRoot,
            proofExecutable,
            [.. measurementArguments]);
        attempt.AutomatedEvidence["li03.measurementExitCode"] = measurementExit.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        attempt.AutomatedEvidence["li03.measurementPath"] = measurementPath;
        persist();
        if (measurementExit is not 0 and not 3)
        {
            return $"LI03 measurement exited {measurementExit}. Evidence was retained; do not replay automatically.";
        }
        Li03AutomatedDisposition.Create(measurementPath, dispositionPath);
        attempt.AutomatedEvidence["li03.automatedDispositionPath"] = dispositionPath;
        persist();
        if (measurementExit == 3)
        {
            return "LI03 was rejected by automated gates. Human review is not required to reject this run; retained evidence may be inspected diagnostically.";
        }

        output.WriteLine("Deriving offline factual checks from the retained answers...");
        var assessment = await RunAsync(
            repositoryRoot,
            proofExecutable,
            "assess-answers",
            "--output",
            assessmentPath,
            "--input",
            measurementPath);
        attempt.AutomatedEvidence["li03.assessmentExitCode"] = assessment.ExitCode.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        attempt.AutomatedEvidence["li03.assessmentOutput"] = Tail(assessment.Output);
        attempt.AutomatedEvidence["li03.assessmentPath"] = assessmentPath;
        persist();
        if (assessment.ExitCode != 0)
        {
            return "Offline answer assessment failed. Raw measurement evidence was retained.";
        }

        var uniqueAnswers = Li03ReviewReport.Create(
            assessmentPath,
            Path.Combine(proofDirectory, "fixtures.json"),
            reviewPath);
        attempt.AutomatedEvidence["li03.uniqueAnswers"] = uniqueAnswers.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        attempt.AutomatedEvidence["li03.reviewPath"] = reviewPath;
        persist();
        OpenReview(reviewPath);
        output.WriteLine($"Opened a local review sheet containing {uniqueAnswers} unique answers. Repeated identical answers are grouped.");
        return null;
    }

    private static async Task CaptureContextAsync(string repositoryRoot, ProofAttempt attempt, Action persist)
    {
        if (attempt.AutomatedEvidence.ContainsKey("source.revision"))
        {
            return;
        }

        var revision = await RunAsync(repositoryRoot, "git", "--no-pager", "rev-parse", "HEAD");
        if (revision.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not identify the source revision: {revision.Output}");
        }

        attempt.AutomatedEvidence["source.revision"] = revision.Output.Trim();
        attempt.AutomatedEvidence["machine.os"] = RuntimeInformation.OSDescription;
        attempt.AutomatedEvidence["machine.framework"] = RuntimeInformation.FrameworkDescription;
        attempt.AutomatedEvidence["machine.processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString();
        attempt.AutomatedEvidence["machine.processorCount"] = Environment.ProcessorCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        persist();
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("The Kora repository root could not be located.");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        return (process.ExitCode, string.IsNullOrEmpty(error) ? output : $"{output}{Environment.NewLine}{error}");
    }

    private static async Task<int> RunVisibleAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static void OpenReview(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string Tail(string value)
    {
        const int maximumLength = 4000;
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[^maximumLength..];
    }
}
