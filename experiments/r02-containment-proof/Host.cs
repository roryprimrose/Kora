using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace ContainmentProof;

internal static class Host
{
    internal static int Run(string output, string powershell, string consent, bool networkHandoff = false)
    {
        InvocationPolicy.RequireOwnedTrialConsent(consent);
        output = Path.GetFullPath(output);
        powershell = Path.GetFullPath(powershell);
        if (Directory.Exists(output)) throw new ArgumentException("Evidence destination must be new");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Explicit PowerShell runtime missing", powershell);
        var token = Native.Token();
        if (token.AppContainer) throw new InvalidOperationException("Host must be outside AppContainer");
        if (token.Elevated) throw new InvalidOperationException("Privileged trials are not admitted by this proof");
        Directory.CreateDirectory(output);
        string name = $"kora.r02.{Guid.NewGuid():N}";
        string scratch = Path.Combine(output, name);
        string credential = $"{name}.synthetic";
        IntPtr sid = IntPtr.Zero;
        bool credentialCreated = false;
        var trials = new List<Trial>();
        var cleanup = new List<string>();
        try
        {
            Directory.CreateDirectory(scratch);
            ProtectDirectory(scratch, token.UserSid, null, FileSystemRights.ReadAndExecute);
            sid = Native.CreateContainer(name);
            string containerSid = Native.SidString(sid);
            Native.CreateCredential(credential);
            credentialCreated = true;
            if (Native.ReadCredential(credential).Outcome != "Allowed")
                throw new InvalidOperationException("Host cannot read its synthetic credential");
            string address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .First(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).ToString();
            using var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start(32);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            // Only RX is granted to the worker for the harness and its local runtime copy.
            string runtime = Path.Combine(scratch, "runtime");
            CopyTree(AppContext.BaseDirectory, Path.Combine(runtime, "proof"));
            CopyTree(Path.GetDirectoryName(powershell)!, Path.Combine(runtime, "powershell"));
            ProtectDirectory(runtime, token.UserSid, containerSid, FileSystemRights.ReadAndExecute);
            ProtectDirectory(scratch, token.UserSid, containerSid, FileSystemRights.ReadAndExecute);
            string exe = Path.Combine(runtime, "proof", "ContainmentProof.exe");
            string pwsh = Path.Combine(runtime, "powershell", Path.GetFileName(powershell));
            string protectedRoot = Path.Combine(scratch, "protected-kora");
            Directory.CreateDirectory(protectedRoot);
            ProtectDirectory(protectedRoot, token.UserSid, null, FileSystemRights.ReadAndExecute);
            foreach (var profile in new[] { "job-only-complete", "appcontainer-complete",
                "appcontainer-cancel", "appcontainer-lost-receipt", "appcontainer-malformed-receipt" })
            {
                bool contained = profile.StartsWith("appcontainer", StringComparison.Ordinal);
                string allowed = Path.Combine(scratch, profile);
                Directory.CreateDirectory(allowed);
                ProtectDirectory(allowed, token.UserSid, containerSid, FileSystemRights.Modify);
                string file = Path.Combine(allowed, "allowed.txt");
                File.WriteAllText(file, "synthetic-allowed");
                string target = Path.Combine(protectedRoot, $"{profile}.txt");
                File.WriteAllText(target, "synthetic Kora binary/source/.git/build/trust/updater fixture");
                File.Copy(target, target + ".delete");
                File.Copy(target, target + ".rename");
                File.Copy(target, target + ".managed-rename");
                string alias = Path.Combine(allowed, "protected-hardlink.txt");
                Native.HardLink(alias, target);
                var spec = new Spec(profile, file, target, alias, credential, port, address, pwsh, exe,
                    profile.EndsWith("lost-receipt", StringComparison.Ordinal));
                string specPath = Path.Combine(allowed, "spec.json");
                Wire.Write(specPath, spec);
                var started = DateTimeOffset.UtcNow;
                var trial = Trial(profile, spec, specPath, contained ? sid : IntPtr.Zero, allowed);
                var ended = DateTimeOffset.UtcNow;
                trials.Add(trial);
                if (networkHandoff && NetworkCollection.Profiles.Contains(profile, StringComparer.Ordinal))
                {
                    Wire.Write(Path.Combine(output, "trials.json"), trials);
                    CollectNetworkAsync(output, spec, trial, scratch, token, started, ended)
                        .GetAwaiter().GetResult();
                }
            }
            Wire.Write(Path.Combine(output, "environment.json"), new
            {
                Os = Environment.OSVersion.VersionString,
                Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                HostToken = token, WorkerSid = containerSid, Capabilities = Array.Empty<string>(),
                OriginalPowerShell = powershell, LocalRuntimeCopy = true,
                ExecutableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))),
                AssemblySha256 = Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Path.ChangeExtension(exe, ".dll")))),
                PowerShellSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pwsh))),
                Utc = DateTimeOffset.UtcNow,
            });
            Wire.Write(Path.Combine(output, "trials.json"), trials);
            return ProofTests.Verify(trials, Path.Combine(output, "verification.json"));
        }
        finally
        {
            // Cleanup is observable and failure makes the invocation fail.
            try
            {
                if (credentialCreated) { Native.DeleteCredential(credential); cleanup.Add("Synthetic credential deleted"); }
            }
            finally
            {
                try
                {
                    if (sid != IntPtr.Zero)
                    {
                        Native.ReleaseSid(sid);
                        Native.DeleteContainer(name);
                        cleanup.Add("Temporary AppContainer profile deleted");
                    }
                }
                finally
                {
                    if (Directory.Exists(scratch))
                    {
                        Directory.Delete(scratch, recursive: true);
                        cleanup.Add("Exact owned scratch directory deleted");
                    }
                    Wire.Write(Path.Combine(output, "cleanup.json"), cleanup);
                }
            }
        }
    }

    private static async Task CollectNetworkAsync(string output, Spec spec, Trial trial,
        string scratch, TokenFacts host, DateTimeOffset started, DateTimeOffset ended)
    {
        if (trial.Receipt is null || trial.Receipt.RunId != trial.Profile ||
            !trial.TreeStopped || trial.LaunchError is not null || trial.Failure is not null ||
            trial.Receipt.Token.UserSid != host.UserSid || trial.Receipt.Token.Elevated ||
            trial.Receipt.Token.AppContainer != trial.Profile.StartsWith("appcontainer", StringComparison.Ordinal))
            throw new InvalidDataException("Cannot request collection for an unverifiable worker trial.");
        NetworkImage Image(string name, string path) =>
            new(name, path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var request = new NetworkRequest(Guid.NewGuid().ToString("N"), trial.Profile, scratch,
            host.UserSid, trial.Receipt.Token.ContainerSid, started, ended, spec.Address, spec.Port,
            [Image("dotnet", spec.Executable), Image("powershell", spec.PowerShell)]);
        NetworkCollection.Validate(request, output, DateTimeOffset.UtcNow);
        string path = Path.Combine(output, $"network-{trial.Profile}.request.json");
        string completionPath = Path.Combine(output, $"network-{trial.Profile}.completed.json");
        string temporary = path + ".tmp";
        Wire.Write(temporary, request);
        string digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(temporary)));
        File.Move(temporary, path);
        Console.WriteLine($"W1 collector request ready: {path}; assets retained for at most 90 seconds.");
        var clock = Stopwatch.StartNew();
        await NetworkCollection.AwaitCompletionAsync(request.RequestId, digest, async () =>
        {
            if (!File.Exists(completionPath)) return null;
            if (new FileInfo(completionPath).Length > NetworkCollection.MaximumJsonBytes)
                throw new InvalidDataException("Collector receipt exceeds 64 KiB.");
            return System.Text.Json.JsonSerializer.Deserialize<NetworkCompletion>(
                await File.ReadAllTextAsync(completionPath), Wire.Json)
                ?? throw new InvalidDataException("Empty collector receipt.");
        }, () => clock.Elapsed, () => Task.Delay(200));
        Console.WriteLine("W1 filtered XML received for inspection; denial attribution remains unproven.");
    }

    private static Trial Trial(string profile, Spec spec, string specPath, IntPtr sid, string directory)
    {
        IntPtr job = Native.Job();
        Native.ProcessInfo process = default;
        var handles = new List<IntPtr>();
        var pids = new List<int>();
        Receipt? receipt = null;
        string? failure = null;
        int? launchError = null;
        int? exitCode = null;
        string state = "Unknown";
        bool stopped = false;
        double elapsed = 0;
        var before = new[] { spec.Protected, spec.Protected + ".delete", spec.Protected + ".rename",
            spec.Protected + ".managed-rename" }
            .ToDictionary(path => path, File.ReadAllBytes);
        try
        {
            try
            {
                process = Native.Launch(spec.Executable, $"worker \"{specPath}\"", directory, sid,
                    EnvironmentBlock(directory), job);
                string report = Path.ChangeExtension(specPath, ".receipt.json");
                var deadline = Stopwatch.StartNew();
                while (!Native.Exited(process.Process) && !File.Exists(report) && deadline.Elapsed.TotalSeconds < 25)
                    Thread.Sleep(25);
                // File existence is not a complete receipt: the host waits for a parseable bounded snapshot.
                if (File.Exists(report))
                {
                    for (int i = 0; i < 40; i++)
                    {
                        try
                        {
                            if (new FileInfo(report).Length > 64 * 1024)
                            {
                                failure = "Receipt exceeds 64 KiB; effect remains Unknown";
                                break;
                            }
                            receipt = Wire.Read<Receipt>(report);
                            if (receipt.RunId != spec.RunId || receipt.Pid != process.Pid)
                            {
                                failure = "Uncorrelated receipt; effect remains Unknown";
                                receipt = null;
                            }
                            break;
                        }
                        catch (Exception error) when (error is System.Text.Json.JsonException or IOException
                            or UnauthorizedAccessException)
                        {
                            if (i == 39) failure = $"Unverifiable receipt; effect remains Unknown: {error.Message}";
                            else Thread.Sleep(25);
                        }
                    }
                }
                state = Native.Exited(process.Process) ? "Exited" :
                    failure is not null ? "Aborted" : receipt is not null ? "Cancelled" : "TimedOut";
                // Let normal child -> grandchild startup publish its actual token.
                Thread.Sleep(500);
                if (Native.Exited(process.Process)) state = "Exited";
                pids = Native.JobPids(job);
                foreach (int pid in pids)
                {
                    using var tracked = Process.GetProcessById(pid);
                    // Duplicate through Process.Handle so verification holds a stable process identity, not a reused PID.
                    handles.Add(NativeProcessHandle.Duplicate(tracked.Handle));
                }
            }
            catch (Win32Exception error)
            {
                launchError = error.NativeErrorCode;
                failure = error.Message;
                state = process.Process == IntPtr.Zero ? "LaunchFailed" : "Unknown";
            }
        }
        finally
        {
            var shutdown = Stopwatch.StartNew();
            Native.Close(job);
            try
            {
                while (handles.Any(h => !Native.Exited(h)) && shutdown.Elapsed.TotalSeconds < 5)
                    Thread.Sleep(10);
                stopped = handles.Count > 0 && handles.All(Native.Exited);
                elapsed = shutdown.Elapsed.TotalMilliseconds;
                if (process.Process != IntPtr.Zero && Native.Exited(process.Process))
                    exitCode = Native.ExitCode(process.Process);
                foreach (var handle in handles) Native.Close(handle);
            }
            finally
            {
                Native.Close(process.Thread);
                Native.Close(process.Process);
            }
        }
        // Breakaway is expected to fail. If it unexpectedly succeeds, terminate only our recorded child.
        if (receipt is not null)
        {
            foreach (int child in receipt.Children.Except(pids))
            {
                failure = $"Unexpected child {child} outside job; profile unsupported";
                using var escaped = Process.GetProcessById(child);
                escaped.Kill(entireProcessTree: true);
                escaped.WaitForExit(5000);
                stopped = false;
            }
        }
        var childTokens = Directory.GetFiles(directory, "child-*.json")
            .Select(Wire.Read<TokenFacts>).ToList();
        var spawns = Directory.GetFiles(directory, "spawn-*.json").Select(Wire.Read<Probe>).ToList();
        if (spawns.Any(p => p.Outcome != "Allowed")) failure = "Grandchild creation failed";
        var failures = Directory.GetFiles(directory, "*.failure.txt").Select(File.ReadAllText).ToList();
        if (failures.Count > 0) failure = string.Join(Environment.NewLine, failures);
        if (sid != IntPtr.Zero && childTokens.Any(t => !t.AppContainer || t.ContainerSid != Native.SidString(sid)))
            failure = "Descendant escaped AppContainer identity";
        return new(profile, state, Wire.EffectOutcome(spec.RunId, receipt), exitCode, launchError,
            receipt, pids, stopped, elapsed,
            before.All(pair => File.Exists(pair.Key) && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))), childTokens,
            File.Exists(Path.Combine(directory, "effect.txt")), failure);
    }

    private static Dictionary<string, string> EnvironmentBlock(string directory) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["TEMP"] = directory, ["TMP"] = directory, ["USERPROFILE"] = directory, ["HOME"] = directory,
        ["LOCALAPPDATA"] = directory, ["APPDATA"] = directory, ["PSModulePath"] = "",
        ["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        ["DOTNET_EnableDiagnostics"] = "0", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
    };

    private static void ProtectDirectory(string path, string host, string? worker, FileSystemRights rights)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(host), FileSystemRights.FullControl,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        if (worker is not null)
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(worker), rights,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string child in Directory.GetDirectories(source))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Runtime has unexpected reparse point: {child}");
            CopyTree(child, Path.Combine(destination, Path.GetFileName(child)));
        }
    }
}
