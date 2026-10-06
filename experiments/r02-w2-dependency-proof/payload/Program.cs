using ContainmentProof;
using W2Payload;

if (args.Length != 2 || args[1] is not ("child" or "grandchild"))
    throw new ArgumentException("Fixed child/grandchild fixture only");
string directory = Path.GetFullPath(args[0]);
if (!Directory.Exists(directory) || !File.Exists(Path.Combine(directory, "w2-owned-scratch")))
    throw new InvalidDataException("Owned fixture marker missing");
int grandchild = 0;
if (args[1] == "child")
{
    var probe = Native.StartChild(Environment.ProcessPath!,
        $"\"{directory}\" grandchild", false, out grandchild);
    if (probe.Outcome != "Allowed") throw new InvalidOperationException("Grandchild launch failed");
}
Wire.Write(Path.Combine(directory, $"payload-{Environment.ProcessId}.json"), new
{
    Pid = Environment.ProcessId, Role = args[1], Value = FixedEffect.Observe(),
    Image = Environment.ProcessPath, Token = Native.Token(), Grandchild = grandchild,
    EnvironmentSentinel = Environment.GetEnvironmentVariable("W2_SENTINEL"),
});
Thread.Sleep(TimeSpan.FromSeconds(120));
