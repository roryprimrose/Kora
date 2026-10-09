using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Host tracing")]
public sealed class WindowsSqliteProviderModeConfigurationTests
{
    [Fact]
    public async Task SharedSessionLeaseAndAtomicPreferencesSurviveRestartWithoutNewGrantsOrQuestions()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
        using var call = new CallCommunicationPolicy(new Call());
        var preferences = new LocalModelProviderModePreferences(f.Paths);
        var audit = new Audit();
        var service = new ProviderModeConfigurationService(preferences, admission, audit);
        await service.RefreshAsync(RequestOrigin.LocalUi, static () => true, f.Token);
        await service.SelectAsync(service.Choices.Single(choice => choice.Mode == ModelProviderMode.HostedPreferred),
            call.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, call, static () => true, f.Token);
        preferences.Load().Should().Be(ModelProviderMode.HostedPreferred);
        var restarted = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f.Paths), admission, audit);
        restarted.Observe();
        restarted.Get().Desired.Should().Be(ModelProviderMode.HostedPreferred);
        await restarted.RefreshAsync(RequestOrigin.LocalUi, static () => true, f.Token);
        await restarted.SelectAsync(restarted.Choices.Single(choice => choice.Mode == ModelProviderMode.LocalOnly),
            call.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, call, static () => true, f.Token);
        restarted.Get().Saved.Should().Be(ModelProviderMode.LocalOnly);
        audit.Requests.Should().HaveCount(4).And.OnlyContain(request => request.Origin == RequestOrigin.LocalUi);
        var metadata = await f.Store.ReadMetadataAsync(audit.Requests[0].SessionId, f.Token);
        metadata.Authority.IsActive.Should().BeTrue();
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<HostRequest> Requests { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Requests.Add(HostActivity.RequireCurrent().Request);
    }
    private sealed class Call : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
