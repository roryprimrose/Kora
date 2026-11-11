namespace Kora.Windows.Coordination;

public sealed record InstanceExitResult(InstanceExitStatus Status, int? ProcessId = null, int? ProcessExitCode = null);
