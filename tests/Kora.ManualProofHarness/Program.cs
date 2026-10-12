namespace Kora.ManualProofHarness;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--render-li03-review", var assessmentPath, var fixturesPath, var outputPath])
            {
                var count = Li03ReviewReport.Create(
                    Path.GetFullPath(assessmentPath),
                    Path.GetFullPath(fixturesPath),
                    Path.GetFullPath(outputPath));
                Console.WriteLine($"Rendered {count} unique LI03 answers to {Path.GetFullPath(outputPath)}.");
                return 0;
            }
            if (args is ["--create-li03-disposition", var measurementPath, var dispositionPath])
            {
                Li03AutomatedDisposition.Create(
                    Path.GetFullPath(measurementPath),
                    Path.GetFullPath(dispositionPath));
                Console.WriteLine($"Created LI03 disposition at {Path.GetFullPath(dispositionPath)}.");
                return 0;
            }

            var options = HarnessOptions.Parse(args);
            if (options.SelfTest)
            {
                return HarnessSelfTest.Run();
            }

            var store = new ResultStore(options.ResultsPath);
            var runner = new HarnessRunner(
                ProofCatalog.Cases,
                store,
                new ProofAutomation(),
                Console.In,
                Console.Out);
            await runner.RunAsync(options.ResumeProofId, options.StartProofId, options.AutomateProofId);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Manual proof harness failed: {exception.Message}");
            return 1;
        }
    }
}
