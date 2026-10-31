using Kora.Windows.Dependencies;

using System.Security.Cryptography;

namespace Kora.Windows.IntegrationTests.Dependencies;

internal sealed class InMemorySpeechComponentFiles : ISpeechComponentFiles
{
    internal Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
    internal bool RootPresent { get; set; }
    internal bool UnsafeRoot { get; set; }
    internal bool ThrowBeforeMove { get; set; }
    internal bool ThrowAfterMove { get; set; }
    internal bool FailFinalDirectoryDelete { get; set; }
    internal Action<string>? AfterDelete { get; set; }
    internal int Moves { get; private set; }
    internal int Leases { get; private set; }
    internal int Releases { get; private set; }
    private readonly Dictionary<string, string> identities = new(StringComparer.Ordinal);
    public string Root => @"Q:\fixture\SpeechComponentsV1\Artifacts";
    public bool RootExists() { CheckRoot(); return RootPresent; }
    public void EnsureRoot() { CheckRoot(); RootPresent = true; Directories.Add(Root); }
    public Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckRoot();
        if (Leases != Releases) { throw new IOException("Fixture store already leased."); }
        Leases++;
        return Task.FromResult<IAsyncDisposable>(new Lease(this));
    }
    public bool Exists(string path) { Validate(path); return Files.ContainsKey(path) || Directories.Contains(path); }
    public IReadOnlyList<string> Names(string directoryPath)
    {
        Validate(directoryPath);
        return Files.Keys.Concat(Directories).Where(path =>
            string.Equals(Path.GetDirectoryName(path), directoryPath, StringComparison.Ordinal))
            .Select(Path.GetFileName).Select(name => name!).ToArray();
    }
    public Stream Read(string path) { Validate(path); return new MemoryStream(Files[path], writable: false); }
    public string Identity(string path)
    {
        Validate(path);
        if (!Exists(path)) { throw new IOException("Fixture identity is absent."); }
        if (!identities.TryGetValue(path, out var identity)) { identities.Add(path, identity = Guid.NewGuid().ToString("N")); }
        return identity;
    }
    public IDisposable PinDirectory(string path) { Validate(path); return new Pin(); }
    public Task DeleteVerifiedFileAsync(string path, string identity, long bytes, string digest,
        Func<bool> admitted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(Identity(path), identity, StringComparison.Ordinal) || Files[path].LongLength != bytes
            || !string.Equals(Convert.ToHexString(SHA256.HashData(Files[path])), digest, StringComparison.Ordinal))
        { throw new InvalidDataException("Fixture refuses replaced or modified bytes."); }
        if (!admitted()) { throw new UnauthorizedAccessException("Fixture removal revoked."); }
        DeleteFile(path);
        return Task.CompletedTask;
    }
    public Stream Create(string path)
    {
        Validate(path);
        if (Exists(path)) { throw new IOException("Fixture refuses overwrite."); }
        Files.Add(path, []);
        return new Output(this, path);
    }
    public void CreateDirectory(string path)
    {
        Validate(path);
        if (!Directories.Add(path)) { throw new IOException("Fixture refuses overwrite."); }
    }
    public void MoveDirectory(string source, string destination, string? expectedIdentity = null)
    {
        Validate(source);
        Validate(destination);
        if (ThrowBeforeMove) { throw new IOException("Fixture publication failed before rename."); }
        if (Exists(destination)) { throw new IOException("Fixture refuses destination overwrite."); }
        if (expectedIdentity is not null && !string.Equals(Identity(source), expectedIdentity, StringComparison.Ordinal))
        { throw new InvalidDataException("Replaced fixture directory."); }
        if (!Directories.Remove(source)) { throw new IOException("Fixture source absent."); }
        Directories.Add(destination);
        foreach (var entry in Files.Where(pair => pair.Key.StartsWith(source + "\\", StringComparison.Ordinal)).ToArray())
        {
            Files.Remove(entry.Key);
            Files.Add(destination + entry.Key[source.Length..], entry.Value);
        }
        foreach (var entry in identities.Where(pair => string.Equals(pair.Key, source, StringComparison.Ordinal)
            || pair.Key.StartsWith(source + "\\", StringComparison.Ordinal)).ToArray())
        {
            identities.Remove(entry.Key);
            identities.Add(destination + entry.Key[source.Length..], entry.Value);
        }
        Moves++;
        if (ThrowAfterMove) { throw new IOException("Fixture rename outcome uncertain."); }
    }
    public void MoveFile(string source, string destination)
    {
        Validate(source);
        Validate(destination);
        if (Exists(destination)) { throw new IOException("Fixture refuses record overwrite."); }
        var identity = Identity(source);
        Files.Add(destination, Files[source]);
        Files.Remove(source);
        identities.Remove(source);
        identities.Add(destination, identity);
    }
    public void DeleteFile(string path)
    {
        Validate(path);
        Files.Remove(path);
        identities.Remove(path);
        AfterDelete?.Invoke(path);
    }
    public void DeleteDirectory(string path, string? expectedIdentity = null)
    {
        Validate(path);
        if (FailFinalDirectoryDelete && path.EndsWith(".removing", StringComparison.Ordinal))
        {
            throw new IOException("Fixture interrupted final directory deletion.");
        }
        if (Names(path).Count != 0) { throw new IOException("Fixture refuses recursive deletion."); }
        if (expectedIdentity is not null && !string.Equals(Identity(path), expectedIdentity, StringComparison.Ordinal))
        { throw new InvalidDataException("Replaced fixture directory."); }
        Directories.Remove(path);
        identities.Remove(path);
    }
    private void CheckRoot()
    {
        if (UnsafeRoot) { throw new UnauthorizedAccessException("Fixture models a link, foreign ACL or untrusted root."); }
    }
    private void Validate(string path)
    {
        CheckRoot();
        if (!string.Equals(path, Root, StringComparison.Ordinal)
            && !Path.GetFullPath(path).StartsWith(Root + "\\", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Fixture path escaped private root.");
        }
    }
    private sealed class Lease(InMemorySpeechComponentFiles owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { owner.Releases++; return ValueTask.CompletedTask; }
    }
    private sealed class Pin : IDisposable { public void Dispose() { } }
    private sealed class Output(InMemorySpeechComponentFiles owner, string path) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) { owner.Files[path] = ToArray(); }
            base.Dispose(disposing);
        }
    }
}