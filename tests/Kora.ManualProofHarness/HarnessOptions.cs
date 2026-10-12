namespace Kora.ManualProofHarness;

internal sealed record HarnessOptions(
    string ResultsPath,
    bool SelfTest,
    string? ResumeProofId,
    string? StartProofId,
    string? AutomateProofId)
{
    internal static HarnessOptions Parse(string[] args)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("A local application data directory is required.");
        }

        var resultsPath = Path.Combine(root, "Kora", "ManualProofHarness", "results.json");
        var selfTest = false;
        string? resumeProofId = null;
        string? startProofId = null;
        string? automateProofId = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--results":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException("--results requires a file path.");
                    }

                    resultsPath = Path.GetFullPath(args[index]);
                    break;
                case "--self-test":
                    selfTest = true;
                    break;
                case "--resume":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException("--resume requires a proof ID.");
                    }

                    resumeProofId = args[index];
                    break;
                case "--start":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException("--start requires a proof ID.");
                    }

                    startProofId = args[index];
                    break;
                case "--automate":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException("--automate requires a proof ID.");
                    }

                    automateProofId = args[index];
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (new[] { resumeProofId, startProofId, automateProofId }.Count(value => value is not null) > 1)
        {
            throw new ArgumentException("--resume, --start and --automate cannot be combined.");
        }

        return new(resultsPath, selfTest, resumeProofId, startProofId, automateProofId);
    }
}
