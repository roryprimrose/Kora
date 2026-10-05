// The reviewed public SessionFsProvider metadata types are experimental.
#pragma warning disable GHCP001
using System.Collections.Concurrent;
using System.Text;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace Kora.Rt1;

internal sealed class VolatileSessionFs : SessionFsProvider
{
    internal const string VirtualRoot = @"Q:\rt1-virtual";
    private readonly ConcurrentDictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock writeGate = new();
    internal int Writes;
    internal int RejectedPaths;
    internal int WriteRejections;
    internal bool RejectWrites { get; set; }
    internal int Count => files.Count;
    internal void Clear() { files.Clear(); directories.Clear(); }

    private string Resolve(string path)
    {
        var full = Path.GetFullPath(path, VirtualRoot);
        if (full != VirtualRoot && !full.StartsWith(VirtualRoot + '\\', StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref RejectedPaths);
            throw new UnauthorizedAccessException("Outside volatile session namespace.");
        }
        return full;
    }

    protected override Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(files.TryGetValue(Resolve(path), out var value)
            ? value : throw new FileNotFoundException("Missing volatile session file."));
    }

    protected override Task WriteFileAsync(string path, string content, int? mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RejectWrites)
        {
            Interlocked.Increment(ref WriteRejections);
            throw new IOException("Synthetic volatile-store write denial.");
        }
        var key = Resolve(path);
        lock (writeGate)
        {
            if (Encoding.UTF8.GetByteCount(content) > 262144 || (files.Count >= 64 && !files.ContainsKey(key)))
                throw new IOException("Volatile fixture storage budget exceeded.");
            files[key] = content;
        }
        Interlocked.Increment(ref Writes);
        return Task.CompletedTask;
    }

    protected override Task AppendFileAsync(string path, string content, int? mode, CancellationToken cancellationToken)
    {
        var key = Resolve(path);
        files.TryGetValue(key, out var previous);
        return WriteFileAsync(key, previous + content, mode, cancellationToken);
    }

    protected override Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(files.ContainsKey(Resolve(path)) || directories.ContainsKey(Resolve(path)));

    protected override Task<SessionFsStatResult> StatAsync(string path, CancellationToken cancellationToken)
    {
        var key = Resolve(path);
        var isFile = files.TryGetValue(key, out var content);
        if (!isFile && !directories.ContainsKey(key)) throw new FileNotFoundException("Missing volatile entry.");
        return Task.FromResult(new SessionFsStatResult
        {
            IsFile = isFile, IsDirectory = !isFile, Size = Encoding.UTF8.GetByteCount(content ?? ""),
            Birthtime = DateTimeOffset.UnixEpoch, Mtime = DateTimeOffset.UnixEpoch
        });
    }

    protected override Task MakeDirectoryAsync(string path, bool recursive, int? mode, CancellationToken cancellationToken)
    {
        directories.TryAdd(Resolve(path), 0);
        return Task.CompletedTask;
    }

    protected override Task<IList<string>> ReadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var prefix = Resolve(path) + '\\';
        IList<string> result = files.Keys.Concat(directories.Keys)
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => key[prefix.Length..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult(result);
    }

    protected override async Task<IList<SessionFsReaddirWithTypesEntry>> ReadDirectoryWithTypesAsync(string path, CancellationToken cancellationToken)
    {
        var entries = await ReadDirectoryAsync(path, cancellationToken);
        return entries.Select(name => new SessionFsReaddirWithTypesEntry
        {
            Name = name, Type = files.ContainsKey(Resolve(Path.Combine(path, name)))
                ? SessionFsReaddirWithTypesEntryType.File : SessionFsReaddirWithTypesEntryType.Directory
        }).ToList();
    }

    protected override Task RemoveAsync(string path, bool recursive, bool force, CancellationToken cancellationToken)
    {
        var key = Resolve(path);
        foreach (var entry in files.Keys.Where(value => value == key || (recursive && value.StartsWith(key + '\\', StringComparison.OrdinalIgnoreCase))))
            files.TryRemove(entry, out _);
        directories.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    protected override Task RenameAsync(string src, string dest, CancellationToken cancellationToken)
    {
        var source = Resolve(src);
        var destination = Resolve(dest);
        if (!files.TryRemove(source, out var content)) throw new FileNotFoundException("Missing volatile rename source.");
        files[destination] = content;
        return Task.CompletedTask;
    }
}
#pragma warning restore GHCP001
