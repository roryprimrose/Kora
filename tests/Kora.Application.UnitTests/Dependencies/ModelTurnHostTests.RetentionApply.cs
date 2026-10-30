using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(SessionRetentionApplyEffect.DeadlinesOnly)]
    [InlineData(SessionRetentionApplyEffect.Archive)]
    [InlineData(SessionRetentionApplyEffect.Delete)]
    public async Task NativeRetentionReviewNeedsSeparateSingleUseDecisionAndNeverRunsModel(SessionRetentionApplyEffect effect)
    {
        using var f = new Fixture();
        await using var environment = new RetentionEnvironment(f, effect);
        using var root = f.Root();
        var review = await environment.Service.ReviewAsync(f.ApplyObservation, () => true, Token);
        environment.Service.IsPending(review).Should().BeTrue();
        f.ApplyCalls.Should().Be(0);
        var receipt = await environment.Service.ConfirmAsync(review, () => true, Token);
        receipt.Effect.Should().Be(effect);
        f.ApplyCalls.Should().Be(1);
        environment.Retired.Should().Be(effect == SessionRetentionApplyEffect.Delete);
        environment.Archived.Should().Be(effect == SessionRetentionApplyEffect.Archive);
        environment.Service.IsPending(review).Should().BeFalse();
        await environment.Service.Invoking(s => s.ConfirmAsync(review, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("discard")]
    [InlineData("replace")]
    [InlineData("control")]
    [InlineData("original")]
    [InlineData("current")]
    [InlineData("policy")]
    [InlineData("cancel")]
    [InlineData("native")]
    [InlineData("preflight")]
    [InlineData("commit")]
    public async Task StaleOrCancelledRetentionDecisionCannotWriteOrReuseReview(string fault)
    {
        using var f = new Fixture();
        await using var environment = new RetentionEnvironment(f, SessionRetentionApplyEffect.DeadlinesOnly);
        using var root = f.Root();
        var original = true;
        var review = await environment.Service.ReviewAsync(f.ApplyObservation, () => original, Token);
        environment.Service.Discard(review with { Id = Guid.NewGuid() });
        if (fault is "discard") { environment.Service.Discard(review); environment.Service.Discard(review); }
        if (fault is "replace") { await environment.Service.ReviewAsync(f.ApplyObservation, () => true, Token); }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "original") { original = false; }
        if (fault is "policy") { environment.Configuration.Observe(); }
        if (fault is "preflight") { f.ApplyPreflightFailure = new IOException("Unconfirmed source"); }
        if (fault is "commit") { f.ApplyCommitFailure = new IOException("Unknown commit"); }
        using var cancellation = new CancellationTokenSource();
        if (fault is "cancel") { cancellation.Cancel(); }
        using var changedOrigin = fault is "native"
            ? HostActivity.BeginRoot(new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.ActivatedVoice),
                HostActivityLayer.Application, HostOperation.Request)
            : null;
        await environment.Service.Invoking(s => s.ConfirmAsync(review, () => fault is not "current", cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.ApplyCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("policy")]
    [InlineData("after-read")]
    [InlineData("selection")]
    [InlineData("policy-revision")]
    [InlineData("policy-settings")]
    [InlineData("overlap")]
    [InlineData("native-selection")]
    public async Task UnavailableOrChangedNativeReviewNeverPublishesApplyAuthority(string fault)
    {
        using var f = new Fixture();
        await using var environment = new RetentionEnvironment(f, SessionRetentionApplyEffect.DeadlinesOnly);
        using var root = f.Root();
        if (fault is "private") { f.CanControl = false; }
        if (fault is "policy") { environment.Preferences.Value = new(2, 60); }
        if (fault is "after-read") { f.ApplyAfterReview = () => f.ControlRevision++; }
        var selected = true;
        if (fault is "native-selection") { f.ApplyAfterReview = () => selected = false; }
        if (fault is "selection") { f.ApplyReviewFactory = value => value with { Before = value.Before with { Generation = new(99) } }; }
        if (fault is "policy-revision") { f.ApplyReviewFactory = value => value with { PolicyRevision = 99 }; }
        if (fault is "policy-settings") { f.ApplyReviewFactory = value => value with { Settings = new(2, 60) }; }
        if (fault is "overlap")
        {
            f.ApplyAfterReview = () =>
            {
                f.ApplyAfterReview = null;
                environment.Service.ReviewAsync(f.ApplyObservation, () => true, Token).IsCompletedSuccessfully.Should().BeTrue();
            };
        }
        await environment.Service.Invoking(s => s.ReviewAsync(f.ApplyObservation, () => selected && fault is not "private", Token))
            .Should().ThrowAsync<Exception>();
        f.ApplyCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(SessionRetentionApplyEffect.Archive)]
    [InlineData(SessionRetentionApplyEffect.Delete)]
    public async Task RetentionEffectRetiresExactNoticeStateWithNoObserverRequired(SessionRetentionApplyEffect effect)
    {
        using var f = new Fixture();
        await using var environment = new RetentionEnvironment(f, effect, notifications: false);
        using var events = new Kora.Application.UnitTests.Interaction.LocalEventBrokerTests.Fixture { Session = f.Session.SessionId };
        events.Events = [events.Event()];
        await using var broker = events.Create();
        await events.Observe(broker);
        environment.Workspace.BindLocalEvents(broker);
        using var root = f.Root();
        var review = await environment.Service.ReviewAsync(f.ApplyObservation, () => true, Token);
        await environment.Service.ConfirmAsync(review, () => true, Token);
        events.Saved!.Receipts.Should().BeEmpty();
    }

    [Fact]
    public async Task RetentionReviewCannotConfirmLiveResourcesOrDispatchDuringTheIdleGuard()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<Kora.Application.Dependencies.ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = conversation.SubmitAsync(target, "Running original", () => true, Token);
        await conversation.Invoking(s => s.WithIdleRetentionAsync(f.Session.SessionId, () => Task.FromResult(true), Token))
            .Should().ThrowAsync<InvalidOperationException>();
        pending.SetResult(LateAnswer());
        await submission;
        await conversation.WithIdleRetentionAsync(f.Session.SessionId, async () =>
        {
            await conversation.Invoking(s => s.SubmitAsync(target, "not admitted", () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            return true;
        }, Token);
    }

    private sealed class RetentionEnvironment : IAsyncDisposable
    {
        private readonly DiagnosticRetentionAdmission admission;
        private readonly SessionConversationService conversation;
        internal RetentionEnvironment(Fixture f, SessionRetentionApplyEffect effect, bool notifications = true)
        {
            var store = new AudioControlTestStore();
            admission = new(store, store, new(store));
            Configuration = new(Preferences, Policy, admission, f);
            Configuration.Observe();
            f.ApplyPolicyRevision = Policy.Observe().Revision;
            f.ApplyEffect = effect;
            Workspace = new(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
            if (notifications)
            {
                Workspace.SessionRetired += _ => Retired = true;
                Workspace.SessionLifecycleChanged += _ => Archived = true;
            }
            conversation = f.ConversationService(new TestAdmission(Kora.Core.Dependencies.ModelTurnOutcome.Unavailable));
            Service = new(Workspace, conversation, Configuration, f);
        }
        internal bool Retired { get; private set; }
        internal bool Archived { get; private set; }
        internal RetentionPreferences Preferences { get; } = new();
        internal SessionRetentionPolicy Policy { get; } = new();
        internal SessionRetentionConfigurationService Configuration { get; }
        internal SessionRetentionApplyService Service { get; }
        internal SessionWorkspaceService Workspace { get; }
        public async ValueTask DisposeAsync() { await conversation.DisposeAsync(); await admission.DisposeAsync(); }
    }

    private sealed class RetentionPreferences : ISessionRetentionPreferences
    {
        internal SessionRetentionSettings? Value { get; set; }
        public SessionRetentionSettings? Load() => Value;
        public SessionRetentionSettings? ReadBack() => Value;
        public void BeginWrite() => throw new NotSupportedException();
        public void Save(SessionRetentionSettings value) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
        public void ConfirmWrite() => throw new NotSupportedException();
    }

    private sealed partial class Fixture : ISessionRetentionApplyStore
    {
        internal SessionRetentionApplyEffect ApplyEffect { get; set; }
        internal int ApplyCalls { get; private set; }
        internal long ApplyPolicyRevision { get; set; }
        internal Action? ApplyAfterReview { get; set; }
        internal Func<SessionRetentionApplyReview, SessionRetentionApplyReview>? ApplyReviewFactory { get; set; }
        internal Exception? ApplyPreflightFailure { get; set; }
        internal Exception? ApplyCommitFailure { get; set; }
        internal SessionRetentionObservation ApplyObservation =>
            new(new(Session.SessionId, Time.Now, Time.Now.AddDays(10), Time.Now.AddDays(60), false, false),
                Session.Generation, 0, false);
        public ValueTask<SessionRetentionApplyReview> ReviewRetentionApplyAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            var review = new SessionRetentionApplyReview(Guid.NewGuid(), ApplyObservation,
                ApplyObservation.State with
                {
                    ArchiveDue = ApplyEffect == SessionRetentionApplyEffect.Archive ? Time.Now : Time.Now.AddDays(1),
                    DeleteDue = ApplyEffect == SessionRetentionApplyEffect.Delete ? Time.Now : Time.Now.AddDays(30),
                }, SessionRetentionSettings.Default, ApplyPolicyRevision,
                new(Guid.NewGuid(), new(Session, null), "source", 0, 0, 0, 0), Time.Now, "head", "snapshot");
            ApplyAfterReview?.Invoke();
            return ValueTask.FromResult(ApplyReviewFactory?.Invoke(review) ?? review);
        }
        public ValueTask ValidateRetentionApplyAsync(SessionRetentionApplyReview review, CancellationToken token)
        {
            if (ApplyPreflightFailure is { } failure) { throw failure; }
            return ValueTask.CompletedTask;
        }
        public ValueTask<SessionRetentionApplyReceipt> ApplyReviewedRetentionAsync(HostRequest request,
            SessionRetentionApplyReview review, Func<bool> admitted, CancellationToken token)
        {
            if (ApplyCommitFailure is { } failure) { throw failure; }
            if (!admitted()) { throw new InvalidOperationException("No current native admission."); }
            ApplyCalls++;
            Task = Task! with { Task = Task.Task.Next(HostTaskState.Succeeded) };
            return ValueTask.FromResult(new SessionRetentionApplyReceipt(review, review.Before, ApplyEffect));
        }
    }
}
