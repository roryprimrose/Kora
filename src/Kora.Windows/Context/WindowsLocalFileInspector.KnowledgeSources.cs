using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.KnowledgeSources;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Context;

public sealed partial class WindowsLocalFileInspector
{
    public async Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope,
        CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(KnowledgeSourcePolicy.MaximumOperationTime);
        return await Task.Run(() => InspectSource(selectedPath, scope, operation.Token), operation.Token).ConfigureAwait(false);
    }

    private SourceSelection InspectSource(string path, KnowledgeSourceScope scope, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        activity.Complete(HostOperationOutcome.Failed);
        KnowledgeSourcePolicy.ValidateScope(scope);
        var directories = new List<SourceDirectory>();
        var files = new List<Selection>();
        try
        {
            Visit(path, 0);
            var root = directories[0].Metadata;
            var metadata = new KnowledgeSourceMetadata(path, root.DirectoryIdentity, scope,
                directories.Select(directory => directory.Metadata), files.Select(file => file.Metadata));
            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Metadata.CanonicalPath, right.Metadata.CanonicalPath));
            var result = new SourceSelection(directories, files, metadata);
            result.Validate(token);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch
        {
            foreach (var file in files) { file.Dispose(); }
            foreach (var directory in directories) { directory.Dispose(); }
            if (token.IsCancellationRequested) { activity.Complete(HostOperationOutcome.Cancelled); }
            throw;
        }

        void Visit(string directoryPath, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > KnowledgeSourcePolicy.MaximumDirectoryDepth
                || directories.Count >= KnowledgeSourcePolicy.MaximumDirectories
                || (depth > 0 && scope != KnowledgeSourceScope.BoundedDescendants))
            { throw new InvalidDataException("The whole named source exceeds its explicitly reviewed traversal bounds."); }
            var handles = OpenVerifiedHandles(directoryPath, file: false, token);
            SourceDirectory directory;
            try
            {
                directory = new(handles, new(directoryPath, DirectoryIdentity(handles[^1], directoryPath)),
                    EnumerateSourceDirectory(directoryPath, token));
            }
            catch
            {
                foreach (var handle in handles) { handle.Dispose(); }
                throw;
            }
            directories.Add(directory);
            foreach (var entry in directory.Members)
            {
                token.ThrowIfCancellationRequested();
                if (File.GetAttributes(entry).HasFlag(FileAttributes.Directory)) { Visit(entry, depth + 1); }
                else
                {
                    if (files.Count >= KnowledgeSourcePolicy.MaximumFiles)
                    { throw new InvalidDataException("The complete named source exceeds its file-count bound."); }
                    files.Add(Inspect(entry, token));
                    if (files.Sum(file => file.Metadata.ByteLength) > KnowledgeSourcePolicy.MaximumCombinedBytes)
                    { throw new InvalidDataException("The complete named source exceeds its aggregate byte bound."); }
                }
            }
        }
    }

    private static string[] EnumerateSourceDirectory(string path, CancellationToken token)
    {
        var entries = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            entries.Add(entry);
            if (entries.Count > KnowledgeSourcePolicy.MaximumFiles + KnowledgeSourcePolicy.MaximumDirectories - 1)
            { throw new InvalidDataException("The complete named-source entry inventory exceeds its bound."); }
        }
        entries.Sort(StringComparer.Ordinal);
        return entries.ToArray();
    }

    private sealed class SourceDirectory(List<SafeFileHandle> handles, KnowledgeSourceDirectory metadata,
        string[] members) : IDisposable
    {
        internal KnowledgeSourceDirectory Metadata { get; } = metadata;
        internal string[] Members { get; } = members;
        internal void Validate(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ValidateDirectories(handles, handles.Count);
            if (!string.Equals(DirectoryIdentity(handles[^1], Metadata.CanonicalPath), Metadata.DirectoryIdentity, StringComparison.Ordinal)
                || !EnumerateSourceDirectory(Metadata.CanonicalPath, token).SequenceEqual(Members, StringComparer.Ordinal))
            { throw new InvalidDataException("The complete retained directory identity or membership changed."); }
        }
        public void Dispose()
        {
            for (var index = handles.Count - 1; index >= 0; index--) { handles[index].Dispose(); }
        }
    }

    private sealed class SourceSelection(List<SourceDirectory> directories, List<Selection> files,
        KnowledgeSourceMetadata metadata) : IKnowledgeSourceSelection
    {
        private int disposed;
        public KnowledgeSourceMetadata Metadata { get; } = metadata;
        internal void Validate(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            foreach (var directory in directories) { directory.Validate(token); }
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                file.Validate();
            }
        }
        public async Task ValidateAsync(CancellationToken cancellationToken)
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operation.CancelAfter(KnowledgeSourcePolicy.MaximumOperationTime);
            await Task.Run(() => Validate(operation.Token), operation.Token).ConfigureAwait(false);
        }
        public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var file = files.SingleOrDefault(item => item.Metadata == exactItem)
                ?? throw new InvalidDataException("Only an exact reviewed named-source item may be read.");
            return file.ReadAsync(cancellationToken);
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
            foreach (var file in files) { file.Dispose(); }
            foreach (var directory in directories) { directory.Dispose(); }
        }
    }
}
