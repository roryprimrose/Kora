using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(ProcessGlobalEvidenceTestGroup))]
public sealed class WindowsSqliteProviderProfileFieldTests
{
    [Theory]
    [InlineData(ProviderProfileField.Model, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(ProviderProfileField.Reasoning, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(ProviderProfileField.Context, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task RealPrivateCompositionSavesCompleteFieldTupleAndCorrelatedAuthoritativeAuditWithoutChangingSessionPolicy(
        ProviderProfileField field, RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var sessionPolicy = (await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token)).Desired.Serialize();
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var observed = new Observed();
        using var loggerProvider = new EvidenceLoggerProvider([sink, observed], observed);
        using var logs = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(loggerProvider));
        var audit = new LoggerSecurityAuditLog(logs.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
        using var policy = new CallCommunicationPolicy(new ClearCall());
        var preferences = new LocalProviderProfilePreferences(f.Paths);
        var tuple = new ProviderProfileTuple(ProviderService.Local, new("account"), new(1), new("model"), new("effort"), new("window"));
        preferences.Save(new(ProviderService.Unset, [tuple]));
        var services = new ServiceCollection();
        services.AddSingleton<IPreferenceStore>(new LocalPreferenceStore(f.Paths));
        services.AddSingleton(admission);
        services.AddSingleton<ISecurityAuditLog>(audit);
        services.AddSingleton<TimeProvider>(f.Time);
        ProviderProfileComposition.Configure(services);
        services.AddSingleton<IProviderCapabilityCatalogue>(new Catalogue(f.Time));
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ProviderProfileConfigurationService>();
        await service.RefreshAsync(origin, static () => true, f.Token);
        var selected = service.Choices.Single(choice => choice.Service == ProviderService.Local);
        var replacement = field switch
        {
            ProviderProfileField.Model => "model-next",
            ProviderProfileField.Reasoning => "effort-next",
            _ => "window-next",
        };
        var choice = service.CreateFieldChoice(field, new(replacement), selected);
        preferences.Load()!.For(ProviderService.Local).Should().Be(tuple);
        (await service.SelectAsync(choice, policy.Current.Revision, origin, initiator, policy, static () => true, f.Token)).Should().BeTrue();
        preferences.Load()!.For(ProviderService.Local).Should().Be(ProviderProfileRules.ReplaceField(tuple, field, new(replacement)));
        preferences.Load()!.Service.Should().Be(ProviderService.Unset);
        (await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token)).Desired.Serialize().Should().Be(sessionPolicy);
        observed.Gaps.Should().BeEmpty();
        observed.Audits.Should().HaveCount(2).And.OnlyContain(envelope =>
            envelope.Diagnostic.Host != null && envelope.Diagnostic.Host.Origin == origin && envelope.Audit.Initiator == initiator
            && envelope.Diagnostic.Trace != null);
        observed.Audits[1].Audit.CorrelationId.Should().Be(observed.Audits[0].Audit.CorrelationId);
        observed.Audits[1].Diagnostic.Host.Should().Be(observed.Audits[0].Diagnostic.Host);
        observed.Audits.Select(envelope => envelope.Audit.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
        (await f.Tasks.ReadIncompleteAsync(10, f.Token)).Should().HaveCount(1);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(f.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
            Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        await connection.OpenAsync(f.Token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId') = $action;";
        command.Parameters.AddWithValue("$action", "configuration.provider-profile." + field.ToString().ToLowerInvariant());
        Convert.ToInt64(await command.ExecuteScalarAsync(f.Token), System.Globalization.CultureInfo.InvariantCulture).Should().Be(2);
    }

    private sealed class Catalogue(TimeProvider time) : IProviderCapabilityCatalogue
    {
        private static readonly string[] ModelIds = ["model", "model-next"];
        public Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProviderCatalogueObservation(LocalProviderInstallation.Installed,
                Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service =>
                    new ProviderCapabilityCatalogue(service, service == ProviderService.Local ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable,
                        service == ProviderService.Local ? new("account") : null, new(1), time.GetUtcNow().AddMinutes(-1), time.GetUtcNow().AddMinutes(10),
                        service != ProviderService.Local ? [] : ModelIds.Select((id, index) =>
                            new ProviderCatalogueModel(new(id), "Inert fake model", ModelProviderSelection.OllamaCandidate with
                            { Model = new(new Guid(index + 1, 0, 0, new byte[8])) },
                                [new(new("effort"), "Exact effort", false), new(new("effort-next"), "Exact effort", false)],
                                [new(new("window"), "Exact window", false, 4096), new(new("window-next"), "Exact window", false, 8192)])).ToArray())).ToArray()));
        }
    }

    private sealed class ClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }

    private sealed class Observed : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "provider-field-fixture";
        internal List<AuditEnvelope> Audits { get; } = [];
        internal List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) { }
        public void WriteAudit(AuditEnvelope envelope) => Audits.Add(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) { }
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
}
