using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

internal sealed class StorageRecoveryFixture : IDisposable, IEvidenceGapReporter
{
    private readonly EvidenceLoggerProvider provider;
    private readonly ILoggerFactory factory;

    internal StorageRecoveryFixture(IApplicationDataPaths paths, WindowsSqliteHostTaskStore tasks)
    {
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.Initialize();
        provider = new([sink], this);
        factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        Recovery = new(tasks, new HostTaskCoordinator(tasks),
            new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>()),
            factory.CreateLogger<DurableHostRecovery>());
    }

    internal DurableHostRecovery Recovery { get; }
    internal List<EvidenceGap> Gaps { get; } = [];

    public void Report(EvidenceGap gap) => Gaps.Add(gap);

    public void Dispose()
    {
        factory.Dispose();
        provider.Dispose();
    }
}
