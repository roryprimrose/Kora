using System.Text;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class HostedClaudeSessionRequestTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, 2)]
    [InlineData(RequestOrigin.ActivatedVoice, 2)]
    [InlineData(RequestOrigin.LocalUi, 0)]
    [InlineData(RequestOrigin.ActivatedVoice, 1)]
    public async Task Complete_real_host_account_runtime_review_and_retention_preserves_original_source(RequestOrigin origin, int approvals)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        await c.InitializeAsync();
        var commands = new SessionRequestCommandService(c.Workspace, c.Conversation, c.Access, f.Time);
        const string body = "  exact original\r\nClaude input  ";
        var review = (await commands.ExecuteAsync(SessionCommand.Parse("session request review active claude -- " + body, "Kora")!,
            origin, () => true, f.Token)).RequestReview!;
        review.Target.Authority.SessionId.Should().Be(f.Request.SessionId);
        review.Origin.Should().Be(origin);
        review.Body.Should().Be(body);
        c.Http.Requests.Should().OnlyContain(request => string.Equals(request.Method, "GET", StringComparison.Ordinal));
        var offers = 0;
        c.Reviews.ClaudeOffered += offered =>
        {
            c.Http.Requests.Count(request => request.Method is "POST").Should().Be(offers);
            Encoding.UTF8.GetString(offered.Body.AsSpan()).Should().Contain("Claude input");
            using var presentation = offered.Presentation();
            c.Reviews.Confirm(offered, ++offers <= approvals, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var confirmation = SessionCommand.Parse("session request confirm", "Kora")!;
        var result = (await commands.ExecuteAsync(confirmation, RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        result.Message.Body.Should().Be(body);
        result.Message.Request.Origin.Should().Be(origin);
        result.Message.Request.SessionId.Should().Be(f.Request.SessionId);
        result.Message.Outcome.Should().Be(approvals == 2 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        result.Message.AssistantResponse?.Body.Should().Be(approvals == 2 ? "genuine exact answer" : null);
        (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, result.Message.Request.RequestId, f.Token))
            .Should().Be(result.Message);
        c.Http.Requests.Count(request => request.Method is "POST").Should().Be(approvals);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ExecuteAsync(confirmation, RequestOrigin.LocalUi, () => true, f.Token));
        c.Turns.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body).And.NotContain("genuine exact answer")
            .And.NotContain(ClaudeAccountBrokerTests.Fixture.Credential);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("local-only")]
    [InlineData("missing-presenter")]
    [InlineData("wrong-service")]
    public async Task Selection_and_ordinary_submit_never_grant_Claude_egress(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        await c.InitializeAsync();
        if (fault is "local-only") { new LocalModelProviderModePreferences(f.Paths).Save(ModelProviderMode.LocalOnly); }
        if (fault is "wrong-service") { new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Copilot, [])); }
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var receipt = fault is "ordinary"
            ? await c.Conversation.SubmitAsync(target, "must not leave host", () => true, f.Token)
            : await c.Conversation.SubmitClaudeAsync(target, "must not leave host", () => true, f.Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        c.Http.Requests.Should().OnlyContain(request => string.Equals(request.Method, "GET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Readable_selector_refuses_ambiguity_and_confirmation_does_not_retarget()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        await c.InitializeAsync();
        var commands = new SessionRequestCommandService(c.Workspace, c.Conversation, c.Access, f.Time);
        var original = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        await c.Workspace.RenameAsync(f.Request.SessionId, original.Authority.Generation, original.Metadata?.Revision.Value ?? 0,
            new("Readable topic"), RequestOrigin.LocalUi, f.Token);
        var command = SessionCommand.Parse("session request review \"Readable topic\" claude -- exact", "Kora")!;
        var review = (await commands.ExecuteAsync(command, RequestOrigin.ActivatedVoice, () => true, f.Token)).RequestReview!;
        var second = await c.Workspace.CreateAsync(new("Another topic"), RequestOrigin.LocalUi, f.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ExecuteAsync(
            SessionCommand.Parse("session request review active claude -- ambiguous", "Kora")!, RequestOrigin.LocalUi, () => true, f.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ExecuteAsync(
            SessionCommand.Parse("session request confirm", "Kora")!, RequestOrigin.LocalUi, () => true, f.Token));
        review.Target.Authority.SessionId.Should().NotBe(second.Authority.SessionId);
        c.Http.Requests.Count(request => request.Method is "POST").Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("rename")]
    [InlineData("expiry")]
    [InlineData("retire")]
    [InlineData("cancel")]
    [InlineData("missing-name")]
    public async Task Readable_review_never_renews_stale_original_authority_or_silently_retargets(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        await c.InitializeAsync();
        var commands = new SessionRequestCommandService(c.Workspace, c.Conversation, c.Access, f.Time);
        var source = true;
        if (fault is "missing-name")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ExecuteAsync(
                SessionCommand.Parse("session request review \"missing topic\" claude -- original", "Kora")!,
                RequestOrigin.ActivatedVoice, () => source, f.Token));
        }
        else
        {
            var review = (await commands.ExecuteAsync(SessionCommand.Parse("session request review active claude -- original", "Kora")!,
                RequestOrigin.ActivatedVoice, () => source, f.Token)).RequestReview!;
            if (fault is "source") { source = false; }
            if (fault is "control") { c.Access.ControlRevision++; }
            if (fault is "rename")
            {
                await c.Workspace.RenameAsync(review.Target.Authority.SessionId, review.Target.Authority.Generation,
                    review.Target.Metadata?.Revision.Value ?? 0, new("changed"), RequestOrigin.LocalUi, f.Token);
            }
            if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
            if (fault is "retire") { commands.Retire(); }
            if (fault is "cancel")
            {
                (await commands.ExecuteAsync(SessionCommand.Parse("session request cancel", "Kora")!,
                    RequestOrigin.LocalUi, () => true, f.Token)).Outcome.Should().Be("cancelled");
            }
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ExecuteAsync(
            SessionCommand.Parse("session request confirm", "Kora")!, RequestOrigin.LocalUi, () => true, f.Token));
        f.Count("conversation_messages").Should().Be(0);
        c.Http.Requests.Count(request => request.Method is "POST").Should().Be(0);
    }

    internal sealed class Composition : IAsyncDisposable
    {
        private readonly InteractionStorageFixture f;
        private readonly AudioControlAdmission admission;
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal SessionWorkspaceService Workspace { get; }
        internal SessionConversationService Conversation { get; private set; } = null!;
        internal ModelTurnHost Turns { get; private set; } = null!;
        internal HostedTransmissionReviewService Reviews { get; }
        internal ClaudeAccountBrokerTests.Fixture Http { get; }
        internal ClaudeAccountBroker Accounts { get; }
        internal ClaudeMessagesRuntime Runtime { get; }
        internal HostBoundary Boundary { get; } = new();
        internal ObservationTime TurnTime { get; }

        internal Composition(InteractionStorageFixture fixture)
        {
            f = fixture; Workspace = WindowsSqliteSessionWorkspaceTests.Service(f, Access);
            TurnTime = new(f.Time);
            admission = new(f.Store, f.Store, new(f.Tasks));
            Http = new() { Time = f.Time };
            Accounts = Http.Create();
            Runtime = new(Accounts, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, Http.CreateHttp);
            Reviews = new(Http, f.Time);
        }
        internal async Task InitializeAsync()
        {
            using (var setup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request))
            { await Accounts.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, f.Token); }
            var catalogue = await Accounts.ObserveAsync(f.Token);
            new LocalModelProviderModePreferences(f.Paths).Save(ModelProviderMode.HostedPreferred);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Claude, [Http.Profile(catalogue)]));
            var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f.Paths), admission, Http);
            mode.Observe();
            var profiles = new ProviderProfileConfigurationService(new LocalProviderProfilePreferences(f.Paths),
                new Catalogue(Accounts, f.Time), admission, Http, f.Time);
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var boundary = Boundary;
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(boundary, new(), new(), new(boundary), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            Turns = new(f.Store, Access, boundary, registry, Http, NullLogger<ModelTurnHost>.Instance, TurnTime,
                [], mode, profiles, f.Store, new ClaudeModelProviderFactory(Accounts, Runtime, Reviews, f.Time,
                    NullLogger<BoundedModelTurnAdapter>.Instance), Reviews);
            Conversation = new(f.Store, f.Store, Workspace, Access, Turns, f.Time, NullLogger<SessionConversationService>.Instance,
                execution: Turns, cancellationAudit: Http, interactions: f.Store);
            Http.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
                ? "{\"input_tokens\":25}" : ClaudeMessagesRuntimeTests.Answer);
        }
        public async ValueTask DisposeAsync()
        {
            if (Turns is not null) { await Turns.DisposeAsync(); }
            await Runtime.DisposeAsync(); await Accounts.DisposeAsync(); await admission.DisposeAsync();
        }
    }
    private sealed class Catalogue(ClaudeAccountBroker account, TimeProvider time) : IProviderCapabilityCatalogue
    {
        public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            var claude = await account.ObserveAsync(cancellationToken);
            var now = time.GetUtcNow();
            return new(LocalProviderInstallation.Missing, Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset)
                .Select(service => service == ProviderService.Claude ? claude
                    : new ProviderCapabilityCatalogue(service, ProviderCatalogueStatus.Unavailable, null, new(1), now, now.AddMinutes(1), [])).ToArray());
        }
    }
    internal sealed class HostBoundary : ICapabilityHostAccess, IApplicationInfo
    {
        public bool IsCurrentHost => true;
        internal int VersionReads { get; private set; }
        public string Version { get { VersionReads++; return "1.0.0"; } }
    }

    internal sealed class ObservationTime(TimeProvider source) : TimeProvider
    {
        private readonly List<ObservationTimer> timers = [];
        public override DateTimeOffset GetUtcNow() => source.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ObservationTimer(callback, state, dueTime);
            timers.Add(timer); return timer;
        }
        internal void FireDeadline()
        {
            var timer = timers.Single(item => item.Due == ModelToolProtocol.Deadline && !item.Disposed);
            timer.Fire();
        }
        private sealed class ObservationTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            internal TimeSpan Due { get; private set; } = due;
            internal bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return !Disposed; }
            internal void Fire() { if (!Disposed) { callback(state); } }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
