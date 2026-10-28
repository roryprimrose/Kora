using System.Text;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Skills;
using Kora.Tools.SessionControl;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Skills;

[Collection("Host tracing")]
public sealed class BundledFixedActionInvocationTests
{
    [Theory]
    [InlineData(FixedSessionAction.Lock)]
    [InlineData(FixedSessionAction.Shutdown)]
    [InlineData(FixedSessionAction.Restart)]
    public async Task Exact_grant_scripts_native_confirmation_join_and_effect_are_ordered_and_one_use(FixedSessionAction action)
    {
        using var fixture = new Fixture(action);
        var result = await fixture.Run(TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(FixedSessionEffectOutcome.RequestAccepted);
        fixture.Events.Should().Equal("grant", "scripts", "prepare", "join", "audit-requested", "effect", "audit-terminal");
        fixture.Platform.Requests.Should().Equal(action);
        fixture.Interaction.Store.Snapshot.Admissions.Should().ContainSingle(item => item.Dispatched);
        fixture.Interaction.Store.Snapshot.Grants.Should().ContainSingle(item => item.Status == OperationGrantStatus.Consumed);
        fixture.Invocation.NativeRequestStarted.Should().BeTrue();
        await fixture.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>();
        fixture.Platform.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("unqualified")]
    [InlineData("missing-invocation")]
    [InlineData("wrong-origin")]
    [InlineData("wrong-context")]
    [InlineData("identity")]
    [InlineData("source")]
    [InlineData("other-action")]
    [InlineData("other-package")]
    [InlineData("deny")]
    [InlineData("forged")]
    [InlineData("grant-revoked")]
    [InlineData("policy")]
    [InlineData("expired")]
    [InlineData("worker-revoked")]
    [InlineData("worker-profile")]
    [InlineData("worker-identity")]
    [InlineData("worker-source")]
    [InlineData("worker-package")]
    [InlineData("worker-failed")]
    [InlineData("receipt-invocation")]
    [InlineData("receipt-action")]
    [InlineData("receipt-digest")]
    [InlineData("receipt-scripts")]
    [InlineData("receipt-join")]
    [InlineData("owner-unjoined")]
    [InlineData("prepare-cancel")]
    [InlineData("prepare-stale")]
    [InlineData("join-failed")]
    [InlineData("join-retired")]
    [InlineData("join-policy")]
    [InlineData("dispatch-audit")]
    [InlineData("dispatch-denied")]
    [InlineData("dispatch-no-admission")]
    [InlineData("dispatch-not-dispatched")]
    public async Task Every_boundary_fault_has_zero_native_effects(string fault)
    {
        using var fixture = new Fixture(fault: fault);
        if (fault is "worker-failed" or "join-failed" or "dispatch-audit")
        { await fixture.Invoking(value => value.Run()).Should().ThrowAsync<IOException>(); }
        else
        { await fixture.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>(); }
        fixture.Platform.Requests.Should().BeEmpty();
        fixture.Events.Should().NotContain("effect");
        fixture.Invocation.NativeRequestStarted.Should().BeFalse();
    }

    [Fact]
    public async Task Ignoring_work_must_physically_join_before_the_native_request()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Join = async () =>
        {
            entered.SetResult();
#pragma warning disable VSTHRD003 // Test-controlled physical join, independent of the UI dispatcher.
            await released.Task;
#pragma warning restore VSTHRD003
        };
        var running = fixture.Run(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Platform.Requests.Should().BeEmpty();
        fixture.Interaction.Store.Snapshot.Admissions.Should().ContainSingle(item => !item.Dispatched);
        released.SetResult();
        (await running).Outcome.Should().Be(FixedSessionEffectOutcome.RequestAccepted);
        fixture.Platform.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancellation_while_ignored_work_joins_does_not_release_or_request_an_effect_early()
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Join = async () =>
        {
            entered.SetResult();
#pragma warning disable VSTHRD003 // Explicit ignored-cancellation owner; release proves physical completion.
            await released.Task;
#pragma warning restore VSTHRD003
        };
        var running = fixture.Run(cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        running.IsCompleted.Should().BeFalse();
        fixture.Platform.Requests.Should().BeEmpty();
        released.SetResult();
        await ((Func<Task>)(async () => await running)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Platform.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Required_audit_failure_after_the_call_is_unconfirmed_and_never_replayed()
    {
        using var fixture = new Fixture(fault: "terminal-audit");
        var result = await fixture.Run(TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(FixedSessionEffectOutcome.Unconfirmed);
        result.AuditConfirmed.Should().BeFalse();
        fixture.Platform.Requests.Should().ContainSingle();
        await fixture.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>();
        fixture.Platform.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("late-review")]
    [InlineData("late-worker")]
    public async Task Authority_retiring_after_dispatch_is_denied_without_a_native_call(string fault)
    {
        using var fixture = new Fixture(fault: fault);
        (await fixture.Run(TestContext.Current.CancellationToken)).Outcome.Should().Be(FixedSessionEffectOutcome.Denied);
        fixture.Platform.Requests.Should().BeEmpty();
        fixture.Invocation.NativeRequestStarted.Should().BeTrue();
        fixture.Interaction.Store.Snapshot.Admissions.Should().ContainSingle(item => item.Dispatched);
    }

    [Fact]
    public void Binding_requires_a_package_and_binds_the_complete_resource_and_closed_effect()
    {
        FluentActions.Invoking(() => BundledActionBinding.Create(null!, new string('a', 64), new string('b', 64)))
            .Should().Throw<ArgumentNullException>();
        var package = Package();
        var first = BundledActionBinding.Create(new(package, FixedSessionAction.Lock), new string('a', 64), new string('b', 64));
        first.SourcePartition.Should().Be("bundled");
        first.TrackedContentDigest.Should().Be(package.ScriptSetDigest);
        first.DeclaredResourceDigest.Should().Be(package.DeclaredResourceDigest);
        first.Should().NotBe(BundledActionBinding.Create(new(package, FixedSessionAction.Restart), new string('a', 64), new string('b', 64)));
    }

    private static SkillPackageSnapshot Package(string entry = "Invoke-ExactFixedAction") =>
        new SkillPackageCatalogue(["Kora.Manifest"],
        [
            new("manifest.json", "Kora.Manifest", """
                {"schemaVersion":1,"id":"test.fixed","version":"1","name":"Fixed test package","description":"Test",
                "action":"session.lock","entryPoint":"entry.ps1","parameterContract":"fixed.v1","runtime":"powershell7",
                "dependencies":["test"],"helperLoadOrder":[],"files":[
                {"name":"skill.md","resourceId":"Kora.Instructions","kind":"instructions"},
                {"name":"entry.ps1","resourceId":"Kora.Entry","kind":"entry"}]}
                """u8.ToArray()),
            new("skill.md", "Kora.Instructions", "# Test"u8.ToArray()),
            new("entry.ps1", "Kora.Entry", Encoding.UTF8.GetBytes(entry)),
        ]).Packages[0];

    private sealed class Fixture : IDisposable
    {
        private readonly string fault;
        private readonly BundledFixedScript script;
        private BundledFixedScript source;
        private bool current = true;
        private bool preparedCurrent = true;
        private readonly HostRequest invocationRequest;
        internal Fixture(FixedSessionAction action = FixedSessionAction.Lock, string fault = "")
        {
            this.fault = fault;
            if (fault is "wrong-origin") { Interaction.Rebind(origin: RequestOrigin.HostSystem); }
            invocationRequest = fault is "missing-invocation"
                ? new(Interaction.Request.RequestId, Interaction.Request.SessionId, Interaction.Request.TaskId, Interaction.Request.Origin)
                : Interaction.Request;
            script = new(Package(), action);
            source = script;
            if (fault is "unqualified") { Worker.QualifiedImplementationDigest = null; }
            if (fault is "identity") { Platform.CurrentIdentityDigest = null; }
            if (fault is "source") { current = false; }
            if (fault is "other-action") { source = new(script.Package, FixedSessionAction.Restart); }
            if (fault is "other-package") { source = new(Package("changed"), action); }
            Worker.Run = RunScripts;
            Audit.Events = Events;
            Platform.Events = Events;
            Audit.FailTerminal = fault is "terminal-audit";
            var broker = new FixedSessionActionBroker(Platform, Audit, NullLogger<FixedSessionActionBroker>.Instance);
            Invocation = new(script, Worker, broker, Interaction.Authorization, Present,
                () => source, invocationRequest, new(1), () => current, Prepare, Interaction.Time);
        }
        internal InteractionFixture Interaction { get; } = new();
        internal Worker Worker { get; } = new();
        internal Platform Platform { get; } = new();
        internal Audit Audit { get; } = new();
        internal List<string> Events { get; } = [];
        internal BundledFixedActionInvocation Invocation { get; }
        internal Func<Task>? Join { get; set; }

        internal Task<FixedSessionEffectResult> Run() => Run(TestContext.Current.CancellationToken);

        internal async Task<FixedSessionEffectResult> Run(CancellationToken token)
        {
            using var activity = HostActivity.BeginRoot(fault is "wrong-context"
                ? HostRequest.Create(RequestOrigin.LocalUi) : invocationRequest, HostActivityLayer.Application, HostOperation.Tool);
            return await Invocation.ExecuteAsync(token);
        }

        private async Task<HostInteractionDecision> Present(HostOperationProposal proposal, HostRevision generation,
            Func<bool> admitted, CancellationToken token)
        {
            Events.Add("grant");
            admitted().Should().BeTrue();
            generation.Should().Be(new HostRevision(1));
            Interaction.Store.Change(snapshot => snapshot with { Proposal = proposal });
            if (fault is "deny") { return new(HostInteractionOutcome.Denied, "user-denied"); }
            var spec = new QuestionSpec("Grant this exact package?", QuestionKind.SingleChoice,
                [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")]);
            var presented = await Interaction.Authorization.RequestGrantAsync(Interaction.Request, spec, token);
            var approved = await Interaction.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), RequestOrigin.LocalUi, token);
            var consumed = await Interaction.Authorization.ConsumeAsync(Interaction.Request, approved.Grant!.Id, approved.Grant.Revision, token);
            if (fault is "forged") { return consumed with { Admission = consumed.Admission! with { Id = Guid.NewGuid() } }; }
            if (fault is "grant-revoked") { RetireGrant(); }
            if (fault is "policy") { RetirePolicy(); }
            if (fault is "expired") { Interaction.Time.Now = proposal.ExpiresAt; }
            return consumed;
        }

        private BundledScriptReceipt RunScripts(BundledFixedScript actual, HostRequest request)
        {
            Events.Add("scripts");
            actual.Should().BeSameAs(script);
            if (fault is "worker-failed") { throw new IOException("Synthetic worker failure."); }
            if (fault is "worker-revoked") { RetireGrant(); }
            if (fault is "worker-profile") { Worker.QualifiedImplementationDigest = new string('d', 64); }
            if (fault is "worker-identity") { Platform.CurrentIdentityDigest = new string('d', 64); }
            if (fault is "worker-source") { current = false; }
            if (fault is "worker-package") { source = new(Package("changed"), script.Action); }
            if (fault is "owner-unjoined") { Worker.IsQuiescent = false; }
            return new(fault is "receipt-invocation" ? HostRequest.Create(RequestOrigin.LocalUi) : request,
                fault is "receipt-action" ? FixedSessionAction.Restart : script.Action,
                fault is "receipt-digest" ? new string('0', 64) : script.Package.DeclaredResourceDigest,
                fault is not "receipt-scripts", fault is not "receipt-join");
        }

        private Task<ApplicationLifecycleShutdown?> Prepare(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Events.Add("prepare");
            if (fault is "prepare-cancel") { return Task.FromResult<ApplicationLifecycleShutdown?>(null); }
            if (fault is "prepare-stale") { preparedCurrent = false; }
            return Task.FromResult<ApplicationLifecycleShutdown?>(new(() => preparedCurrent, async () =>
            {
                Events.Add("join");
                if (Join is not null) { await Join(); }
                if (fault is "join-failed") { throw new IOException("Synthetic physical join failure."); }
                if (fault is "join-retired") { preparedCurrent = false; }
                if (fault is "join-policy") { RetirePolicy(); }
                if (fault is "dispatch-audit") { Interaction.Store.FailAudit = true; }
                if (fault is "dispatch-denied" or "dispatch-no-admission" or "dispatch-not-dispatched"
                    or "late-review" or "late-worker")
                {
                    var transitions = 0;
                    Interaction.Store.BeforeTransition = () =>
                    {
                        if (++transitions != 2) { return; }
                        if (fault is "late-review") { preparedCurrent = false; }
                        else if (fault is "late-worker") { Worker.IsQuiescent = false; }
                        else
                        {
                            Interaction.Store.RefusedDecision = new(
                                fault is "dispatch-denied" ? HostInteractionOutcome.Denied : HostInteractionOutcome.Consumed,
                                "synthetic-dispatch-refusal", Admission: fault is "dispatch-not-dispatched"
                                    ? Interaction.Store.Snapshot.Admissions.Single() : null);
                        }
                    };
                }
            }, static () => { }));
        }

        private void RetireGrant() => Interaction.Store.Change(snapshot =>
            snapshot with { Grants = [.. snapshot.Grants.Select(grant => grant with { Status = OperationGrantStatus.Revoked })] });
        private void RetirePolicy() => Interaction.Store.Change(snapshot =>
            snapshot with { Policy = snapshot.Policy with { OtherMandatoryGatesSatisfied = false } });
        public void Dispose() => Interaction.Dispose();
    }

    private sealed class Worker : IBundledScriptWorker
    {
        public string? QualifiedImplementationDigest { get; set; } = new string('a', 64);
        public bool IsQuiescent { get; set; } = true;
        internal Func<BundledFixedScript, HostRequest, BundledScriptReceipt> Run { get; set; } =
            (_, _) => throw new InvalidOperationException("No fake worker behavior supplied.");
        public Task<BundledScriptReceipt> RunAsync(BundledFixedScript script, HostRequest invocation,
            Func<bool> current, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current().Should().BeTrue();
            return Task.FromResult(Run(script, invocation));
        }
    }

    private sealed class Platform : IFixedSessionEffect
    {
        public string? CurrentIdentityDigest { get; set; } = new string('b', 64);
        internal List<string> Events { get; set; } = [];
        internal List<FixedSessionAction> Requests { get; } = [];
        public FixedSessionEffectResult Request(FixedSessionAction action)
        {
            Events.Add("effect");
            Requests.Add(action);
            return new(FixedSessionEffectOutcome.RequestAccepted);
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<string> Events { get; set; } = [];
        internal bool FailTerminal { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (FailTerminal && auditEvent.Outcome != SecurityAuditOutcome.Requested)
            { throw new IOException("Synthetic terminal audit failure."); }
            Events.Add(auditEvent.Outcome == SecurityAuditOutcome.Requested ? "audit-requested" : "audit-terminal");
        }
    }
}