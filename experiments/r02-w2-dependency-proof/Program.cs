using ContainmentProof;
using W2Proof;

try
{
    if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        throw new PlatformNotSupportedException("Non-elevated Windows x64 only");
    return args switch
    {
        ["run", var output, var powershell, var declared, var undeclared] =>
            Host.Run(output, powershell, declared, undeclared),
        ["worker", var spec] => Worker.Run(spec),
        _ => throw new ArgumentException("Use run <new-evidence> <pwsh> <declared-payload> <undeclared-payload>"),
    };
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine(error);
    return 1;
}
