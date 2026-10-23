namespace Kora.Windows.Dependencies;

/// <summary>Private flat component storage; implementations reject foreign roots, links and ownership.</summary>
public interface ISpeechComponentFiles
{
    /// <summary>Gets the protected data root.</summary>
    string Root { get; }
    /// <summary>Inspects whether the protected root exists without creating it.</summary>
    bool RootExists();
    /// <summary>Creates or validates only the private data partition.</summary>
    void EnsureRoot();
    /// <summary>Serializes mutations across processes.</summary>
    Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken);
    /// <summary>Checks a contained entry, rejecting inaccessible or unsafe paths.</summary>
    bool Exists(string path);
    /// <summary>Lists immediate contained entry names without following links.</summary>
    IReadOnlyList<string> Names(string directoryPath);
    /// <summary>Opens a private file for asynchronous reading.</summary>
    Stream Read(string path);
    /// <summary>Creates a new private file without replacement.</summary>
    Stream Create(string path);
    /// <summary>Creates a new private directory without replacement.</summary>
    void CreateDirectory(string path);
    /// <summary>Atomically moves a private directory without replacement.</summary>
    void MoveDirectory(string source, string destination);
    /// <summary>Publishes a closed private file atomically without replacement.</summary>
    void MoveFile(string source, string destination);
    /// <summary>Deletes one validated private file.</summary>
    void DeleteFile(string path);
    /// <summary>Deletes one empty validated private directory, never recursively.</summary>
    void DeleteDirectory(string path);
}
