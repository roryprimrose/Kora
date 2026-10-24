using System.Text;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace Kora.Windows.Dependencies;

#pragma warning disable GHCP001 // The pinned SDK's typed virtual-filesystem protocol, with no disk fallback.
/// <summary>One turn's bounded virtual filesystem. No path is ever resolved against the host filesystem.</summary>
internal sealed class CopilotVolatileSessionFiles : SessionFsProvider, ISessionFsSqliteProvider, ISessionFsBinaryProvider, IDisposable
{
    internal const int MaximumBytes = 262144;
    internal const int MaximumEntries = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Lock gate = new();
    private readonly Dictionary<string, string?> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string workingDirectory;
    private bool disposed;

    internal CopilotVolatileSessionFiles(string workingDirectory)
    {
        this.workingDirectory = Path.GetFullPath(workingDirectory);
        var missing = MissingDirectories(this.workingDirectory);
        if (this.workingDirectory.Length > 1024 || missing.Count > MaximumEntries)
        { throw new InvalidDataException("The volatile working directory exceeds the path or entry limit."); }
        foreach (var directory in missing) { entries.Add(directory, null); }
    }

    protected override Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            path = Check(path, cancellationToken);
            return Task.FromResult(entries.TryGetValue(path, out var value) && value is not null
                ? value : throw new FileNotFoundException("The volatile session file does not exist."));
        }
    }

    protected override Task WriteFileAsync(string path, string content, int? mode, CancellationToken cancellationToken) =>
        Write(path, content, append: false, cancellationToken);

    protected override Task AppendFileAsync(string path, string content, int? mode, CancellationToken cancellationToken) =>
        Write(path, content, append: true, cancellationToken);

    private Task Write(string path, string content, bool append, CancellationToken token)
    {
        lock (gate)
        {
            path = Check(path, token);
            if (entries.TryGetValue(path, out var existing) && existing is null)
            { throw new IOException("A volatile directory cannot be written as a file."); }
            if (content.Length > MaximumBytes) { throw new IOException("The volatile Copilot session storage limit was exceeded."); }
            var value = append ? entries.GetValueOrDefault(path) + content : content;
            var missing = MissingDirectories(Path.GetDirectoryName(path));
            if (entries.Count + missing.Count + (entries.ContainsKey(path) ? 0 : 1) > MaximumEntries
                || StrictUtf8.GetByteCount(value) + entries.Where(item => !string.Equals(item.Key, path, StringComparison.OrdinalIgnoreCase))
                    .Sum(item => StrictUtf8.GetByteCount(item.Value ?? "")) > MaximumBytes)
            {
                throw new IOException("The volatile Copilot session storage limit was exceeded.");
            }
            foreach (var directory in missing) { entries.Add(directory, null); }
            entries[path] = value;
            return Task.CompletedTask;
        }
    }

    protected override Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        lock (gate) { path = Check(path, cancellationToken); return Task.FromResult(entries.ContainsKey(path)); }
    }

    protected override Task<SessionFsStatResult> StatAsync(string path, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            path = Check(path, cancellationToken);
            if (!entries.TryGetValue(path, out var value)) { throw new FileNotFoundException("The volatile session entry does not exist."); }
            return Task.FromResult(new SessionFsStatResult { IsFile = value is not null, IsDirectory = value is null,
                Size = value is null ? 0 : StrictUtf8.GetByteCount(value) });
        }
    }

    protected override Task MakeDirectoryAsync(string path, bool recursive, int? mode, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            path = Check(path, cancellationToken);
            var missing = MissingDirectories(path);
            if (!recursive && missing.Count > 1)
            { throw new DirectoryNotFoundException("The volatile parent directory does not exist."); }
            if (entries.Count + missing.Count > MaximumEntries)
            { throw new IOException("The volatile Copilot session entry limit was exceeded."); }
            foreach (var directory in missing) { entries.Add(directory, null); }
            return Task.CompletedTask;
        }
    }

    protected override Task<IList<string>> ReadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            path = Check(path, cancellationToken);
            RequireDirectory(path);
            return Task.FromResult<IList<string>>(entries.Keys.Where(item =>
                string.Equals(Path.GetDirectoryName(item), path, StringComparison.OrdinalIgnoreCase))
                .Select(item => Path.GetFileName(item) ?? throw new InvalidDataException("A volatile entry requires a name.")).ToArray());
        }
    }

    protected override Task<IList<SessionFsReaddirWithTypesEntry>> ReadDirectoryWithTypesAsync(string path, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            path = Check(path, cancellationToken);
            RequireDirectory(path);
            return Task.FromResult<IList<SessionFsReaddirWithTypesEntry>>(entries
                .Where(item => string.Equals(Path.GetDirectoryName(item.Key), path, StringComparison.OrdinalIgnoreCase))
                .Select(item => new SessionFsReaddirWithTypesEntry
                {
                    Name = Path.GetFileName(item.Key),
                    Type = item.Value is null ? SessionFsReaddirWithTypesEntryType.Directory : SessionFsReaddirWithTypesEntryType.File,
                }).ToArray());
        }
    }

    protected override Task RemoveAsync(string path, bool recursive, bool force, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            path = Check(path, cancellationToken);
            var children = entries.Keys.Where(item => Descendant(item, path)).ToArray();
            if (!recursive && children.Length != 0) { throw new IOException("The volatile directory is not empty."); }
            if (!entries.Remove(path) && !force) { throw new FileNotFoundException("The volatile session entry does not exist."); }
            foreach (var child in children) { entries.Remove(child); }
            return Task.CompletedTask;
        }
    }

    protected override Task RenameAsync(string src, string dest, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            src = Check(src, cancellationToken);
            dest = Check(dest, cancellationToken);
            if (!entries.ContainsKey(src)) { throw new FileNotFoundException("The volatile session entry does not exist."); }
            if (entries.ContainsKey(dest) || Descendant(dest, src))
            { throw new IOException("The volatile rename destination is not available."); }
            RequireDirectory(Path.GetDirectoryName(dest) ?? throw new InvalidDataException("A rename destination requires a parent."));
            var moved = entries.Where(item => string.Equals(item.Key, src, StringComparison.OrdinalIgnoreCase)
                || Descendant(item.Key, src)).Select(item => (Old: item.Key, New: dest + item.Key[src.Length..], item.Value)).ToArray();
            if (moved.Any(item => item.New.Length > 1024)) { throw new InvalidDataException("The volatile rename exceeds the path limit."); }
            foreach (var item in moved) { entries.Remove(item.Old); }
            foreach (var item in moved) { entries.Add(item.New, item.Value); }
            return Task.CompletedTask;
        }
    }

    private string Check(string path, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024)
        { throw new InvalidDataException("The volatile session path is invalid."); }
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, workingDirectory));
        if (normalized.Length > 1024) { throw new InvalidDataException("The volatile session path is invalid."); }
        return normalized;
    }

    private List<string> MissingDirectories(string? path)
    {
        var missing = new List<string>();
        while (path is not null && !entries.ContainsKey(path))
        {
            missing.Add(path);
            path = Path.GetDirectoryName(path);
        }
        if (path is not null) { RequireDirectory(path); }
        return missing;
    }

    private void RequireDirectory(string path)
    {
        if (!entries.TryGetValue(path, out var value) || value is not null)
        { throw new DirectoryNotFoundException("The volatile directory does not exist."); }
    }

    private static bool Descendant(string candidate, string parent) =>
        candidate.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase);

    Task<SessionFsSqliteResult?> ISessionFsSqliteProvider.QueryAsync(SessionFsSqliteQueryType queryType, string query,
        IDictionary<string, object?>? bindParams, CancellationToken cancellationToken) =>
        Task.FromException<SessionFsSqliteResult?>(new NotSupportedException("SQLite is not admitted for this Copilot profile."));

    Task<bool> ISessionFsSqliteProvider.ExistsAsync(CancellationToken cancellationToken) =>
        Task.FromException<bool>(new NotSupportedException("SQLite is not admitted for this Copilot profile."));

    Task<byte[]> ISessionFsBinaryProvider.ReadFileBytesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromException<byte[]>(new NotSupportedException("Binary session storage is not admitted for this Copilot profile."));

    Task ISessionFsBinaryProvider.WriteFileBytesAsync(string path, ReadOnlyMemory<byte> content, int? mode, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("Binary session storage is not admitted for this Copilot profile."));

    public void Dispose()
    {
        lock (gate) { disposed = true; entries.Clear(); }
    }
}
#pragma warning restore GHCP001
