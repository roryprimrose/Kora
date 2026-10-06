using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using ContainmentProof;

namespace W2Proof;

internal static class Host
{
    internal static int Run(string output, string powershell, string declared, string undeclared)
    {
        output = Path.GetFullPath(output);
        powershell = Path.GetFullPath(powershell);
        declared = Path.GetFullPath(declared);
        undeclared = Path.GetFullPath(undeclared);
        if (Directory.Exists(output)) throw new ArgumentException("Evidence directory must be new");
        var token = Native.Token();
        if (token.Elevated || token.AppContainer) throw new InvalidOperationException("Non-elevated host required");
        if (!File.Exists(powershell) || Path.GetFileName(powershell) != "pwsh.exe")
            throw new InvalidDataException("Explicit existing PowerShell 7 runtime required");
        string name = "kora.w2." + Guid.NewGuid().ToString("N");
        string scratch = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(output);
        IntPtr sid = IntPtr.Zero;
        var cleanup = new List<string>();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        try
        {
            Directory.CreateDirectory(scratch);
            sid = Native.CreateContainer(name);
            string workerSid = Native.SidString(sid);
            Acl(scratch, token.UserSid, workerSid, FileSystemRights.ReadAndExecute);
            string runtime = Path.Combine(scratch, "runtime");
            CopyTree(AppContext.BaseDirectory, Path.Combine(runtime, "worker"));
            CopyTree(Path.GetDirectoryName(powershell)!, Path.Combine(runtime, "powershell"));
            Acl(runtime, token.UserSid, workerSid, FileSystemRights.ReadAndExecute);
            string declaredPath = Path.Combine(runtime, "declared");
            CopyTree(declared, declaredPath);
            string undeclaredPath = Path.Combine(runtime, "undeclared");
            CopyTree(undeclared, undeclaredPath);
            string denied = Path.Combine(scratch, "denied");
            CopyTree(undeclared, denied);
            Acl(denied, token.UserSid, null, FileSystemRights.ReadAndExecute);
            string data = Path.Combine(scratch, "data");
            CopyTree(undeclared, data);
            Acl(data, token.UserSid, workerSid, FileSystemRights.Modify, denyFileExecute: true);
            foreach (string file in Directory.GetFiles(data, "*", SearchOption.AllDirectories))
                DataFileAcl(file, token.UserSid, workerSid);
            File.WriteAllText(Path.Combine(data, "writable.ps1"), "'synthetic-undeclared-script'");
            File.WriteAllText(Path.Combine(denied, "denied.ps1"), "'synthetic-denied-script'");
            string moduleRoot = Path.Combine(data, "modules");
            string module = Path.Combine(moduleRoot, "W2Undeclared");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "W2Undeclared.psm1"),
                "function Get-W2Undeclared { 22 }; Export-ModuleMember -Function Get-W2Undeclared");
            File.WriteAllText(Path.Combine(module, "W2Undeclared.psd1"),
                "@{ RootModule='W2Undeclared.psm1'; ModuleVersion='1.0'; GUID='ae6c9178-d446-468d-9d8e-a3367e4c849b'; FunctionsToExport=@('Get-W2Undeclared') }");
            bool hasNative = File.Exists(Path.Combine(declaredPath, "W2Native.dll"))
                && File.Exists(Path.Combine(undeclaredPath, "W2Native.dll"));
            Wire.Write(Path.Combine(output, "acl-inputs.json"), new[]
            {
                new { Name = "data-exe", Sddl = new FileInfo(Path.Combine(data, "W2Payload.exe"))
                    .GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All) },
                new { Name = "data-module", Sddl = new FileInfo(Path.Combine(data, "W2Payload.dll"))
                    .GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All) },
            });
            var scripts = new[] { Resource("scripts\\entry.ps1", "scripts.entry.ps1"),
                Resource("scripts\\helper.ps1", "scripts.helper.ps1") };
            string scriptDigest = Admission.Digest("Kora.ScriptSet.v1", scripts);
            var definition = new[] { Resource("skill.json", "skill.json"),
                Resource("instructions.txt", "instructions.txt") };
            string definitionDigest = Admission.Digest("Kora.SkillDefinition.v1", definition);
            var declaredClosure = Inventory(declaredPath, runtime);
            var runtimeClosure = Inventory(runtime, runtime);
            var admittedRuntimeClosure = Inventory(Path.Combine(runtime, "worker"), runtime)
                .Concat(Inventory(Path.Combine(runtime, "powershell"), runtime)).ToArray();
            string admission = Admission.Digest("Kora.W2.Admission.v1",
            [
                new("script-set", Encoding.ASCII.GetBytes(scriptDigest)),
                new("definition", Encoding.ASCII.GetBytes(definitionDigest)),
                new("declared-closure", JsonSerializer.SerializeToUtf8Bytes(declaredClosure, Wire.Json)),
                new("runtime-closure", JsonSerializer.SerializeToUtf8Bytes(admittedRuntimeClosure, Wire.Json)),
                new("invocation", Encoding.ASCII.GetBytes("w2.synthetic.fixed;int32:2;marker-only")),
            ]);
            Wire.Write(Path.Combine(output, "inputs.json"), new
            {
                ScriptSetDigest = scriptDigest,
                DefinitionDigest = definitionDigest,
                AdmissionDigest = admission,
                Scripts = scripts,
                Definition = definition,
                DeclaredClosure = declaredClosure,
                RuntimeAndAttackFixtureInventory = runtimeClosure,
                ScratchOnlyAcl = true,
                InstalledProtectionProved = false,
            });
            var trials = new List<JsonElement>();
            using var sentinel = File.Create(Path.Combine(scratch, "host-handle.txt"));
            NativePolicy.MakeInheritable(sentinel.SafeFileHandle.DangerousGetHandle());
            string? priorSentinel = Environment.GetEnvironmentVariable("W2_SENTINEL");
            Environment.SetEnvironmentVariable("W2_SENTINEL", "synthetic-host-only");
            try
            {
                foreach (string profile in new[] { "baseline", "acl", "no-child" })
                {
                    bool block = profile == "no-child";
                    string directory = Path.Combine(scratch, profile);
                    Directory.CreateDirectory(directory);
                    Acl(directory, token.UserSid, workerSid, FileSystemRights.Modify, denyFileExecute: true);
                    File.WriteAllText(Path.Combine(directory, "w2-owned-scratch"), name);
                    var spec = new WorkerSpec(name + "." + profile, directory,
                        declaredPath, undeclaredPath, denied, data, admission, block,
                        sentinel.SafeFileHandle.DangerousGetHandle().ToInt64(), hasNative);
                    string path = Path.Combine(directory, "spec.json");
                    Wire.Write(path, spec);
                    var trial = Execute(Path.Combine(runtime, "worker", "W2Proof.exe"), $"worker \"{path}\"",
                        directory, profile == "baseline" ? IntPtr.Zero : sid, EnvironmentBlock(directory),
                        path + ".receipt.json", spec.RunId, admission);
                    Wire.Write(Path.Combine(output, profile + ".json"), trial);
                    trials.Add(JsonSerializer.SerializeToElement(trial, Wire.Json));
                }
            }
            finally { Environment.SetEnvironmentVariable("W2_SENTINEL", priorSentinel); }
            var psTrials = new List<JsonElement>();
            foreach (string mode in new[] { "baseline", "complete", "lost", "malformed", "cancel" })
            {
                string psDirectory = Path.Combine(scratch, "scripts-" + mode);
                Directory.CreateDirectory(psDirectory);
                Acl(psDirectory, token.UserSid, workerSid, FileSystemRights.Modify, denyFileExecute: true);
                string psReceipt = Path.Combine(psDirectory, "receipt.json");
                var psSpec = new
                {
                    RunId = name + ".scripts." + mode,
                    AdmissionDigest = admission,
                    Scripts = scripts,
                    Mode = mode,
                    DeniedScript = Path.Combine(denied, "denied.ps1"),
                    WritableScript = Path.Combine(data, "writable.ps1"),
                    ModuleRoot = moduleRoot,
                    Module = Path.Combine(module, "W2Undeclared.psd1"),
                    Payload = Path.Combine(data, "W2Payload.dll"),
                    Receipt = psReceipt,
                    DeclaredPayload = Path.Combine(declaredPath, "W2Payload.dll"),
                };
                var environment = EnvironmentBlock(psDirectory);
                environment["W2_SPEC"] = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(psSpec, Wire.Json));
                string command = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                    new UTF8Encoding(false, true).GetString(Resource("scripts\\probe.ps1", "scripts.probe.ps1").Bytes)));
                var psTrial = Execute(Path.Combine(runtime, "powershell", Path.GetFileName(powershell)),
                    $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {command}", psDirectory,
                    mode == "baseline" ? IntPtr.Zero : sid,
                    environment, psReceipt, psSpec.RunId, admission, cancelAtEffect: mode == "cancel");
                Wire.Write(Path.Combine(output, "scripts-" + mode + ".json"), psTrial);
                psTrials.Add(JsonSerializer.SerializeToElement(psTrial, Wire.Json));
            }
            var verification = Verify(trials, psTrials, admission);
            Wire.Write(Path.Combine(output, "verification.json"), verification);
            Wire.Write(Path.Combine(output, "environment.json"), new
            {
                StartedUtc = started,
                EndedUtc = DateTimeOffset.UtcNow,
                Os = Environment.OSVersion.VersionString,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Framework = RuntimeInformation.FrameworkDescription,
                OriginalPowerShell = powershell,
                OriginalPowerShellSha256 = Hash(powershell),
                HostToken = token,
                ContainerSid = workerSid,
                Capabilities = Array.Empty<string>(),
                Scratch = scratch,
                NativeTrial = hasNative ? "Run: synthetic C payload only" :
                    "Blocked/not run: paired reviewed synthetic native DLLs unavailable; compiler prerequisite not satisfied",
                NoNetworkTrial = true,
                NoInstalledAdmission = true,
            });
            Console.WriteLine(JsonSerializer.Serialize(verification, Wire.Json));
            return verification.HarnessErrors.Length > 0 ? 1 : 2;
        }
        finally
        {
            try
            {
                if (sid != IntPtr.Zero)
                {
                    Native.ReleaseSid(sid);
                    Native.DeleteContainer(name);
                    cleanup.Add("Exact temporary AppContainer profile deleted");
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(scratch))
                    {
                        Directory.Delete(scratch, recursive: true);
                        cleanup.Add("Exact owned scratch directory deleted");
                    }
                }
                finally { Wire.Write(Path.Combine(output, "cleanup.json"), new { Utc = DateTimeOffset.UtcNow, Events = cleanup }); }
            }
        }
    }

    private static object Execute(string exe, string args, string directory, IntPtr sid,
        Dictionary<string, string> environment, string receiptPath, string expectedRun,
        string admission, bool cancelAtEffect = false)
    {
        IntPtr job = Native.Job();
        Native.ProcessInfo process = default;
        var handles = new List<IntPtr>();
        JsonElement? receipt = null;
        var pids = new List<int>();
        var ownedProcesses = new List<OwnedProcess>();
        string? error = null;
        int? exit = null;
        bool stopped = false;
        double shutdownMs = 0;
        string state = "Unknown";
        var started = DateTimeOffset.UtcNow;
        try
        {
            process = Native.Launch(exe, args, directory, sid, environment, job);
            var deadline = Stopwatch.StartNew();
            while (!Native.Exited(process.Process) && deadline.Elapsed.TotalSeconds < 30
                && !(cancelAtEffect && File.Exists(receiptPath + ".effect"))) Thread.Sleep(10);
            state = Native.Exited(process.Process) ? "Exited" :
                cancelAtEffect && File.Exists(receiptPath + ".effect") ? "Cancelled" : "TimedOut";
            if (Native.Exited(process.Process)) exit = Native.ExitCode(process.Process);
            else error = state == "Cancelled" ? "Cancelled after marker; effect receipt Unknown" :
                "Worker deadline expired; effect Unknown";
            if (File.Exists(receiptPath))
            {
                if (new FileInfo(receiptPath).Length > 64 * 1024)
                    error = "Oversized receipt; effect Unknown";
                else
                {
                    try
                    {
                        var candidate = Wire.Read<JsonElement>(receiptPath);
                        if (candidate.ValueKind != JsonValueKind.Object
                            || !candidate.TryGetProperty("RunId", out var run)
                            || run.ValueKind != JsonValueKind.String || run.GetString() != expectedRun
                            || !candidate.TryGetProperty("Pid", out var pid)
                            || pid.ValueKind != JsonValueKind.Number
                            || !pid.TryGetInt32(out int identity) || identity != process.Pid)
                            error = "Uncorrelated receipt; effect Unknown";
                        else receipt = candidate;
                    }
                    catch (JsonException failure) { error = "Malformed receipt: " + failure.Message; }
                }
            }
            else error ??= "Missing receipt; effect Unknown";
            if (receipt is { } value && value.TryGetProperty("Probes", out var observations))
            {
                int childCount = observations.EnumerateArray().Count(p =>
                    p.GetProperty("Name").GetString()!.EndsWith(".child", StringComparison.Ordinal)
                    && p.GetProperty("Outcome").GetString() == "Allowed");
                while (Directory.GetFiles(directory, "payload-*.json").Length < childCount * 2
                    && deadline.Elapsed.TotalSeconds < 30) Thread.Sleep(10);
                if (Directory.GetFiles(directory, "payload-*.json").Length != childCount * 2)
                    error = "Descendant receipt deadline/shape failure";
            }
            pids = Native.JobPids(job);
            foreach (int pid in pids)
            {
                using var tracked = Process.GetProcessById(pid);
                handles.Add(NativeProcessHandle.Duplicate(tracked.Handle));
                try
                {
                    ownedProcesses.Add(new(pid, tracked.MainModule?.FileName,
                        new DateTimeOffset(tracked.StartTime.ToUniversalTime()), null));
                }
                catch (Win32Exception failure)
                {
                    ownedProcesses.Add(new(pid, null, null,
                        $"Unknown owned process image: native error {failure.NativeErrorCode}"));
                }
            }
        }
        catch (Win32Exception failure) { error = $"Native error {failure.NativeErrorCode}: {failure.Message}"; }
        finally
        {
            var timer = Stopwatch.StartNew();
            Native.Close(job);
            try
            {
                while ((handles.Any(h => !Native.Exited(h))
                    || (process.Process != IntPtr.Zero && !Native.Exited(process.Process)))
                    && timer.Elapsed.TotalSeconds < 5) Thread.Sleep(10);
                stopped = process.Process != IntPtr.Zero && Native.Exited(process.Process)
                    && handles.All(Native.Exited);
                shutdownMs = timer.Elapsed.TotalMilliseconds;
                if (!stopped) throw new InvalidOperationException("Owned process tree cleanup not verified");
            }
            finally
            {
                foreach (IntPtr handle in handles) Native.Close(handle);
                Native.Close(process.Thread);
                Native.Close(process.Process);
            }
        }
        var descendants = Directory.GetFiles(directory, "payload-*.json").Select(Wire.Read<JsonElement>).ToArray();
        var failures = Directory.GetFiles(directory, "*.failure.json").Select(Wire.Read<JsonElement>).ToArray();
        var images = File.Exists(Path.Combine(directory, "loaded-images.json"))
            ? Wire.Read<JsonElement>(Path.Combine(directory, "loaded-images.json")) : (JsonElement?)null;
        EffectReceipt? effect = receipt is { } confirmed && confirmed.TryGetProperty("Effect", out var effectJson)
            ? effectJson.Deserialize<EffectReceipt>(Wire.Json) : null;
        return new
        {
            StartedUtc = started,
            EndedUtc = DateTimeOffset.UtcNow,
            Pid = process.Pid,
            State = state,
            ExitCode = exit,
            Receipt = receipt,
            Error = error,
            WorkerFailures = failures,
            Descendants = descendants,
            EffectMarker = File.Exists(receiptPath + ".effect") ? File.ReadAllText(receiptPath + ".effect") : null,
            EffectOutcome = Admission.Outcome(expectedRun, checked((int)process.Pid), admission, effect),
            LoadedImages = images,
            OwnedProcesses = ownedProcesses,
            JobPids = pids,
            TreeStopped = stopped,
            ShutdownMilliseconds = shutdownMs
        };
    }

    private sealed record Verification(int Assertions, int Passed, string[] HarnessErrors,
        string Disposition, string[] FailedAdmissionGates)
    {
        public string DefaultGrantPolicy => "Owner-approved best-effort transitive tracking; exact declared script bytes";
        public bool ProductionProfileCertified => false;
    }

    private sealed record OwnedProcess(int Pid, string? Image, DateTimeOffset? StartedUtc, string? IdentityError);

    private static Verification Verify(List<JsonElement> workers, List<JsonElement> scriptTrials, string admission)
    {
        var errors = new List<string>();
        int count = 0;
        void Check(bool value, string message) { count++; if (!value) errors.Add(message); }
        var scripts = scriptTrials[1];
        foreach (var trial in workers.Concat(scriptTrials.Take(2)))
        {
            Check(trial.GetProperty("TreeStopped").GetBoolean(), "Owned tree stopped");
            Check(trial.GetProperty("Error").ValueKind == JsonValueKind.Null, "Complete bounded receipt");
            Check(trial.GetProperty("ExitCode").ValueKind == JsonValueKind.Number
                && trial.GetProperty("ExitCode").GetInt32() == 0, "Worker completed");
            Check(trial.GetProperty("Receipt").ValueKind == JsonValueKind.Object
                && trial.GetProperty("Receipt").GetProperty("Pid").GetInt32()
                == trial.GetProperty("Pid").GetInt32(), "Actual receipt PID");
        }
        if (errors.Count > 0) return new(count, count - errors.Count, errors.ToArray(),
            "Blocked: incomplete trial", ["W2 not proved"]);
        var baseline = workers[0].GetProperty("Receipt");
        var acl = workers[1].GetProperty("Receipt");
        var blocked = workers[2].GetProperty("Receipt");
        foreach (var receipt in new[] { acl, blocked })
        {
            Check(receipt.GetProperty("Token").GetProperty("AppContainer").GetBoolean(), "Actual container token");
            Check(receipt.GetProperty("HostHandleProbe").GetProperty("Outcome").GetString() is "NotInherited" or "Unknown",
                "Handle probe preserves uncertainty (not an enforcement assertion)");
            Check(receipt.GetProperty("AmbientSentinel").ValueKind == JsonValueKind.Null, "No ambient sentinel");
            Check(receipt.GetProperty("AdmissionDigest").GetString() == admission, "Immutable input correlation");
        }
        string Outcome(JsonElement receipt, string name) => receipt.GetProperty("Probes").EnumerateArray()
            .Single(p => p.GetProperty("Name").GetString() == name).GetProperty("Outcome").GetString()!;
        Check(!baseline.GetProperty("Token").GetProperty("AppContainer").GetBoolean(), "Uncontained positive token");
        Check(baseline.GetProperty("Probes").EnumerateArray().All(p =>
            p.GetProperty("Outcome").GetString() == "Allowed"), "All baseline dependency operations work");
        Check(Outcome(acl, "declared.child") == "Allowed", "Declared child positive control");
        Check(Outcome(acl, "undeclared.rx.child") == "Allowed", "Undeclared RX child falsifies exact admission");
        Check(Outcome(acl, "denied.child") == "Denied", "ACL child native denial");
        Check(Outcome(acl, "denied.managed.bytes") == "Denied", "ACL module native denial");
        Check(Outcome(acl, "writable.noexecute.child") == "Denied", "Data executable denial");
        Check(Outcome(acl, "writable.noexecute.managed.bytes") == "Allowed", "Byte-loading bypass observed");
        Check(Outcome(blocked, "declared.child") == "Denied", "No-child policy also denies declared helper");
        Check(Outcome(blocked, "writable.noexecute.managed.bytes") == "Allowed", "No-child is not module admission");
        Check(workers[1].GetProperty("Descendants").GetArrayLength() == 4,
            "Declared and undeclared child/grandchild controls");
        Check(workers[1].GetProperty("Descendants").EnumerateArray().All(d =>
            d.GetProperty("Token").GetProperty("AppContainer").GetBoolean()
            && d.GetProperty("EnvironmentSentinel").ValueKind == JsonValueKind.Null),
            "Descendant token/environment inherited safely");
        var ps = scripts.GetProperty("Receipt");
        Check(scriptTrials[0].GetProperty("Receipt").GetProperty("Probes").EnumerateArray().All(p =>
            p.GetProperty("Outcome").GetString() == "Allowed"), "All script baseline operations work");
        Check(ps.GetProperty("Token").GetProperty("AppContainer").GetBoolean(), "Interpreter actual container token");
        var effect = ps.GetProperty("Effect").Deserialize<EffectReceipt>(Wire.Json);
        Check(Admission.Outcome(ps.GetProperty("RunId").GetString()!, ps.GetProperty("Pid").GetInt32(), admission, effect) == "Observed",
            "Fixed in-memory multi-script typed value 102 receipt");
        Check(scripts.GetProperty("EffectMarker").GetString() == "102", "Actual fixed owned marker observed");
        Check(Outcome(ps, "writable.script") == "Allowed", "Writable script bypass");
        Check(Outcome(ps, "writable.module.explicit") == "Allowed", "Writable module import bypass");
        Check(Outcome(ps, "writable.module.autoload") == "Allowed", "Writable module autoload bypass");
        Check(Outcome(ps, "writable.managed.bytes") == "Allowed", "Interpreter byte-loading bypass");
        Check(Outcome(ps, "denied.script.read") == "Denied", "Actual script source read native denial");
        foreach (var uncertain in scriptTrials.Skip(2))
        {
            Check(uncertain.GetProperty("EffectMarker").GetString() == "102", "Effect precedes receipt loss/cancellation");
            Check(uncertain.GetProperty("Receipt").ValueKind == JsonValueKind.Null
                && uncertain.GetProperty("Error").ValueKind == JsonValueKind.String,
                "Unverifiable receipt stays explicitly Unknown, no replay");
            Check(uncertain.GetProperty("EffectOutcome").GetString() == "Unknown",
                "Unknown is independent of lifecycle/exit");
            Check(uncertain.GetProperty("TreeStopped").GetBoolean(), "Unknown effect tree stopped");
        }
        Check(scriptTrials[4].GetProperty("State").GetString() == "Cancelled",
            "Cancellation lifecycle independent of Unknown effect");
        var gates = new List<string>
        {
            "Undeclared readable managed/script/module code executes", "RX undeclared children/grandchildren execute",
            "No-child policy denies declared children too",
            "Handle probe may be Unknown; bInheritHandles=false is inspected, not object-identity proof",
            "No W1 network or W3 installed protection proof",
            "Fixed-control admission still blocked; no broker selected",
        };
        bool nativeRun = acl.GetProperty("Probes").EnumerateArray()
            .Any(probe => probe.GetProperty("Name").GetString() == "declared.native");
        if (!nativeRun) gates.Add("Native load trial blocked: reviewed compiled fixture unavailable");
        else
        {
            Check(Outcome(baseline, "declared.native") == "Allowed", "Native declared positive control");
            Check(Outcome(baseline, "denied.native") == "Allowed", "Native denied-fixture positive control");
            Check(Outcome(acl, "declared.native") == "Allowed", "Native declared AppContainer load");
            Check(Outcome(acl, "denied.native") == "Denied", "Native ACL denied load");
        }
        return new(count, count - errors.Count, errors.ToArray(),
            "Rejected: ACL/no-child exact dependency profiles", gates.ToArray());
    }

    private static InputFile Resource(string name, string suffix)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("W2Proof." + suffix)
            ?? throw new InvalidDataException("Embedded fixture resource missing: " + suffix);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new(name, buffer.ToArray());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static object[] Inventory(string directory, string relativeRoot) => Directory.GetFiles(directory, "*",
        SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => (object)new
        { Name = Path.GetRelativePath(relativeRoot, path), Length = new FileInfo(path).Length, Sha256 = Hash(path) }).ToArray();

    private static Dictionary<string, string> EnvironmentBlock(string directory) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["TEMP"] = directory,
        ["TMP"] = directory,
        ["USERPROFILE"] = directory,
        ["HOME"] = directory,
        ["LOCALAPPDATA"] = directory,
        ["APPDATA"] = directory,
        ["PSModulePath"] = "",
        ["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        ["DOTNET_EnableDiagnostics"] = "0",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
    };

    private static void Acl(string path, string host, string? worker, FileSystemRights rights,
        bool denyFileExecute = false)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(host), FileSystemRights.FullControl,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        if (worker is not null)
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(worker), rights,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
            if (denyFileExecute)
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(worker),
                    FileSystemRights.ExecuteFile, InheritanceFlags.ObjectInherit,
                    PropagationFlags.InheritOnly, AccessControlType.Deny));
        }
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void DataFileAcl(string path, string host, string worker)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(host),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(worker),
            FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(worker),
            FileSystemRights.ExecuteFile, AccessControlType.Deny));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void CopyTree(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Runtime reparse point not admitted");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Runtime file reparse point not admitted");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string child in Directory.GetDirectories(source))
            CopyTree(child, Path.Combine(destination, Path.GetFileName(child)));
    }
}