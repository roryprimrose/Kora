namespace Kora.Application.UnitTests.Diagnostics;

// Activity listeners observe the entire process, including other test collections.
[CollectionDefinition("Host tracing", DisableParallelization = true)]
public sealed class HostTracingTestGroup;
