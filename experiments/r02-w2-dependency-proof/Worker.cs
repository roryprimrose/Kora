using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using ContainmentProof;

namespace W2Proof;

internal sealed record WorkerSpec(string RunId, string Directory, string Declared, string Undeclared,
    string Denied, string Writable, string AdmissionDigest, bool BlockChildren, long HostHandle, bool HasNative);

internal sealed record Observation(string Name, string Outcome, int? NativeError = null, int? Value = null,
    string? Detail = null);
internal sealed record WorkerReceipt(string RunId, int Pid, string AdmissionDigest, TokenFacts Token,
    uint ChildPolicy, Observation HostHandleProbe, string? AmbientSentinel, List<Observation> Probes);

internal static class Worker
{
    internal static int Run(string path)
    {
        var spec = Wire.Read<WorkerSpec>(path);
        if (!File.Exists(Path.Combine(spec.Directory, "w2-owned-scratch")))
            throw new InvalidDataException("Owned marker missing");
        try { return RunCore(path, spec); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Wire.Write(path + ".failure.json", new { Type = error.GetType().FullName, error.Message,
                Detail = error.ToString() });
            throw;
        }
    }

    private static int RunCore(string path, WorkerSpec spec)
    {
        uint policy = spec.BlockChildren ? NativePolicy.BlockChildren() : 0;
        var probes = new List<Observation>();
        foreach (var (name, directory) in new[]
        {
            ("declared", spec.Declared), ("undeclared.rx", spec.Undeclared),
            ("denied", spec.Denied), ("writable.noexecute", spec.Writable),
        })
        {
            var child = Native.StartChild(Path.Combine(directory, "W2Payload.exe"),
                $"\"{spec.Directory}\" child", false, out _);
            probes.Add(new(name + ".child", child.NativeError is { } code
                ? Admission.ClassifyNative(code) : child.Outcome, child.NativeError));
            probes.Add(Load(name + ".managed.path", Path.Combine(directory, "W2Payload.dll"), false));
            probes.Add(Load(name + ".managed.bytes", Path.Combine(directory, "W2Payload.dll"), true));
            if (spec.HasNative)
                probes.Add(NativePolicy.LoadNative(name + ".native", Path.Combine(directory, "W2Native.dll")));
        }
        var receipt = new WorkerReceipt(spec.RunId, Environment.ProcessId, spec.AdmissionDigest,
            Native.Token(), policy, NativePolicy.HandleObservation(spec.HostHandle),
            Environment.GetEnvironmentVariable("W2_SENTINEL"), probes);
        Wire.Write(path + ".receipt.tmp", receipt);
        File.Move(path + ".receipt.tmp", path + ".receipt.json");
        Wire.Write(Path.Combine(spec.Directory, "loaded-images.json"), Images.Capture());
        return 0;
    }

    private static Observation Load(string name, string path, bool bytes)
    {
        var context = new AssemblyLoadContext(name, isCollectible: true);
        try
        {
            Assembly assembly;
            if (bytes)
            {
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                assembly = context.LoadFromStream(stream);
            }
            else assembly = context.LoadFromAssemblyPath(path);
            var method = assembly.GetType("W2Payload.FixedEffect", throwOnError: true)!
                .GetMethod("Observe") ?? throw new InvalidDataException("Synthetic method missing");
            int value = (int)(method.Invoke(null, null) ?? throw new InvalidDataException("Missing value"));
            return new(name, "Allowed", Value: value);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or Win32Exception)
        {
            int code = error is Win32Exception native ? native.NativeErrorCode : error.HResult & 0xffff;
            return new(name, Admission.ClassifyNative(code), code);
        }
        finally { context.Unload(); }
    }
}
