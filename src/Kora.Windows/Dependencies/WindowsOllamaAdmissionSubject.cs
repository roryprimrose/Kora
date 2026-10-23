using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Volatile current-user/current-session process observation, not qualification or consent.</summary>
public sealed record WindowsOllamaAdmissionSubject(
    WindowsOllamaRuntimeIdentity Runtime, ProviderAccountId Account, int WindowsSessionId);