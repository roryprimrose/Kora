namespace Kora.Windows.IntegrationTests;

// Activity listeners observe this process, not only requests from their own test class.
[CollectionDefinition("Host tracing", DisableParallelization = true)]
public sealed class HostTracingTestGroup;
