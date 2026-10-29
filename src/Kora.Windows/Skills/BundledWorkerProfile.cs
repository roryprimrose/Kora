namespace Kora.Windows.Skills;

// Supplied only by host qualification, never by artifact metadata or a saved user path.
internal sealed record BundledWorkerProfile(string Executable, string ExecutableDigest,
    string AppContainerSid, string ImplementationDigest);