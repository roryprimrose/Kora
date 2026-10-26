using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
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
    [InlineData(RequestOrigin.LocalUi, false, 0)]
    [InlineData(RequestOrigin.ActivatedVoice, false, 0)]
    [InlineData(RequestOrigin.LocalUi, true, 0)]
    [InlineData(RequestOrigin.LocalUi, false, 1)]
    [InlineData(RequestOrigin.ActivatedVoice, false, 1)]
    [InlineData(RequestOrigin.LocalUi, false, 2)]
    [InlineData(RequestOrigin.ActivatedVoice, false, 2)]
    [InlineData(RequestOrigin.LocalUi, true, 2)]
    public async Task ExactProductionResolverAdapterHostAndConversationRetainGenuineAnswerOrSuppressCancelledLateOutput(
        RequestOrigin origin, bool cancel, int questionRound)
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
        var finalRound = questionRound == 2 ? 4 : questionRound == 1 ? 3 : 2;
        native.Chat = async (message, _) =>
        {
            if (native.Bodies.Count == questionRound) { return Json(message, StructuredResponse(StructuredQuestion)); }
            if (native.Bodies.Count < finalRound) { return Json(message, Call); }
            entered.TrySetResult();
            if (cancel) { await release.Task; }
            return Json(message, questionRound == 0 ? Final : StructuredResponse("""{"kind":"answer","answer":"Genuine answer"}"""));
        };
        var target = await storage.Store.ReadMetadataAsync(storage.Request.SessionId, Token);
        var pending = questionRound == 0
            ? service.SubmitAsync(target, "What is the actual version?", static () => true, Token, origin: origin)
            : service.SubmitReadOnlyWithClarificationAsync(target, "What is the actual version?", static () => true,
                async (request, generation, spec, expiry, _, token) =>
                {
                    await storage.Store.PublishTrustedSnapshotAsync(request, new(true, false, false, true), null, 0, token);
                    var questions = new HostQuestionService(storage.Store, storage.Time);
                    var created = (await questions.CreateAsync(request, spec, expiry, token)).Question!;
                    created.SessionGeneration.Should().Be(generation);
                    var answered = await questions.SubmitAsync(created.Key, new(["option-0"]), origin, token);
                    return new(created, answered);
                }, Token, origin: origin);
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
        native.Bodies.Should().HaveCount(finalRound);
        controls.VersionReads.Should().Be(questionRound == 2 ? 2 : 1);
        host.IsQuiescent.Should().BeTrue();
        bindings.Should().ContainSingle();
        bindings[0].Revoked.IsCancellationRequested.Should().BeTrue();
        bindings[0].IsAdmitted().Should().BeFalse();
        using var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, origin);
        var page = await service.ReadPageAsync(target.Authority.SessionId, null, 25, Token);
        page.Messages.Should().ContainSingle();
        (page.Messages[0].AssistantResponse?.Body).Should().Be(cancel ? null : "Genuine answer");
        controls.Audits.Should().Contain(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal));
        if (questionRound != 0)
        {
            var question = (await storage.Store.ReadQuestionsAsync(target.Authority.SessionId, Token)).Single();
            question.Proposal.Should().BeNull();
            question.AnswerChannel.Should().Be(origin);
            question.Draft!.Choices.Should().Equal("option-0");
            using var sent = System.Text.Json.JsonDocument.Parse(native.Bodies[^1]);
            var messages = sent.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            messages.Count(message => string.Equals(message.GetProperty("role").GetString(), "system", StringComparison.Ordinal)).Should().Be(1);
            messages.Should().Contain(message => string.Equals(message.GetProperty("role").GetString(), "user", StringComparison.Ordinal)
                && message.GetProperty("content").GetString()!.Contains("clarificationAnswer", StringComparison.Ordinal));
        }
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