using ContainmentProof;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Unsupported OS: proof requires Windows; no worker was launched.");
    return 2;
}
try
{
    return args switch
    {
        ["self-test"] => ProofTests.SelfTest(),
        ["run", var output, var powershell, var consent] => Host.Run(output, powershell, consent),
        ["run-with-network-handoff", var output, var powershell, var consent] =>
            Host.Run(output, powershell, consent, true),
        ["collect-network" or "collect-network-user-comparison", var output, var consent] =>
            await NetworkObserver.RunAsync(output, NetworkCollection.ObserverMode(args[0], consent)),
        ["worker", var spec] => Worker.Run(spec),
        ["sleeper", var spec] => Worker.Sleeper(spec, false),
        ["grandchild", var spec] => Worker.Sleeper(spec, true),
        _ => throw new ArgumentException("Use: self-test | run <new-evidence-directory> <pwsh.exe> " +
            InvocationPolicy.OwnedTrialConsent + " | run-with-network-handoff <new-evidence-directory> <pwsh.exe> " +
            InvocationPolicy.OwnedTrialConsent + " | " +
            "collect-network <evidence-directory> consent-filtered-buffered-events | " +
            "collect-network-user-comparison <evidence-directory> consent-application-endpoint-events-without-user-filter"),
    };
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    if (args is ["worker" or "sleeper" or "grandchild", var spec])
        File.WriteAllText(Path.ChangeExtension(spec, $".{Environment.ProcessId}.failure.txt"), error.ToString());
    return 1;
}
