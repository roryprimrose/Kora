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
        ["run", var output, var powershell] => Host.Run(output, powershell),
        ["worker", var spec] => Worker.Run(spec),
        ["sleeper", var spec] => Worker.Sleeper(spec, false),
        ["grandchild", var spec] => Worker.Sleeper(spec, true),
        _ => throw new ArgumentException("Use: self-test | run <new-evidence-directory> <pwsh.exe>"),
    };
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    if (args is ["worker" or "sleeper" or "grandchild", var spec])
        File.WriteAllText(Path.ChangeExtension(spec, $".{Environment.ProcessId}.failure.txt"), error.ToString());
    return 1;
}
