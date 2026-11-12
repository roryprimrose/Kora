using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using Kora.Windows.Coordination;

namespace Kora;

internal static class ExitCommandStartup
{
    internal static async Task<int> RunAsync(string[] args,
        Func<CancellationToken, Task<InstanceExitResult>> request, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args is not ["--exit"])
        {
            await error.WriteLineAsync("Usage: Kora.exe --exit");
            return 2;
        }
        try
        {
            var result = await request(cancellationToken);
            if (result is { Status: InstanceExitStatus.NoOwner, ProcessId: null, ProcessExitCode: null })
            {
                await output.WriteLineAsync("NO_OWNER: no assistant ownership was observed; clean shutdown is not implied.");
                return 0;
            }
            if (result is not { Status: InstanceExitStatus.Exited, ProcessId: > 0, ProcessExitCode: not null })
            { throw new InvalidDataException("Exit did not provide an authenticated process result."); }
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"EXITED: processId={result.ProcessId}; processExitCode={result.ProcessExitCode}."));
            if (result.ProcessExitCode != 0)
            {
                await error.WriteLineAsync("The process exited with a failure code; clean shutdown is not confirmed.");
                return 3;
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or UnauthorizedAccessException or Win32Exception or OperationCanceledException or TimeoutException
            or JsonException or ArgumentException or BadImageFormatException or InvalidDataException)
        {
            await error.WriteLineAsync($"Graceful exit was not confirmed ({exception.GetType().Name}). No process was forcibly terminated.");
            return 1;
        }
    }
}
