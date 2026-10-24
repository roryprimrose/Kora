using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    public async Task ExactProductionResolverAdapterHostAndConversationRetainGenuineAnswerOrSuppressCancelledLateOutput(
        RequestOrigin origin, bool cancel)
    {
        await using var runtime = await OllamaAdmissionWorkflowTests.Fixture.Create();
        await using var native = new Fixture();
        var storage = runtime.Storage;
        native.Time.Now = storage.Time.Now;
        await WindowsSqliteConversationTests.FinishInitial(storage);
        var controls = new LocalControls();
        var workspace = new SessionWorkspaceService(storage.Store, new(storage.Tasks), controls,
            NullLogger<SessionWorkspaceService>.Instance);
        var admission = runtime.Admission;
        var review = (await runtime.Workflow.Review(origin, () => runtime.Eligible, Token))!;
        await runtime.Workflow.Confirm(review, origin, () => runtime.Eligible, Token);
        var resolver = new WindowsOllamaRuntimeResolver(runtime.Broker, new RetainedInstallation(), storage.Time,
            NullLogger<WindowsOllamaRuntimeResolver>.Instance, (_, _) => Task.FromResult(runtime.SubjectAvailable));
        var local = (await resolver.ObserveAsync(Token)).For(ProviderService.Local);
        var selected = local.Models.Single();
        var tuple = new ProviderProfileTuple(ProviderService.Local, local.Account!, local.Revision,
            selected.Id, selected.Reasoning.Single().Id, selected.Context.Single().Id);
        var profiles = new LocalProviderProfilePreferences(storage.Paths);
        profiles.Save(new(ProviderService.Local, [tuple]));
        var profileConfiguration = new ProviderProfileConfigurationService(profiles, resolver, admission, controls, storage.Time);
        var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(storage.Paths), admission, controls);
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance, storage.Time);
        var recorded = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
        var registry = new ReadOnlyCapabilityRegistry(controls, new(), new(), new(controls), new(dependencies),
            new(recorded), new(recorded), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        var bindings = new List<WindowsOllamaToolEndpointBinding>();
        var factory = new OllamaModelProviderFactory(resolver, storage.Time, NullLoggerFactory.Instance, binding =>
        {
            bindings.Add(binding);
            return new WindowsOllamaToolTransport(new HttpClient(native.HttpHandler),
                request => request.Provenance == binding.Provenance && request.Profile == binding.Profile ? binding : null,
                (_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(runtime.SubjectAvailable); },
                storage.Time, NullLogger<WindowsOllamaToolTransport>.Instance);
        });
        await using var host = new ModelTurnHost(storage.Store, controls, controls, registry, controls,
            NullLogger<ModelTurnHost>.Instance, storage.Time, [], mode, profileConfiguration, storage.Store, factory);
        var service = new SessionConversationService(storage.Store, storage.Store, workspace, controls, host, storage.Time,
            NullLogger<SessionConversationService>.Instance, host, controls, storage.Store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.Chat = async (message, _) =>
        {
            if (native.Bodies.Count == 1) { return Json(message, Call); }
            entered.TrySetResult();
            if (cancel) { await release.Task; }
            return Json(message, Final);
        };
        var target = await storage.Store.ReadMetadataAsync(storage.Request.SessionId, Token);
        var pending = service.SubmitAsync(target, "What is the actual version?", static () => true, Token, origin: origin);
        await entered.Task.WaitAsync(Token);
        if (cancel)
        {
            var running = await service.ReadRunningConversationAsync(target, static () => true, Token);
            await service.CancelRunningConversationAsync(running!.Target, static () => true, Token, origin);
            host.IsQuiescent.Should().BeFalse();
            pending.IsCompleted.Should().BeFalse();
            bindings.Single().Revoked.IsCancellationRequested.Should().BeTrue();
            release.TrySetResult();
        }
        var receipt = await pending;
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.SessionId.Should().Be(target.Authority.SessionId);
        receipt.Message.Body.Should().Be("What is the actual version?");
        receipt.Message.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Succeeded);
        (receipt.Message.AssistantResponse?.Body).Should().Be(cancel ? null : "Genuine answer");
        native.Bodies.Should().HaveCount(2);
        controls.VersionReads.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
        bindings.Should().ContainSingle();
        bindings[0].Revoked.IsCancellationRequested.Should().BeTrue();
        bindings[0].IsAdmitted().Should().BeFalse();
        using var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, origin);
        var page = await service.ReadPageAsync(target.Authority.SessionId, null, 25, Token);
        page.Messages.Should().ContainSingle();
        (page.Messages[0].AssistantResponse?.Body).Should().Be(cancel ? null : "Genuine answer");
        controls.Audits.Should().Contain(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal));
    }

    private sealed class RetainedInstallation : ILocalProviderInstallationObservation
    {
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LocalProviderInstallation.Installed);
    }

    private sealed class LocalControls : ISessionWorkspaceAccess, ICapabilityHostAccess, ISecurityAuditLog, IApplicationInfo
    {
        internal int VersionReads;
        internal List<SecurityAuditEvent> Audits { get; } = [];
        public bool CanInspect => true;
        public bool CanControl => true;
        public long ControlRevision => 1;
        public bool IsCurrentHost => true;
        public string Version { get { VersionReads++; return "1.0"; } }
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            Audits.Add(auditEvent);
        }
    }
}