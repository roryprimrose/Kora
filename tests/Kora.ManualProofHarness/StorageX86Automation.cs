using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kora.ManualProofHarness;

internal static class StorageX86Automation
{
    private const string ExpectedAssetSha256 = "2B7AD2DA9CA246E1D68BC0762D8AC3AA9BA98F4D071480546B3EBE23D6C6AB21";
    private static readonly JsonSerializerOptions ReceiptJson = new() { WriteIndented = true };

    internal static async Task<string?> RunAsync(ProofAttempt attempt, Action persist, TextWriter output)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var evidenceRoot = Path.Combine(localData, "Kora", "ManualProofHarness", "evidence");
        var sourceDirectory = Directory.EnumerateDirectories(evidenceRoot, "STORAGE-ASSETS-*")
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (sourceDirectory is null)
            return "No retained STORAGE-ASSETS evidence directory was found.";

        var sourceReceipt = Path.Combine(sourceDirectory, "receipt.json");
        var nativePath = Path.Combine(sourceDirectory, "win-x86", "e_sqlcipher.dll");
        if (!File.Exists(sourceReceipt) || !File.Exists(nativePath))
            return "The retained x86 SQLCipher receipt or native asset is missing.";

        using (var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(sourceReceipt)))
        {
            var root = receipt.RootElement;
            if (!string.Equals(
                    root.GetProperty("schema").GetString(),
                    "Kora.ManualProofHarness.StorageAssets.v1",
                    StringComparison.Ordinal))
                return "The retained storage asset receipt schema is unsupported.";
            var x86 = root.GetProperty("assets").EnumerateArray().SingleOrDefault(asset =>
                string.Equals(asset.GetProperty("rid").GetString(), "win-x86", StringComparison.Ordinal));
            if (x86.ValueKind == JsonValueKind.Undefined
                || !string.Equals(x86.GetProperty("sha256").GetString(), ExpectedAssetSha256, StringComparison.Ordinal)
                || !string.Equals(x86.GetProperty("machine").GetString(), "0x014C", StringComparison.Ordinal))
                return "The retained storage asset receipt does not identify the expected x86 SQLCipher binary.";
        }

        var assetHash = await ComputeSha256Async(nativePath);
        if (!string.Equals(assetHash, ExpectedAssetSha256, StringComparison.Ordinal))
            return "The retained x86 SQLCipher binary no longer matches its recorded SHA-256.";
        if (ReadMachine(nativePath) != 0x014c)
            return "The retained SQLCipher binary is not an x86 PE image.";

        var x86PowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SysWOW64",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(x86PowerShell))
            return "The installed 32-bit Windows PowerShell host was not found.";

        var outputDirectory = Path.Combine(evidenceRoot, $"STX86-{attempt.AttemptId:N}");
        Directory.CreateDirectory(outputDirectory);
        var scriptPath = Path.Combine(outputDirectory, "probe.ps1");
        var receiptPath = Path.Combine(outputDirectory, "receipt.json");
        await File.WriteAllTextAsync(scriptPath, CreateProbeScript(nativePath));

        output.WriteLine("Executing the exact retained SQLCipher asset in 32-bit Windows PowerShell...");
        var start = new ProcessStartInfo(x86PowerShell)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(nativePath)!,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the installed 32-bit Windows PowerShell host.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var probeOutput = await standardOutput;
        var probeError = await standardError;
        if (process.ExitCode != 0)
            return $"The x86 native-load probe exited {process.ExitCode}: {probeError.Trim()}";

        using var probe = JsonDocument.Parse(probeOutput);
        var result = probe.RootElement;
        var loadedPath = result.GetProperty("loadedModulePath").GetString();
        if (result.GetProperty("pointerSize").GetInt32() != 4
            || result.GetProperty("is64BitProcess").GetBoolean()
            || result.GetProperty("openResult").GetInt32() != 0
            || result.GetProperty("keyResult").GetInt32() != 0
            || result.GetProperty("execResult").GetInt32() != 0
            || result.GetProperty("closeResult").GetInt32() != 0
            || !string.Equals(
                Path.GetFullPath(loadedPath ?? string.Empty),
                Path.GetFullPath(nativePath),
                StringComparison.OrdinalIgnoreCase))
            return "The x86 SQLCipher process did not establish the expected architecture, module identity and successful calls.";

        var finalHash = await ComputeSha256Async(nativePath);
        if (!string.Equals(finalHash, assetHash, StringComparison.Ordinal))
            return "The retained x86 SQLCipher binary changed during execution.";

        var receiptObject = new
        {
            schema = "Kora.ManualProofHarness.StorageX86Load.v1",
            recordedUtc = DateTimeOffset.UtcNow,
            sourceReceipt,
            nativePath,
            nativeSha256Before = assetHash,
            nativeSha256After = finalHash,
            nativeMachine = "0x014C",
            hostPath = x86PowerShell,
            hostSha256 = await ComputeSha256Async(x86PowerShell),
            probe = JsonSerializer.Deserialize<JsonElement>(probeOutput),
            checks = new
            {
                x86Process = true,
                exactModuleLoaded = true,
                sqliteOpened = true,
                sqlCipherKeyAccepted = true,
                schemaStatementExecuted = true,
                sqliteClosed = true,
                sourceAssetUnchanged = true,
            },
            limitation = "This proves exact x86 native loading and basic SQLCipher execution on this machine, not full Kora x86 startup, installer behavior, clean-machine dependencies, production admission or package provenance.",
        };
        var temporary = receiptPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(receiptObject, ReceiptJson));
        File.Move(temporary, receiptPath);

        attempt.AutomatedEvidence["stx86.sourceReceipt"] = sourceReceipt;
        attempt.AutomatedEvidence["stx86.nativePath"] = nativePath;
        attempt.AutomatedEvidence["stx86.nativeSha256"] = assetHash;
        attempt.AutomatedEvidence["stx86.receiptPath"] = receiptPath;
        attempt.AutomatedEvidence["stx86.sqliteVersion"] =
            result.GetProperty("sqliteVersion").GetString() ?? "unknown";
        persist();
        output.WriteLine(
            $"Verified x86 SQLCipher {attempt.AutomatedEvidence["stx86.sqliteVersion"]} in PID {result.GetProperty("processId").GetInt32()}; receipt: {receiptPath}");
        return null;
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static ushort ReadMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException("The native asset has no DOS header.");
        stream.Position = 0x3c;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 6)
            throw new InvalidDataException("The native asset has an invalid PE offset.");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("The native asset has no PE signature.");
        return reader.ReadUInt16();
    }

    private static string CreateProbeScript(string nativePath)
    {
        var escapedPath = nativePath.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $$"""
            $ErrorActionPreference = 'Stop'
            $source = @'
            using System;
            using System.Runtime.InteropServices;

            public static class SqlCipherProbe
            {
                [DllImport(@"{{escapedPath}}", CallingConvention = CallingConvention.Cdecl)]
                public static extern IntPtr sqlite3_libversion();

                [DllImport(@"{{escapedPath}}", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
                public static extern int sqlite3_open(string filename, out IntPtr database);

                [DllImport(@"{{escapedPath}}", CallingConvention = CallingConvention.Cdecl)]
                public static extern int sqlite3_key(IntPtr database, byte[] key, int keyLength);

                [DllImport(@"{{escapedPath}}", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
                public static extern int sqlite3_exec(
                    IntPtr database,
                    string sql,
                    IntPtr callback,
                    IntPtr callbackArgument,
                    out IntPtr errorMessage);

                [DllImport(@"{{escapedPath}}", CallingConvention = CallingConvention.Cdecl)]
                public static extern int sqlite3_close(IntPtr database);
            }
            '@
            Add-Type -TypeDefinition $source -Language CSharp
            $database = [IntPtr]::Zero
            $openResult = [SqlCipherProbe]::sqlite3_open(':memory:', [ref]$database)
            $key = [Text.Encoding]::UTF8.GetBytes('Kora-STX86-probe-key')
            $keyResult = [SqlCipherProbe]::sqlite3_key($database, $key, $key.Length)
            $errorMessage = [IntPtr]::Zero
            $execResult = [SqlCipherProbe]::sqlite3_exec(
                $database,
                'CREATE TABLE proof(value TEXT NOT NULL); INSERT INTO proof VALUES (''verified'');',
                [IntPtr]::Zero,
                [IntPtr]::Zero,
                [ref]$errorMessage)
            $version = [Runtime.InteropServices.Marshal]::PtrToStringAnsi([SqlCipherProbe]::sqlite3_libversion())
            $module = [Diagnostics.Process]::GetCurrentProcess().Modules |
                Where-Object { $_.ModuleName -eq 'e_sqlcipher.dll' } |
                Select-Object -First 1
            $closeResult = [SqlCipherProbe]::sqlite3_close($database)
            [pscustomobject]@{
                processId = $PID
                pointerSize = [IntPtr]::Size
                is64BitProcess = [Environment]::Is64BitProcess
                sqliteVersion = $version
                openResult = $openResult
                keyResult = $keyResult
                execResult = $execResult
                closeResult = $closeResult
                loadedModulePath = $module.FileName
            } | ConvertTo-Json -Compress
            """;
    }
}
