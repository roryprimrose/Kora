using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ContainmentProof;

internal static class Worker
{
    internal static int Run(string specPath)
    {
        var spec = Wire.Read<Spec>(specPath);
        var probes = new List<Probe>
        {
            Access("filesystem.allowed.read", () => File.ReadAllText(spec.Allowed)),
            Access("filesystem.allowed.write", () => File.AppendAllText(spec.Allowed, "-worker")),
            Access("filesystem.protected.read", () => File.ReadAllText(spec.Protected)),
            Access("filesystem.protected.write", () => File.AppendAllText(spec.Protected, "-worker")),
            Access("filesystem.protected.delete", () => File.Delete(spec.Protected + ".delete")),
            Access("filesystem.protected.rename.managed", () =>
                File.Move(spec.Protected + ".managed-rename", spec.Protected + ".managed-moved")),
            Native.Rename(spec.Protected + ".rename", spec.Protected + ".moved"),
            Access("filesystem.hardlink.write", () => File.AppendAllText(spec.Alias, "-alias")),
            Access("filesystem.traversal.write", () => File.AppendAllText(Path.Combine(
                Path.GetDirectoryName(spec.Allowed)!, "..", "protected-kora", Path.GetFileName(spec.Protected)), "-traversal")),
            Network("network.loopback", "127.0.0.1", spec.Port),
            Network("network.owned-interface", spec.Address, spec.Port),
            Native.ReadCredential(spec.Credential),
            Native.OpenDesktop(),
        };
        var children = new List<int>();
        probes.Add(Native.StartChild(spec.Executable, $"sleeper \"{specPath}\"", false, out int normal));
        if (normal != 0) children.Add(normal);
        probes.Add(Native.StartChild(spec.Executable, $"sleeper \"{specPath}\"", true, out int escaped));
        if (escaped != 0) children.Add(escaped);
        probes.Add(PowerShell(spec));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(spec.Allowed)!, "effect.txt"),
            "synthetic effect occurred; receipt may be lost");
        if (spec.RunId.EndsWith("-malformed-receipt", StringComparison.Ordinal))
        {
            File.WriteAllText(Path.ChangeExtension(specPath, ".receipt.json"), "{unverifiable");
        }
        else if (!spec.LoseReceipt)
        {
            Wire.Write(Path.ChangeExtension(specPath, ".receipt.json"),
                new Receipt(spec.RunId, Environment.ProcessId, Native.Token(), probes, children));
        }
        if (spec.RunId.EndsWith("-complete", StringComparison.Ordinal)) return 0;
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    internal static int Sleeper(string specPath, bool grandchild)
    {
        var spec = Wire.Read<Spec>(specPath);
        Wire.Write(Path.Combine(Path.GetDirectoryName(spec.Allowed)!, $"child-{Environment.ProcessId}.json"),
            Native.Token());
        if (!grandchild)
        {
            var result = Native.StartChild(spec.Executable, $"grandchild \"{specPath}\"", false, out _);
            Wire.Write(Path.Combine(Path.GetDirectoryName(spec.Allowed)!, $"spawn-{Environment.ProcessId}.json"),
                result);
        }
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    internal static Probe Access(string name, Action action)
    {
        try { action(); return new(name, "Allowed"); }
        catch (UnauthorizedAccessException error) { return new(name, "Denied", error.HResult & 0xffff); }
        catch (IOException error) { return new(name, "Unknown", error.HResult & 0xffff, error.GetType().Name); }
    }

    private static Probe Network(string name, string address, int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            socket.ConnectAsync(address, port, timeout.Token).AsTask().GetAwaiter().GetResult();
            return new(name, "Allowed");
        }
        catch (SocketException error)
        {
            return new(name, error.NativeErrorCode == 10013 ? "Denied" : "Unknown",
                error.NativeErrorCode);
        }
        catch (OperationCanceledException) { return new(name, "Unknown", Detail: "Timed out"); }
    }

    private static Probe PowerShell(Spec spec)
    {
        using var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ContainmentProof.FixedProbe.ps1")
            ?? throw new InvalidDataException("Fixed PowerShell resource missing");
        using var reader = new StreamReader(resource, Encoding.UTF8);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(reader.ReadToEnd()));
        var start = new ProcessStartInfo(spec.PowerShell)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded },
        };
        start.Environment["KORA_ALLOWED"] = spec.Allowed;
        start.Environment["KORA_PROTECTED"] = spec.Protected;
        start.Environment["KORA_PORT"] = spec.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["KORA_ADDRESS"] = spec.Address;
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("No PowerShell process");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                string stdout = output.GetAwaiter().GetResult();
                string stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    return new("powershell.fixed", "Unknown", process.ExitCode, stderr.Trim());
                // Validate the fixed receipt instead of treating exit zero as successful execution.
                using var json = JsonDocument.Parse(stdout);
                if (!json.RootElement.TryGetProperty("version", out _))
                    return new("powershell.fixed", "Unknown", Detail: "Missing version receipt");
                return new("powershell.fixed", "Observed", Detail: stdout.Trim());
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return new("powershell.fixed", "Unknown", Detail: "Interpreter deadline exceeded");
            }
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            return new("powershell.fixed", "Unknown", error.NativeErrorCode, error.Message);
        }
    }
}
