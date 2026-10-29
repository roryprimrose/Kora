using Kora.Application.Auditing;
using Kora.Application.Dependencies;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Windows.Dependencies;
using Kora.Windows.Audio;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Setup;

internal sealed class InstallerSpeechComponentHost : IDisposable, ISecurityAuditLog, IEvidenceGapReporter
{
    private readonly HttpSpeechComponentAcquisition acquisition = HttpSpeechComponentAcquisition.CreateDefault();
    private readonly WindowsSqliteEvidenceSink sink;
    private readonly EvidenceLoggerProvider evidence;
    private readonly LoggerSecurityAuditLog audit;
    private bool initialized;

    internal InstallerSpeechComponentHost()
    {
        var paths = new ApplicationDataPaths(developmentBuild: false);
        sink = new WindowsSqliteEvidenceSink(paths);
        evidence = new EvidenceLoggerProvider([sink], this);
        audit = new LoggerSecurityAuditLog(new EvidenceLogger<LoggerSecurityAuditLog>(evidence));
        var files = new WindowsSpeechComponentFiles(paths);
        Service = new SpeechComponentSetupService(HandsFreeSetupCatalogue.Production,
            new WindowsSpeechComponentStore(files, acquisition),
            new HandsFreeSetupRuntime(files), this, new EvidenceLogger<SpeechComponentSetupService>(evidence), TimeProvider.System);
    }

    internal SpeechComponentSetupService Service { get; }

    public void Write(SecurityAuditEvent auditEvent)
    {
        // Preflight is read-only. Audit storage is opened only for an explicitly selected operation.
        if (!initialized) { sink.Initialize(); initialized = true; }
        audit.Write(auditEvent);
    }

    public void Report(EvidenceGap gap) =>
        throw new IOException("Installer speech setup evidence delivery failed. No subsequent asset operation is authorized.");

    public void Dispose()
    {
        evidence.Dispose();
        acquisition.Dispose();
    }

    private sealed class EvidenceLogger<T>(EvidenceLoggerProvider provider) : ILogger<T>
    {
        private readonly ILogger inner = provider.CreateLogger(typeof(T).FullName!);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
