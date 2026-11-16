using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class WindowsOllamaEvaluationSourceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public WindowsOllamaEvaluationSourceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Native_review_and_metadata_revalidation_cannot_admit_inference_without_separate_confirmation()
    {
        await using var f = new Fixture();
        using var root = Native();
        (await f.Source.ReadAsync(Token)).Should().BeNull();
        var review = await f.Source.ObserveAsync(() => f.Live, Token);
        (await f.Source.ReadAsync(Token)).Should().BeNull();
        await f.Source.RevalidateAsync(review, () => f.Live, Token);
        (await f.Source.ReadAsync(Token)).Should().BeNull();
        f.Source.Admit(review, () => f.Live, () => f.Live, Token);
        var receipt = (await f.Source.ReadAsync(Token))!;
        receipt.IsEvaluation.Should().BeTrue();
        receipt.QualificationEvidence.Should().BeNull();
        receipt.Runtime.Should().BeSameAs(f.Subject.Runtime);
        receipt.Catalogue.Models.Single().Label.Should().Contain("not production-qualified");
        receipt.Model.Should().Be(WindowsOllamaSetupService.Model);
        receipt.RuntimeVersion.Should().Be(WindowsOllamaSetupService.PackageVersion);
        receipt.ModelDigest.Should().Be(WindowsOllamaSetupService.ModelDigest[7..]);
        (await f.Source.ReadAsync(Token)).Should().BeSameAs(receipt);
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.ActionId == "model.ollama.evaluation");
        f.Audit.Events.Select(item => item.CorrelationId).Distinct().Should().ContainSingle();
        var model = receipt.Catalogue.Models.Single();
        var tuple = new ProviderProfileTuple(ProviderService.Local, receipt.Catalogue.Account!, receipt.Catalogue.Revision,
            model.Id, model.Reasoning.Single().Id, model.Context.Single().Id);
        var factory = new OllamaModelProviderFactory(f.Resolver, f.Clock, NullLoggerFactory.Instance,
            _ => throw new InvalidOperationException("Admission and selection do not run inference."));
        var registration = (await factory.ResolveAsync(model.Selection, tuple, Token))!;
        registration.Evidence.Should().Be(ModelQualificationGate.UserReviewedOllamaEvaluation | ModelQualificationGate.IntegratedHost);
        registration.IsQualified(f.Clock.Now).Should().BeTrue();
        f.Source.RevokeNative(() => f.Live);
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
        registration.IsQualified(f.Clock.Now).Should().BeFalse();
        (await factory.ResolveAsync(model.Selection, tuple, Token)).Should().BeNull();
        f.Audit.Events.TakeLast(2).Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.TakeLast(2).Should().OnlyContain(item => item.ActionId == "model.ollama.evaluation.revoke");
    }

    [Theory]
    [InlineData("missing-review")]
    [InlineData("missing-revalidation")]
    [InlineData("stale")]
    [InlineData("expired-review")]
    [InlineData("expired-revalidation")]
    [InlineData("cancelled")]
    [InlineData("privacy")]
    [InlineData("audit-request")]
    [InlineData("audit-terminal")]
    [InlineData("revoke-at-audit")]
    [InlineData("privacy-at-audit")]
    public async Task Failed_or_stale_confirmation_never_advertises_a_profile_or_retains_authority(string fault)
    {
        await using var f = new Fixture();
        using var root = Native();
        var reviewed = await f.Source.ObserveAsync(() => true, Token);
        if (fault is not "missing-revalidation") { await f.Source.RevalidateAsync(reviewed, () => true, Token); }
        if (fault is "missing-review") { f.Source.DiscardPendingReview(); }
        if (fault is "stale") { _ = await f.Source.ObserveAsync(() => true, Token); }
        if (fault is "expired-review") { f.Clock.Now = f.Clock.Now.AddMinutes(5); }
        if (fault is "expired-revalidation") { f.Clock.Now = f.Clock.Now.AddSeconds(30); }
        if (fault is "privacy") { f.Live = false; }
        if (fault is "audit-request") { f.Audit.Failure = SecurityAuditOutcome.Requested; }
        if (fault is "audit-terminal") { f.Audit.Failure = SecurityAuditOutcome.Succeeded; }
        if (fault is "revoke-at-audit") { f.Audit.OnSuccess = f.Source.Revoke; }
        if (fault is "privacy-at-audit") { f.Audit.OnSuccess = () => f.Live = false; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancelled") { await cancellation.CancelAsync(); }
        Action confirm = () => f.Source.Admit(reviewed, () => true, () => f.Live, cancellation.Token);
        confirm.Should().Throw<Exception>();
        f.Source.HasAdmission.Should().BeFalse();
        (await f.Source.ReadAsync(Token)).Should().BeNull();
        (await f.Resolver.ObserveAsync(Token)).For(ProviderService.Local).Models.Should().BeEmpty();
        f.Source.IsQuiescent.Should().BeTrue();
        if (fault is not ("revoke-at-audit" or "privacy-at-audit"))
        { f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded); }
        f.Audit.Events.Last().Outcome.Should().Be(fault is "cancelled" ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("privacy")]
    [InlineData("revoked")]
    [InlineData("process")]
    [InlineData("account")]
    [InlineData("session")]
    [InlineData("model")]
    [InlineData("read-failure")]
    public async Task Live_receipts_cannot_survive_authority_or_exact_subject_loss(string reason)
    {
        await using var f = new Fixture();
        using var root = Native();
        var review = await f.Source.ObserveAsync(() => f.Live, Token);
        await f.Source.RevalidateAsync(review, () => f.Live, Token);
        f.Source.Admit(review, () => f.Live, () => f.Live, Token);
        var receipt = (await f.Source.ReadAsync(Token))!;
        if (reason is "expiry") { f.Clock.Now = f.Clock.Now.AddHours(1); }
        if (reason is "privacy") { f.Live = false; }
        if (reason is "revoked") { f.Source.Revoke(); }
        if (reason is "process" or "model") { f.Current = false; }
        if (reason is "account") { f.Subject = f.Subject with { Account = new("foreign") }; }
        if (reason is "session") { f.Subject = f.Subject with { WindowsSessionId = 2 }; }
        if (reason is "read-failure") { f.OnCheck = _ => throw new IOException("synthetic-metadata-failure"); }
        if (reason is "read-failure")
        { await Assert.ThrowsAsync<IOException>(() => f.Source.ReadAsync(Token)); }
        else { (await f.Source.ReadAsync(Token)).Should().BeNull(); }
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
        receipt.IsCurrent(f.Clock.Now).Should().BeFalse();
        (await f.Resolver.ObserveAsync(Token)).For(ProviderService.Local).Status.Should().Be(ProviderCatalogueStatus.Unavailable);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("disposed")]
    [InlineData("cancelled")]
    [InlineData("privacy")]
    public async Task Late_observation_is_not_review_authority_and_owns_physical_work_until_it_joins(string loss)
    {
        await using var f = new Fixture();
        using var root = Native();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnObserve = async _ => { entered.SetResult(); await release.Task; };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var observing = f.Source.ObserveAsync(() => f.Live, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        Task? disposing = null;
        if (loss is "revoked") { f.Source.Revoke(); }
        if (loss is "disposed") { disposing = f.Source.DisposeAsync().AsTask(); }
        if (loss is "cancelled") { await cancellation.CancelAsync(); }
        if (loss is "privacy") { f.Live = false; }
        f.Source.IsQuiescent.Should().BeFalse();
        observing.IsCompleted.Should().BeFalse();
        disposing?.IsCompleted.Should().BeFalse();
        release.SetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => observing);
        if (disposing is not null) { await disposing; await f.Source.DisposeAsync(); }
        f.Source.IsQuiescent.Should().BeTrue();
        f.Source.HasAdmission.Should().BeFalse();
        (await f.Source.ReadAsync(Token)).Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task Non_native_input_cannot_observe_or_enable_evaluation(RequestOrigin origin)
    {
        await using var f = new Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Windows, HostOperation.Request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Source.ObserveAsync(() => true, Token));
        f.Observations.Should().Be(0);
        (await f.Source.ReadAsync(Token)).Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Invocation_bound_native_input_cannot_observe_evaluation()
    {
        await using var f = new Fixture();
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, new(Guid.NewGuid()));
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Windows, HostOperation.Request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Source.ObserveAsync(() => true, Token));
        f.Observations.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoked_metadata_read_stays_owned_until_the_physical_observer_returns()
    {
        await using var f = new Fixture();
        using var root = Native();
        var reviewed = await f.Source.ObserveAsync(() => f.Live, Token);
        await f.Source.RevalidateAsync(reviewed, () => f.Live, Token);
        f.Source.Admit(reviewed, () => f.Live, () => f.Live, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnCheck = async _ => { entered.SetResult(); await release.Task; };
        var reading = f.Source.ReadAsync(Token);
        await entered.Task.WaitAsync(Token);
        f.Source.Revoke();
        reading.IsCompleted.Should().BeFalse();
        f.Source.IsQuiescent.Should().BeFalse();
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        f.Source.IsQuiescent.Should().BeTrue();
        f.Source.HasAdmission.Should().BeFalse();
    }

    [Fact]
    public async Task Revocation_remains_closed_when_its_audit_write_fails()
    {
        await using var f = new Fixture();
        using var root = Native();
        var reviewed = await f.Source.ObserveAsync(() => f.Live, Token);
        await f.Source.RevalidateAsync(reviewed, () => f.Live, Token);
        f.Source.Admit(reviewed, () => f.Live, () => f.Live, Token);
        var receipt = (await f.Source.ReadAsync(Token))!;
        f.Audit.Failure = SecurityAuditOutcome.Requested;
        Action revoke = () => f.Source.RevokeNative(() => f.Live);
        revoke.Should().Throw<IOException>();
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
        (await f.Source.ReadAsync(Token)).Should().BeNull();
    }

    [Fact]
    public async Task Stale_review_failure_and_old_view_cleanup_cannot_revoke_a_newly_confirmed_receipt()
    {
        await using var f = new Fixture();
        using var root = Native();
        var stale = await f.Source.ObserveAsync(() => true, Token);
        var current = await f.Source.ObserveAsync(() => true, Token);
        await f.Source.RevalidateAsync(current, () => true, Token);
        var receipt = f.Source.Admit(current, () => true, () => true, Token);
        Action staleConfirm = () => f.Source.Admit(stale, () => true, () => true, Token);
        staleConfirm.Should().Throw<InvalidOperationException>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Source.RevalidateAsync(stale, () => true, Token));
        f.Source.DiscardPendingReview(stale);
        (await f.Source.ReadAsync(Token)).Should().BeSameAs(receipt);
        await using var other = new Fixture();
        var foreignReview = await other.Source.ObserveAsync(() => true, Token);
        await other.Source.RevalidateAsync(foreignReview, () => true, Token);
        var foreignReceipt = other.Source.Admit(foreignReview, () => true, () => true, Token);
        f.Source.Revoke(foreignReceipt);
        receipt.IsCurrent(f.Clock.Now).Should().BeTrue();
        f.Source.Revoke(receipt);
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Disposal_callback_failure_still_closes_authority_and_joins_the_physical_read()
    {
        var f = new Fixture();
        using var root = Native();
        var reviewed = await f.Source.ObserveAsync(() => f.Live, Token);
        await f.Source.RevalidateAsync(reviewed, () => f.Live, Token);
        var receipt = f.Source.Admit(reviewed, () => f.Live, () => f.Live, Token);
        Task? reentered = null;
        using var registration = receipt.Revoked.Register(() =>
        {
            reentered = f.Source.DisposeAsync().AsTask();
            throw new IOException("synthetic-callback-failure");
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnCheck = async _ => { entered.SetResult(); await release.Task; };
        var reading = f.Source.ReadAsync(Token);
        await entered.Task.WaitAsync(Token);
        var disposing = f.Source.DisposeAsync().AsTask();
        try
        {
            disposing.IsCompleted.Should().BeFalse();
            reentered.Should().BeSameAs(disposing);
            f.Source.HasAdmission.Should().BeFalse();
            f.Source.IsQuiescent.Should().BeFalse();
        }
        finally { release.SetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        await Assert.ThrowsAsync<AggregateException>(() => disposing);
        await Assert.ThrowsAsync<AggregateException>(() => f.Source.DisposeAsync().AsTask());
        f.Source.IsQuiescent.Should().BeTrue();
    }

    private static HostActivity Native() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);

    private sealed class Fixture : IWindowsOllamaEvaluationObservation, ILocalProviderInstallationObservation, IAsyncDisposable
    {
        internal Clock Clock { get; } = new();
        internal Audit Audit { get; } = new();
        internal WindowsOllamaAdmissionSubject Subject { get; set; }
        internal WindowsOllamaEvaluationSource Source { get; }
        internal WindowsOllamaRuntimeResolver Resolver { get; }
        internal bool Live { get; set; } = true;
        internal bool Current { get; set; } = true;
        internal int Observations { get; private set; }
        internal Func<CancellationToken, Task>? OnObserve { get; set; }
        internal Func<CancellationToken, Task>? OnCheck { get; set; }
        internal Fixture()
        {
            Subject = new(new(4242, Clock.Now.AddMinutes(-1), @"C:\fake\ollama.exe",
                WindowsOllamaRuntimeResolverTests.Digest), new("windows.fixture"), 1);
            Source = new(this, Clock, Audit, NullLogger<WindowsOllamaEvaluationSource>.Instance);
            Resolver = new(Source, this, Clock, NullLogger<WindowsOllamaRuntimeResolver>.Instance,
                (runtime, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(Current && runtime == Subject.Runtime); });
        }
        public async Task<WindowsOllamaAdmissionSubject?> ObserveEvaluationAsync(CancellationToken cancellationToken)
        {
            Observations++;
            if (OnObserve is not null) { await OnObserve(cancellationToken); }
            return Current ? Subject : null;
        }
        public async Task<bool> IsEvaluationCurrentAsync(WindowsOllamaAdmissionSubject subject, CancellationToken cancellationToken)
        {
            if (OnCheck is not null) { await OnCheck(cancellationToken); }
            return Current && subject == Subject;
        }
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LocalProviderInstallation.Installed);
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal SecurityAuditOutcome? Failure { get; set; }
        internal Action? OnSuccess { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            if (Failure == auditEvent.Outcome) { throw new IOException("synthetic-audit-failure"); }
            Events.Add(auditEvent);
            if (auditEvent.Outcome == SecurityAuditOutcome.Succeeded) { OnSuccess?.Invoke(); }
        }
    }
}
