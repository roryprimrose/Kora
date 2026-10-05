using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace StorageProof;

internal static class Program
{
    private const string Marker = "R02_SYNTHETIC_NEEDLE_7D3490B4";
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(Marker + " needle " + new string('x', 4096));
    private static readonly List<object> Checks = [];
    private static readonly List<object> Measurements = [];
    private static readonly HashSet<string> ObservedFiles = [];
    private static string root = "";
    private static string project = "";

    public static int Main(string[] args)
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            project = Path.GetFullPath(Environment.CurrentDirectory);
            if (!File.Exists(Path.Combine(project, "StorageProof.csproj")))
                throw new InvalidOperationException("Run from experiments\\r02-storage-proof.");
            if (args.Length > 0 && args[0] == "--crash-child") return CrashChild(args);
            if (args.Length == 3 && args[0] == "--verify-cross-user") return VerifyCrossUser(args[1], args[2]);
            if (args.Length > 0 && (args.Length != 1 || args[0] != "--prepare-cross-user"))
                throw new ArgumentException("Unknown proof command.");
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Full proof requires actual Windows DPAPI and ACL evidence.");

            root = Path.Combine(project, ".scratch", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            RestrictDirectory(root);
            File.WriteAllText(Path.Combine(root, "proof-owned"), "synthetic-only");
            Environment.SetEnvironmentVariable("TEMP", root);
            Environment.SetEnvironmentVariable("TMP", root);
            var key = RandomNumberGenerator.GetBytes(32);
            try
            {
                if (args.Length == 1 && args[0] == "--prepare-cross-user")
                {
                    PrepareCrossUser(key);
                    return 0;
                }
                Check("owner-only scratch ACL", OwnerOnlyAcl(root));
                KeyTests(key);
                EnvelopeTests(key);
                foreach (var cipher in new[] { false, true })
                {
                    StorageTests(key, cipher);
                    foreach (var phase in new[] { "uncommitted", "committed", "intent", "journal" })
                        CrashTests(key, cipher, phase);
                    foreach (var phase in new[] { "artifact-stage", "artifact-publish", "artifact-reference" })
                        CrashTests(key, cipher, phase);
                    Performance(key, cipher);
                }
                LegacyConversion(key);
                PageRekey(key);
                ArtifactTests(key);
                Check("actual cross-user protection evidence", false, "BLOCKED: no approved second-account trial; not simulated", blocked: true);
                WriteReport();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
            // Delete only the just-created, owned run, never a caller-supplied tree.
            Directory.Delete(root, recursive: true);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PROOF FAILED: {exception.GetType().Name} ({exception.HResult})");
            Console.Error.WriteLine(exception.StackTrace);
            if (root.Length > 0)
            {
                Console.Error.WriteLine("Synthetic scratch retained for investigation.");
                WriteReport();
            }
            return 1;
        }
    }

    private static void Check(string name, bool passed, string? detail = null, bool blocked = false)
    {
        Checks.Add(new { name, status = blocked ? "blocked" : passed ? "passed" : "failed", detail });
        Console.WriteLine($"{(blocked ? "BLOCKED" : passed ? "PASS" : "FAIL")} {name}");
        if (root.Length > 0)
            File.AppendAllText(Path.Combine(root, "diagnostics.jsonl"),
                JsonSerializer.Serialize(new { name, passed, blocked }) + Environment.NewLine);
        if (!passed && !blocked) throw new InvalidDataException("Proof assertion failed: " + name);
    }

    private static void Rejects<T>(string name, Action action) where T : Exception
    {
        try { action(); }
        catch (T) { Check(name, true); return; }
        Check(name, false);
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var security = new DirectorySecurity();
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No Windows SID.");
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static bool OwnerOnlyAcl(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var sid = WindowsIdentity.GetCurrent().User;
        var acl = new DirectoryInfo(path).GetAccessControl();
        if (!acl.AreAccessRulesProtected) return false;
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier));
        if (rules.Count != 1) return false;
        foreach (FileSystemAccessRule rule in rules)
            if (!rule.IdentityReference.Equals(sid) || rule.AccessControlType != AccessControlType.Allow) return false;
        return true;
    }

    private static void KeyTests(byte[] key)
    {
        var path = Path.Combine(root, "key.dpapi");
        File.WriteAllBytes(path, WindowsKey.Wrap(key));
        var copiedWrapper = File.ReadAllBytes(path);
        var recovered = WindowsKey.Unwrap(File.ReadAllBytes(path));
        Check("DPAPI CurrentUser persisted roundtrip", recovered.SequenceEqual(key));
        CryptographicOperations.ZeroMemory(recovered);
        var wrapped = File.ReadAllBytes(path);
        Check("wrapped key does not contain raw key", !Contains(wrapped, key));
        wrapped[wrapped.Length / 2] ^= 1;
        Rejects<CryptographicException>("DPAPI blob tampering rejected", () => WindowsKey.Unwrap(wrapped));
        Rejects<FileNotFoundException>("missing key fails without replacement", () => File.ReadAllBytes(path + ".missing"));
        Check("key loss did not create replacement", !File.Exists(path + ".missing"));
        var replacement = RandomNumberGenerator.GetBytes(32);
        try
        {
            var sealedValue = Envelope.Seal(key, Payload, "rotation");
            Rejects<CryptographicException>("new key cannot read old ciphertext", () => Envelope.Open(replacement, sealedValue, "rotation"));
            var rewritten = Envelope.Seal(replacement, Envelope.Open(key, sealedValue, "rotation"), "rotation");
            Check("explicit re-encryption survives rotation", Envelope.Open(replacement, rewritten, "rotation").SequenceEqual(Payload));
            File.Delete(path);
            var copiedKey = WindowsKey.Unwrap(copiedWrapper);
            Check("deleting current wrapper does not revoke copied wrapper", copiedKey.SequenceEqual(key));
            CryptographicOperations.ZeroMemory(copiedKey);
        }
        finally { CryptographicOperations.ZeroMemory(replacement); }
    }

    private static void EnvelopeTests(byte[] key)
    {
        var sealedValue = Envelope.Seal(key, Payload, "event:1");
        Check("AES-GCM roundtrip", Envelope.Open(key, sealedValue, "event:1").SequenceEqual(Payload));
        Check("random nonces change ciphertext", !sealedValue.SequenceEqual(Envelope.Seal(key, Payload, "event:1")));
        foreach (var (name, offset) in new[] { ("version", 0), ("nonce", 1), ("tag", 13), ("ciphertext", 29) })
        {
            var bad = (byte[])sealedValue.Clone();
            bad[offset] ^= 1;
            Rejects<CryptographicException>("AES-GCM " + name + " tamper rejected", () => Envelope.Open(key, bad, "event:1"));
        }
        Rejects<CryptographicException>("AES-GCM truncated envelope rejected", () => Envelope.Open(key, sealedValue[..20], "event:1"));
        Rejects<CryptographicException>("AES-GCM row substitution rejected", () => Envelope.Open(key, sealedValue, "event:2"));
        Rejects<CryptographicException>("AES-GCM wrong key rejected", () => Envelope.Open(new byte[32], sealedValue, "event:1"));
    }

    private static void StorageTests(byte[] key, bool cipher)
    {
        var label = cipher ? "sqlcipher" : "envelopes";
        var path = Path.Combine(root, label + ".db");
        var backup = Path.Combine(root, label + ".backup");
        using (var store = new ScratchStore(path, key, cipher))
        {
            if (cipher)
            {
                Measurements.Add(new
                {
                    engine = store.Scalar("SELECT sqlite_version()"),
                    sqlcipher = store.Scalar("PRAGMA cipher_version"),
                    cipherProvider = store.Scalar("PRAGMA cipher_provider"),
                    cipherProviderVersion = store.Scalar("PRAGMA cipher_provider_version"),
                    cipherPageSize = store.Scalar("PRAGMA cipher_page_size"),
                    kdfIterations = store.Scalar("PRAGMA kdf_iter"),
                    cipherHmac = store.Scalar("PRAGMA cipher_use_hmac"),
                    tempStore = store.Scalar("PRAGMA temp_store"),
                    tempStoreCompileOption = store.Scalar("SELECT compile_options FROM pragma_compile_options WHERE compile_options LIKE 'TEMP_STORE=%'"),
                });
            }
            using (var transaction = store.Connection.BeginTransaction())
            {
                for (var i = 1; i <= 40; i++) store.Append(i, Payload, transaction);
                transaction.Commit();
            }
            Check(label + " readback", store.Read(1).SequenceEqual(Payload));
            Check(label + " SQLite temporary store is memory-only", Convert.ToInt64(store.Scalar("PRAGMA temp_store")) == 2);
            store.Execute("CREATE TEMP TABLE temporary_fixture AS SELECT payload FROM samples;");
            Check(label + " content-bearing temp table exercised", Convert.ToInt64(store.Scalar("SELECT count(*) FROM temporary_fixture")) == 40);
            Check(label + " persistent WAL observed", File.Exists(path + "-wal") && new FileInfo(path + "-wal").Length > 0);
            Scan(label + " live WAL/index", allowMarker: false);

            using (var transaction = store.Connection.BeginTransaction())
            {
                store.Execute("CREATE TABLE migration_probe(value TEXT); DELETE FROM samples WHERE id=2; PRAGMA user_version=2;", transaction);
                Rejects<SqliteException>(label + " injected migration failure", () =>
                    store.Execute("INSERT INTO samples SELECT * FROM samples WHERE id=1;", transaction));
                transaction.Rollback();
            }
            Check(label + " migration preserves version and rows",
                Convert.ToInt64(store.Scalar("PRAGMA user_version")) == 1 && store.Count() == 40 &&
                Convert.ToInt64(store.Scalar("SELECT count(*) FROM sqlite_master WHERE name='migration_probe'")) == 0 &&
                store.Read(2).SequenceEqual(Payload));
            store.Backup(backup);
            using (var saved = new ScratchStore(backup, key, cipher, initialize: false))
                Check(label + " explicitly keyed backup readable", saved.Count() == 40 && saved.Read(1).SequenceEqual(Payload));
            var pageLimit = store.Scalar("PRAGMA page_count");
            store.Execute("PRAGMA max_page_count=" + pageLimit);
            Rejects<SqliteException>(label + " storage capacity failure is explicit", () => store.Append(999, new byte[1024 * 1024]));
            Check(label + " capacity failure preserves existing rows", store.Count() == 40 && store.Read(1).SequenceEqual(Payload));
            store.Execute("PRAGMA max_page_count=1073741823;");

            // A positive control demonstrates an unsafe content-bearing index on an unkeyed database.
            if (!cipher)
            {
                store.Execute("CREATE VIRTUAL TABLE unsafe_search USING fts5(content);");
                using var command = store.Connection.CreateCommand();
                command.CommandText = "INSERT INTO unsafe_search VALUES($content)";
                command.Parameters.AddWithValue("$content", Marker);
                command.ExecuteNonQuery();
                Check("plaintext FTS positive-control leaks in WAL", Contains(ReadObservedBytes(path + "-wal"), Encoding.UTF8.GetBytes(Marker)));
                store.Execute("DROP TABLE unsafe_search; PRAGMA secure_delete=ON; PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
                store.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            }
            Scan(label + " backup/journal/temp", allowMarker: false);
            store.Execute("DELETE FROM samples WHERE id=1;");
            if (cipher) store.Execute("DELETE FROM search WHERE rowid=1;");
            Check(label + " targeted deletion preserves unrelated records",
                store.Count() == 39 && store.Read(2).SequenceEqual(Payload) &&
                Convert.ToInt64(store.Scalar("SELECT count(*) FROM samples WHERE id=1")) == 0);
            store.Execute("PRAGMA secure_delete=ON; DELETE FROM samples;");
            if (cipher) store.Execute("DELETE FROM search; INSERT INTO search(search) VALUES('optimize');");
            store.Execute("PRAGMA wal_checkpoint(TRUNCATE); VACUUM; PRAGMA wal_checkpoint(TRUNCATE);");
            Check(label + " logical deletion and index cleanup", store.Count() == 0);
            if (cipher) Check("SQLCipher deleted FTS entries absent", Convert.ToInt64(store.Scalar("SELECT count(*) FROM search WHERE search MATCH 'needle'")) == 0);
            using (var saved = new ScratchStore(backup, key, cipher, initialize: false))
                Check(label + " backup still recovers deleted content (limitation)", saved.Read(1).SequenceEqual(Payload));
        }
        if (cipher)
        {
            var untouchedHash = SHA256.HashData(File.ReadAllBytes(backup));
            Rejects<SqliteException>("SQLCipher wrong key rejected without replacement", () =>
            {
                using var wrong = new ScratchStore(backup, new byte[32], true, initialize: false);
                wrong.Count();
            });
            Check("wrong key preserves existing file", untouchedHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backup))));
            var damaged = Path.Combine(root, "tampered.db");
            var bytes = File.ReadAllBytes(backup);
            bytes[1024] ^= 1;
            File.WriteAllBytes(damaged, bytes);
            Rejects<SqliteException>("SQLCipher authenticated page tampering rejected", () =>
            {
                using var bad = new ScratchStore(damaged, key, true, initialize: false);
                bad.Count();
            });
        }
        // Explicit future-version refusal, not a final application migration framework.
        using (var store = new ScratchStore(path, key, cipher, initialize: false))
            store.Execute("PRAGMA user_version=99;");
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        Rejects<InvalidDataException>(label + " future schema refused", () =>
        {
            using var store = new ScratchStore(path, key, cipher, initialize: false);
            if (Convert.ToInt64(store.Scalar("PRAGMA user_version")) > 1)
                throw new InvalidDataException("Unsupported prototype version.");
        });
        Check(label + " future schema refusal preserves file", hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))));
        Scan(label + " closed storage", allowMarker: false);
    }

    private static int CrashChild(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Invalid child invocation.");
        root = Path.GetFullPath(args[1]);
        var scratch = Path.Combine(project, ".scratch") + Path.DirectorySeparatorChar;
        if (!root.StartsWith(scratch, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(root, "proof-owned")) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Child requires an owned synthetic run.");
        var cipher = bool.Parse(args[2]);
        var phase = args[3];
        if (phase is not ("uncommitted" or "committed" or "intent" or "journal" or
            "artifact-stage" or "artifact-publish" or "artifact-reference") ||
            args[4] != Path.GetFileName(args[4]))
            throw new ArgumentException("Invalid child phase or database name.");
        var key = WindowsKey.Unwrap(File.ReadAllBytes(Path.Combine(root, "child-key.dpapi")));
        using var store = new ScratchStore(Path.Combine(root, args[4]), key, cipher, initialize: false);
        if (phase == "journal") store.Execute("PRAGMA journal_mode=DELETE; PRAGMA cache_size=5;");
        if (phase.StartsWith("artifact-", StringComparison.Ordinal))
        {
            var stage = Path.Combine(root, args[4] + ".stage");
            using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(Envelope.Seal(key, Payload, "artifact:" + args[4]));
                file.Flush(flushToDisk: true);
            }
            if (phase is "artifact-publish" or "artifact-reference")
                File.Move(stage, Path.Combine(root, args[4] + ".blob"));
            if (phase == "artifact-reference") store.Append(2, Encoding.UTF8.GetBytes(args[4] + ".blob"));
            File.WriteAllText(Path.Combine(root, "child-ready"), phase);
            Thread.Sleep(Timeout.Infinite);
        }
        else
        {
            using var transaction = store.Connection.BeginTransaction();
            for (var i = 2; i <= 80; i++) store.Append(i, Payload, transaction);
            if (phase is "committed" or "intent") transaction.Commit();
            File.WriteAllText(Path.Combine(root, "child-ready"), phase);
            Thread.Sleep(Timeout.Infinite);
        }
        return 1;
    }

    private static void CrashTests(byte[] key, bool cipher, string phase)
    {
        var label = (cipher ? "sqlcipher" : "envelopes") + "-" + phase;
        var name = label + ".db";
        var path = Path.Combine(root, name);
        using (var initial = new ScratchStore(path, key, cipher)) initial.Append(1, Payload);
        File.WriteAllBytes(Path.Combine(root, "child-key.dpapi"), WindowsKey.Wrap(key));
        var ready = Path.Combine(root, "child-ready");
        File.Delete(ready);
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("No process path."))
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = project,
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in new[] { "--crash-child", root, cipher.ToString(), phase, name }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Child did not start.");
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (process.HasExited) throw new InvalidOperationException("Crash child exited before readiness: " + process.StandardError.ReadToEnd());
                if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Crash child not ready.");
                Thread.Sleep(25);
            }
            Check(label + " actual live child at interruption", !process.HasExited);
            if (phase == "journal")
                Check(label + " rollback journal observed", File.Exists(path + "-journal") && new FileInfo(path + "-journal").Length > 0);
            Scan(label + " interrupted files", allowMarker: false);
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        }
        using var recovered = new ScratchStore(path, key, cipher, initialize: false);
        Check(label + " crash integrity", (string)recovered.Scalar("PRAGMA integrity_check") == "ok");
        var expected = phase is "committed" or "intent" ? 80 : phase == "artifact-reference" ? 2 : 1;
        Check(label + " atomic recovery", recovered.Count() == expected && recovered.Read(1).SequenceEqual(Payload));
        if (phase.StartsWith("artifact-", StringComparison.Ordinal))
        {
            var file = path + (phase == "artifact-stage" ? ".stage" : ".blob");
            Check(label + " recovered staged/orphan/referenced artifact authenticates",
                Envelope.Open(key, File.ReadAllBytes(file), "artifact:" + name).SequenceEqual(Payload));
            Check(label + " no dangling committed reference",
                phase != "artifact-reference" || (File.Exists(path + ".blob") &&
                    Encoding.UTF8.GetString(recovered.Read(2)) == name + ".blob"));
            File.Delete(file);
        }
        if (phase == "intent")
        {
            // No executable behavior exists in this proof. Persisted intent without a receipt is unknown.
            Check(label + " durable intent without receipt classified unknown", recovered.Count() == 80 &&
                Convert.ToInt64(recovered.Scalar("SELECT count(*) FROM sqlite_master WHERE name='receipt'")) == 0);
        }
        File.Delete(ready);
        File.Delete(Path.Combine(root, "child-key.dpapi"));
    }

    private static void ArtifactTests(byte[] key)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Payload));
        var stage = Path.Combine(root, digest + ".stage");
        var artifact = Path.Combine(root, digest + ".blob");
        var encrypted = Envelope.Seal(key, Payload, "artifact:" + digest);
        using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.Write(encrypted);
            file.Flush(flushToDisk: true);
        }
        Scan("artifact staging/temp", allowMarker: false);
        File.Move(stage, artifact);
        Check("artifact published before reference is recoverable orphan", File.Exists(artifact) && !File.Exists(stage));
        Check("artifact authenticated digest roundtrip", Envelope.Open(key, File.ReadAllBytes(artifact), "artifact:" + digest).SequenceEqual(Payload));
        var corrupt = File.ReadAllBytes(artifact);
        corrupt[^1] ^= 1;
        Rejects<CryptographicException>("artifact tampering rejected", () => Envelope.Open(key, corrupt, "artifact:" + digest));
        Rejects<CryptographicException>("artifact identity substitution rejected", () => Envelope.Open(key, encrypted, "artifact:other"));
        var snapshot = Path.Combine(root, "artifact.backup");
        File.Copy(artifact, snapshot);
        File.Delete(artifact);
        Check("artifact unlink leaves backup readable (limitation)", Envelope.Open(key, File.ReadAllBytes(snapshot), "artifact:" + digest).SequenceEqual(Payload));
        File.Delete(snapshot);
        Check("managed artifact/current backup logical cleanup", !File.Exists(artifact) && !File.Exists(snapshot));
    }

    private static void LegacyConversion(byte[] key)
    {
        var source = Path.Combine(root, "synthetic-legacy.db");
        var candidate = Path.Combine(root, "synthetic-converted.db");
        using (var legacy = new ScratchStore(source, key, false))
        {
            legacy.Execute("CREATE TABLE legacy_fixture(value TEXT NOT NULL);");
            using var command = legacy.Connection.CreateCommand();
            command.CommandText = "INSERT INTO legacy_fixture VALUES($value)";
            command.Parameters.AddWithValue("$value", Marker);
            command.ExecuteNonQuery();
            legacy.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        var hash = SHA256.HashData(File.ReadAllBytes(source));
        Rejects<SqliteException>("keying plaintext database is not a migration", () =>
        {
            using var wrong = new ScratchStore(source, key, true, initialize: false);
            wrong.Count();
        });
        using (var target = new ScratchStore(candidate, key, true))
        {
            using var transaction = target.Connection.BeginTransaction();
            target.Append(1, Payload, transaction);
            Rejects<SqliteException>("failed conversion preserves legacy source", () => target.Append(1, Payload, transaction));
            transaction.Rollback();
            Check("failed conversion target has no partial content", target.Count() == 0);
        }
        Check("legacy source byte-preserved after failed conversion", hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))));
        using (var legacy = new ScratchStore(source, key, false, initialize: false))
        using (var target = new ScratchStore(candidate, key, true, initialize: false))
        {
            var value = Encoding.UTF8.GetBytes((string)legacy.Scalar("SELECT value FROM legacy_fixture"));
            using var transaction = target.Connection.BeginTransaction();
            target.Append(1, value, transaction);
            transaction.Commit();
            Check("copy migration verifies encrypted candidate before replacement", target.Read(1).SequenceEqual(value));
        }
        Check("verified conversion still leaves legacy plaintext (limitation)",
            Contains(File.ReadAllBytes(source), Encoding.UTF8.GetBytes(Marker)));
        File.Delete(source);
        Scan("conversion candidate after managed legacy unlink", allowMarker: false);
    }

    private static void PageRekey(byte[] key)
    {
        var path = Path.Combine(root, "page-rekey.db");
        var backup = Path.Combine(root, "page-rekey.backup");
        var replacement = RandomNumberGenerator.GetBytes(32);
        try
        {
            using (var store = new ScratchStore(path, key, true))
            {
                store.Append(1, Payload);
                store.Backup(backup);
                // Only generated hexadecimal is concatenated; never user input.
                store.Execute("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA rekey='" + Convert.ToHexString(replacement) + "';");
            }
            using (var store = new ScratchStore(path, replacement, true, initialize: false))
                Check("SQLCipher explicit rekey readable with new key", store.Read(1).SequenceEqual(Payload));
            Rejects<SqliteException>("SQLCipher old key rejected after rekey", () =>
            {
                using var store = new ScratchStore(path, key, true, initialize: false);
                store.Count();
            });
            using (var store = new ScratchStore(backup, key, true, initialize: false))
                Check("SQLCipher rekey does not rotate independent backup (limitation)", store.Read(1).SequenceEqual(Payload));
        }
        finally { CryptographicOperations.ZeroMemory(replacement); }
    }

    private static void Performance(byte[] key, bool cipher)
    {
        const int count = 2000;
        var path = Path.Combine(root, (cipher ? "cipher" : "envelope") + "-perf.db");
        var open = Stopwatch.StartNew();
        using var store = new ScratchStore(path, key, cipher);
        open.Stop();
        var watch = Stopwatch.StartNew();
        using (var transaction = store.Connection.BeginTransaction())
        {
            for (var i = 1; i <= count; i++) store.Append(i, Payload, transaction);
            transaction.Commit();
        }
        watch.Stop();
        var writeMs = watch.Elapsed.TotalMilliseconds;
        var commits = new List<double>();
        for (var i = count + 1; i <= count + 100; i++)
        {
            watch.Restart();
            store.Append(i, Payload);
            watch.Stop();
            commits.Add(watch.Elapsed.TotalMilliseconds);
        }
        var reads = new List<double>();
        for (var i = 1; i <= count; i++)
        {
            watch.Restart();
            var value = store.Read(i);
            watch.Stop();
            if (!value.SequenceEqual(Payload)) throw new InvalidDataException("Benchmark content mismatch.");
            reads.Add(watch.Elapsed.TotalMilliseconds);
        }
        using var query = store.Connection.CreateCommand();
        query.CommandText = cipher
            ? "SELECT count(*) FROM search WHERE search MATCH 'needle'"
            : "SELECT count(*) FROM samples WHERE token=$token";
        if (!cipher) query.Parameters.AddWithValue("$token", Envelope.Token(key, "needle"));
        watch.Restart();
        var hits = Convert.ToInt64(query.ExecuteScalar());
        watch.Stop();
        Check((cipher ? "SQLCipher FTS" : "envelope blind equality") + " index returns exact fixture count", hits == count + 100);
        Measurements.Add(new
        {
            strategy = cipher ? "SQLCipher+FTS5" : "AES-GCM+HMAC-equality-index",
            records = count, payloadBytes = Payload.Length, openMs = open.Elapsed.TotalMilliseconds,
            batchWriteMs = writeMs, batchWritesPerSecond = count * 1000 / writeMs,
            commitP50Ms = Percentile(commits, .5), commitP95Ms = Percentile(commits, .95),
            readP50Ms = Percentile(reads, .5), readP95Ms = Percentile(reads, .95),
            indexMs = watch.Elapsed.TotalMilliseconds,
            databaseBytes = new FileInfo(path).Length, walBytes = new FileInfo(path + "-wal").Length,
        });
        Scan((cipher ? "cipher" : "envelope") + " measured database/WAL", allowMarker: false);
    }

    private static double Percentile(List<double> values, double fraction)
    {
        values.Sort();
        return values[(int)Math.Ceiling(fraction * values.Count) - 1];
    }

    private static bool Contains(byte[] bytes, byte[] needle) => bytes.AsSpan().IndexOf(needle) >= 0;

    private static byte[] ReadObservedBytes(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Scan(string name, bool allowMarker)
    {
        var leaked = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ObservedFiles.Add(Path.GetFileName(file));
            // SQLite locks parts of the live WAL-index mapping on Windows.
            if (file.EndsWith("-shm", StringComparison.Ordinal)) continue;
            var bytes = ReadObservedBytes(file);
            if (Contains(bytes, Encoding.UTF8.GetBytes(Marker)) || Contains(bytes, Encoding.Unicode.GetBytes(Marker)))
                leaked.Add(Path.GetFileName(file));
        }
        Check(name + " UTF-8/UTF-16 sentinel scan", allowMarker || leaked.Count == 0,
            leaked.Count == 0 ? "No fixture marker in observed files; not a general absence-of-plaintext proof." : string.Join(",", leaked));
    }

    private static void WriteReport()
    {
        var report = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            logicalProcessors = Environment.ProcessorCount,
            overallStatus = "blocked",
            fixture = "Synthetic records only; no application database or settings opened.",
            checks = Checks, measurements = Measurements, observedFiles = ObservedFiles.Order().ToArray(),
            scanExclusions = "Live -shm mappings observed but not read: SQLite Windows byte locks; WAL-index metadata, not encrypted page content.",
            crossUser = "BLOCKED until approved different Windows SID rejects original DPAPI blob.",
        };
        var directory = Path.Combine(project, "evidence");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "latest.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void PrepareCrossUser(byte[] key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var ownerSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No SID.");
        var handoff = Path.Combine(root, "cross-user-handoff.json");
        var wrapped = WindowsKey.Wrap(key);
        var recovered = WindowsKey.Unwrap(wrapped);
        Check("cross-user handoff owner roundtrip", recovered.SequenceEqual(key));
        CryptographicOperations.ZeroMemory(recovered);
        var protectedKey = Path.Combine(root, "cross-user-key.dpapi");
        File.WriteAllBytes(protectedKey, wrapped);
        File.WriteAllText(handoff, JsonSerializer.Serialize(new Handoff(ownerSid, Convert.ToBase64String(wrapped))));
        Console.WriteLine("Synthetic handoff created: " + handoff);
        Console.WriteLine("Original ACL-protected synthetic key: " + protectedKey);
        Console.WriteLine("Owner-confirmed handoff SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(handoff))));
        Console.WriteLine("Owner must explicitly copy ONLY this synthetic handoff into a location approved for the second account.");
        Console.WriteLine("Do not alter the owner's scratch ACL or share any credentials. This command leaves its owned scratch run.");
    }

    private static int VerifyCrossUser(string path, string expectedHash)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var bytes = File.ReadAllBytes(path);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedHash)))
            throw new InvalidDataException("Owner-confirmed handoff digest mismatch.");
        var handoff = JsonSerializer.Deserialize<Handoff>(bytes) ?? throw new InvalidDataException("Invalid handoff.");
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No SID.");
        if (sid == handoff.OwnerSid) throw new InvalidOperationException("A different actual Windows SID is required.");
        try
        {
            var value = WindowsKey.Unwrap(Convert.FromBase64String(handoff.Blob));
            CryptographicOperations.ZeroMemory(value);
            throw new InvalidDataException("Cross-user unwrap unexpectedly succeeded.");
        }
        catch (CryptographicException)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "passed", trial = "actual Windows CurrentUser DPAPI different SID denial",
                ownerSidHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(handoff.OwnerSid))),
                executingSidHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))),
                handoffSha256 = expectedHash,
                generatedUtc = DateTimeOffset.UtcNow,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            }));
            return 0;
        }
    }

    private sealed record Handoff(string OwnerSid, string Blob);
}
