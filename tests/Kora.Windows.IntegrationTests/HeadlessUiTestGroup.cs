namespace Kora.Windows.IntegrationTests;

/// <summary>
/// Serializes all headless-Avalonia runtime tests against the single process-wide UI thread and
/// isolated application that <see cref="HeadlessSession"/> bootstraps. Running these concurrently with
/// other parallel test collections can race the one-time <c>AppBuilder</c>/compositor setup against
/// unrelated threads, intermittently throwing a dispatcher thread-ownership error.
/// </summary>
[CollectionDefinition(nameof(HeadlessUiTestGroup), DisableParallelization = true)]
public sealed class HeadlessUiTestGroup;
