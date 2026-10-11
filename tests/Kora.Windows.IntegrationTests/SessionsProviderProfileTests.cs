using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Providers;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class SessionsProviderProfileTests
{
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
    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
