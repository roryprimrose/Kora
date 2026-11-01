using AwesomeAssertions;
using System.Text;
using Kora.Application.Tools;
using Kora.Application.Skills;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedInstructionOnlySelectionCannotMislabelOrRunAnOwnedToolWorkflow(bool observation)
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        await using var shared = new SharedSkillHarness(f);
        if (observation) { DeclareObservationWorkflow(owned); } else { DeclareOwnedWebTool(owned); }
        var inventory = new NamedSkillCatalogue(owned.Store, owned.Saved, shared.Discovery, f, new NamedUsePolicy(), f.Time);
        using var candidates = await InspectNamed(inventory, "Example", owned.Authority, owned.Eligible);
        candidates.Choices.Should().ContainSingle().Which.IsSupported.Should().BeFalse();
        var choose = () => candidates.Choose(1);
        choose.Should().Throw<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnusedObservationReviewCanBeExplicitlyDisposedWithoutLeavingAuthority(bool enable)
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        DeclareObservationWorkflow(owned);
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        var use = await flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        if (enable) { await flow.Enable(use, Token); }
        use.Dispose();
        use.State.Should().Be(ObservationWorkflowState.Cancelled);
        use.SourceText.Should().BeEmpty();
        use.Dispose();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("revoked-on-target-read")]
    [InlineData("foreign-profile")]
    public async Task ObservationReviewRejectsUnavailableTargetAndRetiresItsPartiallyCapturedSource(string fault)
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        DeclareObservationWorkflow(owned);
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        if (fault is "revoked-on-target-read") { f.OnRead = () => owned.Open = false; }
        if (fault is "foreign-profile") { f.ProfileRead = () => new(f.Session with { Generation = new(99) }, 0, f.SessionOverrides, false); }
        var target = new Kora.Core.Storage.SessionWorkspaceEntry(fault is "inactive" ? f.Session with { IsActive = false } : f.Session, null);
        await flow.Invoking(value => value.Review("example", "1.0.0", target, "original", owned.Authority, owned.Eligible, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("original")]
    [InlineData("control")]
    [InlineData("cancel")]
    public async Task StepAuditCannotBridgeAClosedFinalIndependentDispatchGate(string fault)
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        DeclareObservationWorkflow(owned);
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        ObservationWorkflowUse? use = null;
        var audit = new ObservationStepAudit(() =>
        {
            if (fault is "original") { owned.Open = false; }
            if (fault is "control") { f.ControlRevision++; }
            if (fault is "cancel") { use!.Cancel(); }
        });
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        use = await flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        using (use)
        {
            await flow.Enable(use, Token);
            f.OnVersion = () => throw new InvalidOperationException("No dispatch is allowed after final gate revocation.");
            await flow.Invoking(value => value.Invoke(use, (_, _, _) => Task.FromResult(true), Token)).Should().ThrowAsync<Exception>();
            use.State.Should().Be(fault is "cancel" ? ObservationWorkflowState.Cancelled : ObservationWorkflowState.Failed);
            audit.Events.Select(item => item.Outcome).Should().Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested,
                Kora.Core.Auditing.SecurityAuditOutcome.Failed);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ObservationWorkflowEnforcesExactCompleteUtf8ResultBudgetIncludingEverySourceLabel(int excess)
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var info = new ObservationVersion();
        var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
        var registry = new ReadOnlyCapabilityRegistry(f, new(), new(), new(info), new(dependencies), new(runtime), new(runtime),
            NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        var sizes = new Dictionary<int, int>();
        int versionSize;
        using (var root = f.Root())
        {
            for (var count = 1; count <= 6; count++)
            {
                var reply = registry.Invoke(registry.Admit(CapabilityLane.Native), ReadOnlyCapabilityCatalog.List,
                    "{\"count\":" + count + "}", Token);
                sizes[count] = CapabilityReplyWire.GetSerializedSize(reply);
            }
            versionSize = CapabilityReplyWire.GetSerializedSize(registry.Invoke(registry.Admit(CapabilityLane.Native),
                ReadOnlyCapabilityCatalog.Version, "{}", Token));
        }
        var framing = Encoding.UTF8.GetByteCount("Source: Kora host observations. No model inference, probes or external actions.\n")
            + Enumerable.Range(1, 5).Sum(number => Encoding.UTF8.GetByteCount("Step " + number + " - capabilities.list (schema 1)\n\n"))
            + Encoding.UTF8.GetByteCount("Step 6 - application.get_version (schema 1)\n\n");
        var choices = Enumerable.Range(1, 6);
        var selected = (from a in choices from b in choices from c in choices from d in choices from e in choices
            let counts = new[] { a, b, c, d, e }
            let padding = ObservationWorkflowDefinition.MaximumResultBytes - framing - counts.Sum(count => sizes[count]) - versionSize
            where padding is >= 0 and < 127
            select (counts, padding)).First();
        info.Value = "v" + new string('x', selected.padding + excess);
        var steps = selected.counts.Select(count => new { tool = ReadOnlyCapabilityCatalog.List, input = "{\"count\":" + count + "}" })
            .Append(new { tool = ReadOnlyCapabilityCatalog.Version, input = "{}" });
        var source = "{\"steps\":[" + string.Join(',', steps.Select(step => "{\"tool\":\"" + step.tool + "\",\"input\":" + step.input + "}")) + "]}";
        DeclareObservationWorkflow(owned, source);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, registry, f, dependencies, owned.Audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        using var use = await flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        await flow.Enable(use, Token);
        var approvals = 0;
        var run = () => flow.Invoke(use, (_, _, _) => { approvals++; return Task.FromResult(true); }, Token);
        if (excess == 0)
        {
            var result = await run();
            Encoding.UTF8.GetByteCount(result).Should().Be(ObservationWorkflowDefinition.MaximumResultBytes);
            use.State.Should().Be(ObservationWorkflowState.Succeeded);
        }
        else
        {
            await run.Should().ThrowAsync<InvalidDataException>().WithMessage("*complete result budget*");
            use.State.Should().Be(ObservationWorkflowState.Failed);
        }
        approvals.Should().Be(6, "every step is executed and individually approved, never dropped or silently truncated");
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ObservationWorkflowOwnsCancellationIgnoringSourceReadUntilItReallyJoins()
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        DeclareObservationWorkflow(owned);
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        using var use = await flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        await flow.Enable(use, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owned.Store.DuringRead = () => { entered.TrySetResult(); return release.Task; };
        var approvals = 0;
        var run = flow.Invoke(use, (_, _, _) => { approvals++; return Task.FromResult(true); }, Token);
        await entered.Task;
        use.Cancel();
        use.State.Should().Be(ObservationWorkflowState.Cancelling);
        run.IsCompleted.Should().BeFalse();
        release.SetResult();
        await ((Func<Task>)(async () => await run)).Should().ThrowAsync<OperationCanceledException>();
        use.State.Should().Be(ObservationWorkflowState.Cancelled);
        approvals.Should().Be(0);
    }

    private const string ObservationSequence = """
        {"steps":[
          {"tool":"application.get_version","input":{}},
          {"tool":"readiness.get","input":{"count":6}},
          {"tool":"runtime.get_status","input":{"id":"local.inference"}}
        ]}
        """;

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ObservationWorkflowRunsEveryDeclaredStepInOrderAfterIndependentGatesWithoutAModelOrEffects(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        await using var owned = new OwnedSkillHarness(f, origin);
        DeclareObservationWorkflow(owned);
        var policy = new NamedUsePolicy();
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, policy, f);
        using var use = await flow.Review("example", "1.0.0", new(f.Session, null), "  original request unchanged\r\n ", owned.Authority, owned.Eligible, Token);
        use.State.Should().Be(ObservationWorkflowState.Reviewed);
        use.SourceText.Should().Contain(ObservationSequence);
        owned.Store.Snapshot!.Document.IsAvailableForInvocation.Should().BeFalse();
        var calls = new List<string>();
        var invokeDisabled = () => flow.Invoke(use, (_, _, _) => throw new InvalidOperationException("No approval before enable."), Token);
        await invokeDisabled.Should().ThrowAsync<InvalidOperationException>();
        await flow.Enable(use, Token);
        use.State.Should().Be(ObservationWorkflowState.Enabled);
        f.OnVersion = () => calls.Add("executed-version");
        var result = await flow.Invoke(use, (step, current, _) =>
        {
            current().Should().BeTrue();
            calls.Add("approved-" + step.Number);
            step.Number.Should().Be(calls.Count == 1 ? 1 : calls.Count - 1);
            return Task.FromResult(true);
        }, Token);
        calls.Should().Equal("approved-1", "executed-version", "approved-2", "approved-3");
        result.Should().Contain("Source: Kora host observations").And.Contain("Step 1 - application.get_version")
            .And.Contain("Step 2 - readiness.get").And.Contain("Step 3 - runtime.get_status").And.Contain("NotObserved");
        use.Request.Should().Be("  original request unchanged\r\n ");
        use.Target.Authority.Should().Be(f.Session);
        use.State.Should().Be(ObservationWorkflowState.Succeeded);
        use.SourceText.Should().BeEmpty();
        owned.Store.Writes.Should().Be(0);
        dependencies.Observations.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        owned.Audit.Events.Count(item => string.Equals(item.ActionId, "skills.observation-step.invoke", StringComparison.Ordinal)).Should().Be(6);
        var replay = () => flow.Invoke(use, (_, _, _) => Task.FromResult(true), Token);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        use.Cancel();
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("manifest")]
    [InlineData("instructions")]
    [InlineData("bytes")]
    [InlineData("missing")]
    [InlineData("owner")]
    [InlineData("control")]
    [InlineData("privacy")]
    [InlineData("profile")]
    [InlineData("profile-held")]
    [InlineData("target")]
    [InlineData("dependencies")]
    [InlineData("policy")]
    [InlineData("source-disabled")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("host")]
    [InlineData("audit")]
    public async Task ObservationWorkflowClosesNextStepAndSuppressesWholeResultWhenAnyReviewedBoundaryChanges(string fault)
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        DeclareObservationWorkflow(owned);
        var policy = new NamedUsePolicy();
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, policy, f);
        using var use = await flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        await flow.Enable(use, Token);
        var executions = 0;
        f.OnVersion = () =>
        {
            executions++;
            var snapshot = owned.Store.Snapshot!;
            switch (fault)
            {
                case "directory": owned.Store.Snapshot = snapshot with { DirectoryIdentity = "replaced" }; break;
                case "manifest": owned.Store.Snapshot = snapshot with { ManifestIdentity = "replaced" }; break;
                case "instructions": owned.Store.Snapshot = snapshot with { InstructionsIdentity = "replaced" }; break;
                case "bytes":
                    owned.Store.Snapshot = snapshot with { Document = OwnedDeclarativeSkillDocument.CreateObservationWorkflow(
                        "example", "1.0.0", "Example", "Description", ObservationSequence + " ") }; break;
                case "missing": owned.Store.Snapshot = null; break;
                case "owner": owned.Open = false; break;
                case "control": f.ControlRevision++; break;
                case "privacy": f.CanControl = false; break;
                case "profile": f.SessionProfileRevision++; break;
                case "profile-held": f.SessionProfileHeld = true; break;
                case "target": f.Session = f.Session with { Generation = new(2) }; break;
                case "dependencies": dependencies.RecordObservation(new("local.inference", "recorded", DependencyReadiness.Ready, "private")); break;
                case "policy": policy.Revision++; break;
                case "source-disabled": policy.Disabled.Add(Kora.Core.Artifacts.ArtifactUseIdentity.File(Path.Combine(owned.Store.Destination(snapshot.Document), "manifest.yaml"))); break;
                case "expiry": f.Time.Now = f.Time.Now.AddMinutes(2); break;
                case "clock": f.Time.Now = f.Time.Now.AddTicks(-1); break;
                case "host": f.Current = false; break;
                case "audit": owned.Audit.Failure = Kora.Core.Auditing.SecurityAuditOutcome.Succeeded; break;
            }
        };
        var approvals = 0;
        var invoke = () => flow.Invoke(use, (_, _, _) => { approvals++; return Task.FromResult(true); }, Token);
        await invoke.Should().ThrowAsync<Exception>();
        use.State.Should().Be(ObservationWorkflowState.Failed);
        approvals.Should().Be(1);
        executions.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        owned.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("deadline")]
    [InlineData("deny")]
    [InlineData("dispose")]
    public async Task ObservationWorkflowCancellationDoesNotClaimTerminalUntilCancellationIgnoringApprovalJoins(string operation)
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        DeclareObservationWorkflow(owned);
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        using var use = await flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        await flow.Enable(use, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = 0;
        f.OnVersion = () => executed++;
        var run = flow.Invoke(use, async (_, _, _) => { entered.SetResult(); return await release.Task; }, Token);
        await entered.Task;
        if (operation is "deadline") { f.Time.FireDeadline(); }
        else if (operation is "dispose") { use.Dispose(); }
        else if (operation is "cancel") { use.Cancel(); }
        run.IsCompleted.Should().BeFalse();
        use.State.Should().Be(operation is "cancel" or "dispose" ? ObservationWorkflowState.Cancelling : ObservationWorkflowState.Running);
        release.SetResult(operation is not "deny");
        await ((Func<Task>)(async () => await run)).Should().ThrowAsync<OperationCanceledException>();
        use.State.Should().Be(ObservationWorkflowState.Cancelled);
        executed.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ObservationWorkflowRejectsWrongKindForeignOwnerAndReenableWithoutDispatch()
    {
        using var f = new Fixture();
        await using var owned = new OwnedSkillHarness(f);
        await using var control = new OwnedSkillControlAdmission(owned.Control, owned.Control, new(owned.Control));
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var flow = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        var wrong = () => flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        await wrong.Should().ThrowAsync<InvalidDataException>();
        DeclareObservationWorkflow(owned);
        using var use = await flow.Review("example", "1.0.0", new(f.Session, null), "original", owned.Authority, owned.Eligible, Token);
        var foreign = new ObservationSkillWorkflow(owned.Workflow, control, f.ObservationRegistry, f, dependencies, owned.Audit, f.Time, owned.Store, new NamedUsePolicy(), f);
        await foreign.Invoking(value => value.Invoke(use, (_, _, _) => Task.FromResult(true), Token)).Should().ThrowAsync<InvalidOperationException>();
        await flow.Enable(use, Token);
        await flow.Invoking(value => value.Enable(use, Token)).Should().ThrowAsync<InvalidOperationException>();
        use.State.Should().Be(ObservationWorkflowState.Failed);
        f.Adapter.Calls.Should().Be(0);
    }

    private static void DeclareObservationWorkflow(OwnedSkillHarness owned, string source = ObservationSequence)
    {
        owned.Store.Snapshot = owned.Store.Snapshot! with
        { Document = OwnedDeclarativeSkillDocument.CreateObservationWorkflow("example", "1.0.0", "Example", "Description", source) };
    }

    private sealed partial class Fixture
    {
        internal Kora.Application.Tools.ReadOnlyCapabilityRegistry ObservationRegistry => registry;
    }

    private sealed class ObservationVersion : Kora.Core.Hosting.IApplicationInfo
    {
        internal string Value { get; set; } = "v";
        public string Version => Value;
    }

    private sealed class ObservationStepAudit(Action requested) : Kora.Core.Auditing.ISecurityAuditLog
    {
        internal List<Kora.Core.Auditing.SecurityAuditEvent> Events { get; } = [];
        public void Write(Kora.Core.Auditing.SecurityAuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            if (auditEvent.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Requested) { requested(); }
        }
    }
}
