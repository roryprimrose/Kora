using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Providers;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Xml.Linq;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class SessionsProviderProfileTests
{
    [Theory]
    [InlineData(SessionProviderField.Model, "model-next")]
    [InlineData(SessionProviderField.Reasoning, "effort-next")]
    [InlineData(SessionProviderField.Context, "window-next")]
    public async Task NativeOneFieldReviewAndConfirmationUsePrivateCompositionAndExactSparseReadback(SessionProviderField field, string id)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var session = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var otherSession = f.Request.SessionId;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
        var prefs = new LocalProviderProfilePreferences(f.Paths);
        var tuple = new ProviderProfileTuple(ProviderService.Local, new("account"), new(1), new("model"), new("effort"), new("window"));
        prefs.Save(new(ProviderService.Unset, [tuple]));
        var appBytes = prefs.Load()!.Serialize();
        var services = new ServiceCollection();
        services.AddSingleton<IPreferenceStore>(new LocalPreferenceStore(f.Paths));
        services.AddSingleton(f.Store);
        services.AddSingleton(workspace);
        services.AddSingleton(admission);
        services.AddSingleton<ISecurityAuditLog>(new Audit());
        services.AddSingleton<TimeProvider>(f.Time);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SessionProviderProfileConfigurationService>>(NullLogger<SessionProviderProfileConfigurationService>.Instance);
        ProviderProfileComposition.Configure(services);
        services.AddSingleton<IProviderCapabilityCatalogue>(new Catalogue(f.Time));
        await using var provider = services.BuildServiceProvider();
        var profiles = provider.GetRequiredService<SessionProviderProfileConfigurationService>();
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(workspace,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance, providers: profiles);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == session));
        await viewer.RefreshProviders();
        viewer.CanSelectProviderField.Should().BeFalse();
        viewer.ProviderService = ProviderService.Local;
        await viewer.ReviewProviderField(SessionProviderField.Service, reset: false);
        await viewer.ConfirmProviders();
        await viewer.RefreshProviders();
        viewer.CanSelectProviderField.Should().BeTrue();
        viewer.ProviderModel = viewer.ProviderModels.Single(model => model.Id.Value is "model-next");
        // Full-profile draft selection must not change options for the current effective model.
        viewer.ProviderFieldReasonings.Select(option => option.Id.Value).Should().Equal("effort", "effort-next");
        viewer.ProviderFieldContexts.Select(option => option.Id.Value).Should().Equal("window", "window-next");
        viewer.ProviderReasoning.Should().BeNull();
        viewer.ProviderContext.Should().BeNull();
        viewer.CanReviewProviderModel.Should().BeFalse();
        if (field == SessionProviderField.Model) { viewer.ProviderFieldModel = viewer.ProviderModels.Single(model => model.Id.Value is "model-next"); }
        if (field == SessionProviderField.Reasoning) { viewer.ProviderFieldReasoning = viewer.ProviderFieldReasonings.Single(option => option.Id.Value is "effort-next"); }
        if (field == SessionProviderField.Context) { viewer.ProviderFieldContext = viewer.ProviderFieldContexts.Single(option => option.Id.Value is "window-next"); }
        var before = await f.Store.ReadProviderProfile(session, f.Token);
        await viewer.ReviewProviderField(field, reset: false);
        viewer.CanConfirmProviders.Should().BeTrue();
        (await f.Store.ReadProviderProfile(session, f.Token)).Desired.Serialize().Should().Be(before.Desired.Serialize());
        using var review = JsonDocument.Parse(viewer.ProviderStatus);
        review.RootElement.GetProperty("field").GetString().Should().Be(field.ToString());
        review.RootElement.GetProperty("PreviousEffective").GetProperty("Model").GetProperty("Value").GetString().Should().Be("model");
        var expected = new SessionProviderOverrides(ProviderService.Local).Select(
            field switch
            {
                SessionProviderField.Model => tuple with { Model = new(id) },
                SessionProviderField.Reasoning => tuple with { Reasoning = new(id) },
                _ => tuple with { Context = new(id) },
            }, field);
        await viewer.ConfirmProviders();
        var saved = await f.Store.ReadProviderProfile(session, f.Token);
        saved.Desired.Serialize().Should().Be(expected.Serialize());
        saved.Revision.Should().Be(2);
        saved.Held.Should().BeFalse();
        using (var connection = f.OpenRaw())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT payload FROM session_provider_profile WHERE session_id=$session;";
            command.Parameters.AddWithValue("$session", session.Value.ToString("D"));
            ((string)command.ExecuteScalar()!).Should().Be(expected.Serialize());
        }
        using (var connection = f.OpenRaw())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT envelope FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId')=$action ORDER BY sequence;";
            command.Parameters.AddWithValue("$action", "session.provider-profile");
            using var rows = command.ExecuteReader();
            var audits = new List<JsonElement>();
            while (rows.Read())
            {
                using var document = JsonDocument.Parse(rows.GetString(0));
                audits.Add(document.RootElement.Clone());
            }
            audits.Should().HaveCount(6);
            var fieldAudits = audits.TakeLast(3).ToArray();
            fieldAudits.Select(audit => audit.GetProperty("Audit").GetProperty("Outcome").GetInt32())
                .Should().Equal((int)SecurityAuditOutcome.Requested, (int)SecurityAuditOutcome.Requested, (int)SecurityAuditOutcome.Succeeded);
            foreach (var audit in fieldAudits)
            {
                audit.GetProperty("Request").GetProperty("SessionId").GetProperty("Value").GetGuid().Should().Be(session.Value);
                audit.GetProperty("TraceId").GetString()!.Length.Should().Be(32);
                audit.GetProperty("SpanId").GetString()!.Length.Should().Be(16);
                audit.GetProperty("Audit").GetProperty("CorrelationId").GetGuid().Should().NotBeEmpty();
                audit.GetProperty("Request").GetRawText().Should().Be(fieldAudits[0].GetProperty("Request").GetRawText());
                audit.GetProperty("TraceId").GetString().Should().Be(fieldAudits[0].GetProperty("TraceId").GetString());
            }
        }
        (await f.Store.ReadProviderProfile(otherSession, f.Token)).Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        prefs.Load()!.Serialize().Should().Be(appBytes);
        await viewer.RefreshProviders();
        viewer.ProviderFieldContext = viewer.ProviderFieldContexts[0];
        await viewer.ReviewProviderField(SessionProviderField.Context, reset: false);
        viewer.ProviderFieldContext = viewer.ProviderFieldContexts[1];
        viewer.CanConfirmProviders.Should().BeFalse();
        viewer.ClearProviderReview();
        (await f.Store.ReadProviderProfile(session, f.Token)).Revision.Should().Be(2);
        await viewer.RefreshProviders();
        viewer.ProviderFieldReasoning = viewer.ProviderFieldReasonings[0];
        await viewer.ReviewProviderField(SessionProviderField.Reasoning, reset: false);
        await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == otherSession));
        viewer.CanConfirmProviders.Should().BeFalse();
        await viewer.ConfirmProviders();
        (await f.Store.ReadProviderProfile(session, f.Token)).Revision.Should().Be(2);
        (await f.Store.ReadProviderProfile(otherSession, f.Token)).Revision.Should().Be(0);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == session));
        await viewer.RefreshProviders();
        viewer.ProviderModel = viewer.ProviderModels.Single(model => model.Id.Value is "model");
        viewer.ProviderReasoning = viewer.ProviderReasonings[0];
        viewer.ProviderContext = viewer.ProviderContexts[0];
        viewer.CanReviewProviderModel.Should().BeTrue();
        await viewer.ReviewProviderField(null, reset: false);
        await viewer.ConfirmProviders();
        (await f.Store.ReadProviderProfile(session, f.Token)).Desired.Serialize()
            .Should().Be(new SessionProviderOverrides(ProviderService.Local).Select(tuple, null).Serialize());
        await viewer.RefreshProviders();
        await viewer.ReviewProviderField(field, reset: true);
        await viewer.ConfirmProviders();
        (await f.Store.ReadProviderProfile(session, f.Token)).Desired.Serialize()
            .Should().Be(new SessionProviderOverrides(ProviderService.Local).Select(tuple, null).Reset(field).Serialize());
        await viewer.RefreshProviders();
        await viewer.ReviewProviderField(null, reset: true);
        await viewer.ConfirmProviders();
        (await f.Store.ReadProviderProfile(session, f.Token)).Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        prefs.Load()!.Serialize().Should().Be(appBytes);
        viewer.Close();
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }

    [Fact]
    public void NativeFieldPickersBindEffectiveModelAndKeepSeparateFullProfileRecovery()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, ".github", "copilot-instructions.md"))) { root = root.Parent; }
        var controls = XDocument.Load(Path.Combine(root!.FullName, "src", "Kora", "SessionsWindow.axaml")).Descendants().ToArray();
        foreach (var (selected, source) in new[]
        {
            ("ProviderFieldModel", "ProviderModels"), ("ProviderFieldReasoning", "ProviderFieldReasonings"), ("ProviderFieldContext", "ProviderFieldContexts"),
        })
        {
            var picker = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding " + selected + ", Mode=TwoWay}", StringComparison.Ordinal));
            picker.Attribute("ItemsSource")!.Value.Should().Be("{Binding " + source + "}");
            picker.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanSelectProviderField}");
        }
        foreach (var (name, enabled) in new[]
        {
            ("ReviewProviderModel", "CanReviewProviderModelField"), ("ReviewProviderReasoning", "CanReviewProviderReasoningField"),
            ("ReviewProviderContext", "CanReviewProviderContextField"), ("ReviewProviderTuple", "CanReviewProviderModel"), ("ConfirmProviders", "CanConfirmProviders"),
        })
        {
            controls.Single(control => control.Attributes().Any(attribute => attribute.Name.LocalName is "Name" && string.Equals(attribute.Value, name, StringComparison.Ordinal)))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding " + enabled + "}");
        }
    }

    [Fact]
    public async Task RealNativeSelectedSessionReviewSharesPrivateDurableAuthorityAndKeepsCatalogueUnavailable()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
        var prefs = new LocalProviderProfilePreferences(f.Paths);
        var catalogue = new UnavailableProviderCapabilityCatalogue(new Installation(), f.Time);
        var app = new ProviderProfileConfigurationService(prefs, catalogue, admission, new Audit(), f.Time);
        var profiles = new SessionProviderProfileConfigurationService(workspace, f.Store, prefs, catalogue, f.Time,
            NullLogger<SessionProviderProfileConfigurationService>.Instance, app);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(workspace,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance, providers: profiles);
        viewer.CanInspectProviders.Should().BeFalse();
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.CanInspectProviders.Should().BeTrue();
        var priorActivity = (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).LastMeaningfulActivity;
        var priorTasks = f.Count("host_tasks");
        f.Time.Now = f.Time.Now.AddHours(1);
        await viewer.RefreshProviders();
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).LastMeaningfulActivity.Should().Be(priorActivity);
        f.Count("host_tasks").Should().Be(priorTasks);
        viewer.ProviderModels.Should().BeEmpty();
        viewer.CanReviewProviderModel.Should().BeFalse();
        viewer.ProviderStatus.Should().Contain("inherited").And.Contain("Unavailable");
        viewer.ProviderService = ProviderService.Claude;
        await viewer.ReviewProviderField(SessionProviderField.Service, reset: false);
        viewer.CanConfirmProviders.Should().BeTrue();
        (await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token)).Revision.Should().Be(0);
        await viewer.ConfirmProviders();
        (await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token)).Desired.Service.Should().Be(ProviderService.Claude);
        prefs.Load().Should().BeNull();
        await viewer.RefreshProviders();
        await viewer.ReviewProviderField(null, reset: true);
        await viewer.ConfirmProviders();
        (await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token)).Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        await viewer.RefreshProviders();
        await viewer.ReviewProviderField(SessionProviderField.Service, reset: false);
        viewer.Close();
        viewer.CanConfirmProviders.Should().BeFalse();
        viewer.CanInspectProviders.Should().BeFalse();
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }

    private sealed class Installation : ILocalProviderInstallationObservation
    {
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken token) => Task.FromResult(LocalProviderInstallation.Missing);
    }
    private sealed class Catalogue(TimeProvider time) : IProviderCapabilityCatalogue
    {
        public Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = ModelProviderSelection.OllamaCandidate;
            return Task.FromResult(new ProviderCatalogueObservation(LocalProviderInstallation.Installed,
                Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service =>
                    new ProviderCapabilityCatalogue(service, service == ProviderService.Local ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable,
                        service == ProviderService.Local ? new("account") : null, new(1), time.GetUtcNow().AddMinutes(-1), time.GetUtcNow().AddDays(1),
                        service != ProviderService.Local ? [] :
                        [
                            new(new("model"), "Synthetic original model", selection,
                                [new(new("effort"), "Synthetic effort", false), new(new("effort-next"), "Synthetic effort next", false)],
                                [new(new("window"), "Synthetic window", false, 4096), new(new("window-next"), "Synthetic window next", false, 8192)]),
                            new(new("model-next"), "Synthetic next model", selection with { Model = new(Guid.Parse("11111111-1111-1111-1111-111111111111")) },
                                [new(new("effort-next"), "Synthetic effort next", false), new(new("effort"), "Synthetic effort", false)],
                                [new(new("window-next"), "Synthetic window next", false, 8192), new(new("window"), "Synthetic window", false, 4096)]),
                        ])).ToArray()));
        }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
