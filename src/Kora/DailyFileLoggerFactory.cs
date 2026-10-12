using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

using Serilog;
using Serilog.Formatting.Json;

namespace Kora;

internal static class DailyFileLoggerFactory
{
    internal static Serilog.Core.Logger Create(IApplicationDataPaths paths, FileEvidenceHealth health)
    {
        var logDirectory = Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName);
        Directory.CreateDirectory(logDirectory);

        // Serilog normally self-reports file errors instead of throwing. A sticky admission
        // latch makes those failures observable without recursively invoking the failed sink.
        Serilog.Debugging.SelfLog.Enable(_ => health.Fail());
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                new JsonFormatter(renderMessage: true),
                Path.Combine(logDirectory, DailyLogFilePolicy.RollingName),
                rollingInterval: RollingInterval.Day,
                // The default 1 GiB cutoff stops same-day writes; calendar retention is separate.
                fileSizeLimitBytes: null,
                retainedFileCountLimit: 30,
                retainedFileTimeLimit: TimeSpan.FromDays(30),
                rollOnFileSizeLimit: false,
                shared: false)
            .CreateLogger();
        health.RequireHealthy();
        return logger;
    }
}
