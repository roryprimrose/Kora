using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed partial class ModelTurnHostTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ModelTurnHostTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Production_profiles_remain_unavailable_without_contacting_either_provider()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var host = fixture.Production();
        foreach (var selection in new[] { ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate })
        {
            var admission = await host.AdmitAsync(selection, fixture.Context(), Token);
            admission.Result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            admission.Result.Reason.Should().Be(ModelTurnReason.QualificationPending);
            admission.Result.Provenance!.Selection.Should().Be(selection);
            admission.Turn.Should().BeNull();
        }
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Audits.Select(a => a.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task One_host_issued_turn_dispatches_exactly_one_provider_once_with_host_provenance()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        turn.Provenance.Turn.Validate();
        turn.Provenance.Request.Should().BeSameAs(fixture.Request);
        var result = await fixture.Host.RunAsync(turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Response!.Answer.Should().Be("answer");
        result.Provenance.Should().BeSameAs(turn.Provenance);
        (await fixture.Host.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.TurnAlreadyUsed);
        fixture.Adapter.Calls.Should().Be(1);
        fixture.Adapter.Wire.Should().Equal(fixture.Context().Serialize());
        fixture.Adapter.Provenance.Should().BeSameAs(turn.Provenance);
        HostActivity.Current.Should().BeSameAs(root);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    [InlineData(ModelTurnOutcome.QuotaExceeded)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.DeniedEgress)]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task Every_failure_outcome_is_typed_has_no_response_and_never_falls_back(ModelTurnOutcome outcome)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate, new("hostile success-shaped payload", null));
        var result = await fixture.Run();
        result.Outcome.Should().Be(outcome);
        result.Response.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(1);
        fixture.Audits.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("model")]
    [InlineData("revision")]
    [InlineData("unknown-outcome")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("action")]
    [InlineData("grant")]
    [InlineData("question")]
    [InlineData("combined")]
    [InlineData("overflow")]
    public async Task Untrusted_provider_fields_cannot_select_identity_authority_or_success(string failure)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var selection = ModelProviderSelection.OllamaCandidate;
        fixture.Adapter.Reply = failure switch
        {
            "provider" => new(ModelTurnOutcome.Succeeded, selection with { Provider = ModelProviderIdentity.Copilot }, new("x", null)),
            "model" => new(ModelTurnOutcome.Succeeded, selection with { Model = new(Guid.NewGuid()) }, new("x", null)),
            "revision" => new(ModelTurnOutcome.Succeeded, selection with { Revision = new(2) }, new("x", null)),
            "unknown-outcome" => new((ModelTurnOutcome)999, selection),
            "null" => new(ModelTurnOutcome.Succeeded, selection),
            "empty" => new(ModelTurnOutcome.Succeeded, selection, new(" ", null)),
            "action" => new(ModelTurnOutcome.Succeeded, selection, new(null, Kora.Core.Commands.BuiltInAction.LockMachine)),
            "grant" => new(ModelTurnOutcome.Succeeded, selection, new("x", null,
                new(Kora.Core.Commands.GrantChangeOperation.Add, Kora.Core.Commands.BuiltInAction.LockMachine,
                    Kora.Core.Commands.ModelApprovalScope.Session))),
            "question" => new(ModelTurnOutcome.Succeeded, selection, new("x", null, Question: new("choose", ["a", "b"]))),
            "combined" => new(ModelTurnOutcome.Succeeded, selection, new("x", Kora.Core.Commands.BuiltInAction.LockMachine)),
            _ => new(ModelTurnOutcome.Succeeded, selection, new(new string('x', 4096), null)),
        };
        var result = await fixture.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.Response.Should().BeNull();
        result.Provenance!.Selection.Should().Be(selection);
    }

    [Theory]
    [InlineData(4096, ModelTurnOutcome.Succeeded)]
    [InlineData(4097, ModelTurnOutcome.Unknown)]
    public async Task Complete_serialized_response_has_the_exact_utf8_limit(int bytes, ModelTurnOutcome outcome)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var framing = JsonSerializer.SerializeToUtf8Bytes(new LocalModelResponse("", null)).Length;
        fixture.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new(new string('x', bytes - framing), null));
        JsonSerializer.SerializeToUtf8Bytes(fixture.Adapter.Reply.Response).Length.Should().Be(bytes);
        (await fixture.Run()).Outcome.Should().Be(outcome);
    }

    [Theory]
    [InlineData("missing", ModelTurnReason.ContextMissing)]
    [InlineData("foreign", ModelTurnReason.ContextMismatch)]
    [InlineData("expired", ModelTurnReason.ContextExpired)]
    [InlineData("future", ModelTurnReason.ContextExpired)]
    [InlineData("owner", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("privacy", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("foreign-session", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("inactive", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("unknown-generation", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("missing-task", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("foreign-task", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("old-task", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("task-generation", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("terminal", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("changed-on-read", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("expired-on-read", ModelTurnReason.ContextExpired)]
    public async Task Admission_fails_closed_for_missing_expired_foreign_or_revoked_host_state(string failure, ModelTurnReason expected)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var context = fixture.Context();
        switch (failure)
        {
            case "missing": context = null; break;
            case "foreign": context = fixture.Context(HostRequest.Create(RequestOrigin.LocalUi)); break;
            case "expired": fixture.Time.Now += TimeSpan.FromMinutes(1); break;
            case "future": fixture.Time.Now -= TimeSpan.FromSeconds(1); break;
            case "owner": fixture.Current = false; break;
            case "privacy": fixture.CanControl = false; break;
            case "foreign-session": fixture.Session = fixture.Session with { SessionId = new(Guid.NewGuid()) }; break;
            case "inactive": fixture.Session = fixture.Session with { IsActive = false }; break;
            case "unknown-generation": fixture.Session = fixture.Session with { Generation = default }; break;
            case "missing-task": fixture.Task = null; break;
            case "foreign-task": fixture.Task = fixture.Task! with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.IntentRecorded) }; break;
            case "old-task": fixture.Task = fixture.Task! with { CurrentSource = false }; break;
            case "task-generation": fixture.Task = fixture.Task! with { Generation = new(2) }; break;
            case "terminal": fixture.Task = fixture.Task! with { Task = fixture.Task.Task.Next(HostTaskState.Succeeded) }; break;
            case "changed-on-read": fixture.OnRead = () => fixture.ControlRevision++; break;
            case "expired-on-read": fixture.OnRead = () => fixture.Time.Now += TimeSpan.FromMinutes(1); break;
        }
        var admission = await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token);
        admission.Result.Reason.Should().Be(expected);
        admission.Turn.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(32768, ModelTurnOutcome.Succeeded)]
    [InlineData(32769, ModelTurnOutcome.Denied)]
    public async Task Complete_input_counts_catalogue_evidence_and_json_overhead_at_the_exact_limit(int bytes, ModelTurnOutcome outcome)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var framing = fixture.Context(text: "").Serialize().Length;
        var envelope = fixture.Context(text: new string('x', bytes - framing));
        envelope.Serialize().Length.Should().Be(bytes);
        var result = await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, envelope, Token);
        result.Result.Outcome.Should().Be(outcome);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Hosted_eligibility_never_establishes_exact_destination_egress_authority()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var hosted = fixture.Qualified(ModelProviderSelection.CopilotCandidate);
        var result = await hosted.AdmitAsync(ModelProviderSelection.CopilotCandidate, fixture.Context(), Token);
        result.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Turn.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Exact_qualification_gates_cannot_be_inferred_from_partial_or_expired_evidence()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        foreach (var selection in new[] { ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate })
        {
            var required = Fixture.Gates(selection);
            foreach (var gate in Enum.GetValues<ModelQualificationGate>().Where(g => g != ModelQualificationGate.None && required.HasFlag(g)))
            {
                var host = fixture.Qualified(selection, required & ~gate);
                (await host.AdmitAsync(selection, fixture.Context(), Token)).Result.Reason.Should().Be(ModelTurnReason.QualificationPending);
            }
            var expired = new ModelProviderRegistration(selection, required, fixture.Time.Now, fixture.Adapter);
            expired.IsQualified(fixture.Time.Now).Should().BeFalse();
            (expired with { ExpiresAt = fixture.Time.Now.AddMinutes(1), Adapter = null }).IsQualified(fixture.Time.Now).Should().BeFalse();
        }
        new ModelProviderRegistration(ModelProviderSelection.OllamaCandidate with { Provider = (ModelProviderIdentity)999 },
            (ModelQualificationGate)31, DateTimeOffset.MaxValue, fixture.Adapter).IsQualified(fixture.Time.Now).Should().BeFalse();
        var unknown = await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate with { Model = new(Guid.NewGuid()) },
            fixture.Context(), Token);
        unknown.Result.Reason.Should().Be(ModelTurnReason.UnregisteredModel);
    }

    [Fact]
    public async Task Incoming_activity_completed_context_and_foreign_session_or_host_cannot_borrow_a_turn()
    {
        using var fixture = new Fixture();
        using var incoming = new Activity("untrusted").Start();
        (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token))
            .Result.Reason.Should().Be(ModelTurnReason.HostContextRequired);
        using var root = fixture.Root();
        root.Activity!.ParentId.Should().BeNull();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        using (var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            (await fixture.Host.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        }
        (await fixture.Production().RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        root.Complete(HostOperationOutcome.Completed);
        (await fixture.Host.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token))
            .Result.Reason.Should().Be(ModelTurnReason.HostContextRequired);
    }

    [Fact]
    public async Task A_retired_issuing_activity_cannot_be_replaced_by_a_new_trace_with_identical_request_ids()
    {
        using var fixture = new Fixture();
        ModelTurn turn;
        using (var issuer = fixture.Root())
        {
            turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        }
        using var replacement = fixture.Root();
        (await fixture.Host.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("expiry", ModelTurnReason.ContextExpired)]
    [InlineData("stopped", ModelTurnReason.HostContextRequired)]
    [InlineData("completed", ModelTurnReason.HostContextRequired)]
    public async Task Context_expiry_and_issuing_trace_retirement_during_revalidation_close_dispatch(
        string retirement, ModelTurnReason expected)
    {
        using var fixture = new Fixture();
        using var issuer = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        fixture.OnRead = retirement switch
        {
            "expiry" => () => fixture.Time.Now += TimeSpan.FromMinutes(1),
            "stopped" => () => issuer.Activity!.Stop(),
            _ => () => issuer.Complete(HostOperationOutcome.Completed),
        };
        (await fixture.Host.RunAsync(turn, Token)).Reason.Should().Be(expected);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Disposal_denies_a_previously_admitted_unused_turn()
    {
        using var fixture = new Fixture();
        using var issuer = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        await fixture.Host.DisposeAsync();
        (await fixture.Host.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("owner", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("privacy", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("revision", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("read-revision", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("foreign", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("inactive", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("generation", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("task-revision", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("expired", ModelTurnReason.ContextExpired)]
    [InlineData("qualification", ModelTurnReason.QualificationPending)]
    public async Task Revalidation_closes_dispatch_and_late_presentation(string failure, ModelTurnReason expected)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        void Revoke()
        {
            switch (failure)
            {
                case "owner": fixture.Current = false; break;
                case "privacy": fixture.CanControl = false; break;
                case "revision": fixture.ControlRevision++; break;
                case "read-revision": fixture.OnRead = () => fixture.ControlRevision++; break;
                case "foreign": fixture.Session = fixture.Session with { SessionId = new(Guid.NewGuid()) }; break;
                case "inactive": fixture.Session = fixture.Session with { IsActive = false }; break;
                case "generation": fixture.Session = fixture.Session with { Generation = new(2) }; break;
                case "task-revision": fixture.Task = fixture.Task! with { Task = new(fixture.Request, new(2), HostTaskState.IntentRecorded) }; break;
                case "expired": fixture.Time.Now += TimeSpan.FromMinutes(1); break;
                case "qualification": fixture.Time.Now += TimeSpan.FromSeconds(30); break;
            }
        }
        fixture.Adapter.OnRun = Revoke;
        var late = await fixture.Host.RunAsync(turn, Token);
        late.Reason.Should().Be(expected);
        late.Response.Should().BeNull();
        var earlier = await fixture.Host.RunAsync(
            new(turn.Owner, turn.Issuer, turn.Provenance, turn.Context, turn.Wire, turn.SessionGeneration, turn.TaskRevision,
                turn.ControlRevision, turn.Registration), Token);
        earlier.Reason.Should().Be(expected);
        fixture.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_and_deadline_suppress_late_responses_without_claiming_resource_stop()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), cancellation.Token))
            .Result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        (await fixture.Host.RunAsync(turn, cancellation.Token)).Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        fixture.Adapter.Calls.Should().Be(0);
        using var lateCancellation = new CancellationTokenSource();
        fixture.Adapter.OnRun = lateCancellation.Cancel;
        var late = await fixture.Run(lateCancellation.Token);
        late.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        late.Response.Should().BeNull();
        fixture.Adapter.OnRun = fixture.Time.FireDeadline;
        var timeout = await fixture.Run();
        timeout.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        timeout.Response.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Noncooperative_late_completion_holds_the_slot_and_disposal_until_actual_termination(bool fault)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Adapter.Pending = pending.Task;
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        var run = fixture.Host.RunAsync(turn, cancellation.Token);
        fixture.Adapter.Calls.Should().Be(1);
        fixture.Host.IsQuiescent.Should().BeFalse();
        cancellation.Cancel();
        (await run).Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        fixture.Host.IsQuiescent.Should().BeFalse();
        (await fixture.Run()).Reason.Should().Be(ModelTurnReason.ProviderBusy);
        fixture.Adapter.Calls.Should().Be(1);
        var stopped = new List<Activity>();
        using var observer = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(observer);
        var close = fixture.Host.DisposeAsync();
        close.IsCompleted.Should().BeFalse();
        if (fault) { pending.SetException(new IOException("private late provider payload")); }
        else { pending.SetResult(fixture.Adapter.Reply); }
        await close;
        fixture.Host.IsQuiescent.Should().BeTrue();
        stopped.Should().ContainSingle();
        stopped[0].ParentId.Should().BeNull();
        stopped[0].Links.Should().ContainSingle(link => link.Context.TraceId == root.Activity!.TraceId);
        (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token))
            .Result.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        fixture.Messages.Should().NotContain(m => m.Contains("private late", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admission_io_cancellation_and_failures_are_explicit_and_audit_is_not_optional()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        fixture.OnRead = () => { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); };
        (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), cancellation.Token))
            .Result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        fixture.OnRead = () => throw new IOException("private path credential");
        var io = () => fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token);
        await io.Should().ThrowAsync<IOException>();
        fixture.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        fixture.OnRead = null;
        fixture.Adapter.OnRun = () => throw new IOException("private provider secret");
        var provider = () => fixture.Run();
        await provider.Should().ThrowAsync<IOException>();
        fixture.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        fixture.AuditFailure = true;
        var audit = () => fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token);
        await audit.Should().ThrowAsync<IOException>();
        fixture.Messages.Should().NotContain(m => m.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tool_proposals_reuse_the_same_registry_and_cannot_widen_authority()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        (await fixture.Host.InvokeToolAsync(turn, ReadOnlyCapabilityCatalog.Version, "{}", Token, Token))
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        fixture.Adapter.OnTool = async invoke =>
        {
            (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
            (await invoke("computer.lock", "{\"approved\":true}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
            (await invoke(ReadOnlyCapabilityCatalog.Version, "{\"session\":\"foreign\"}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
            fixture.OnVersion = () => fixture.ControlRevision++;
            (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Reason.Should().Be("model-turn-admission-changed");
            (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Reason.Should().Be("model-turn-not-admitted");
        };
        (await fixture.Host.RunAsync(turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.ControlRevision = turn.ControlRevision;
        (await fixture.Host.InvokeToolAsync(turn, ReadOnlyCapabilityCatalog.Version, "{}", Token, Token))
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        fixture.Messages.Should().NotContain(m => m.Contains("approved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Activities_and_audits_are_host_correlated_without_content_or_paths()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var stopped = new List<Activity>();
        using var observer = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(observer);
        await fixture.Run();
        stopped.Select(a => a.OperationName).Should().Contain("policy.evaluate").And.Contain("runtime.request");
        stopped.Should().OnlyContain(a => a.TraceId == root.Activity!.TraceId && a.Status == ActivityStatusCode.Ok);
        stopped.SelectMany(a => a.TagObjects).Should().NotContain(t => Equals(t.Value, "request-secret"));
        fixture.Audits.Should().OnlyContain(a => a.ActionId == "model.turn" && a.Category == SecurityAuditCategory.ProtectedOperation);
        fixture.LogRequests.Should().OnlyContain(r => ReferenceEquals(r, fixture.Request));
        fixture.Messages.Should().NotContain(m => m.Contains("request-secret", StringComparison.Ordinal));
    }

    private sealed class Fixture : ISessionWorkspaceStore, ISessionWorkspaceAccess, ICapabilityHostAccess,
        ISecurityAuditLog, IApplicationInfo, ILogger<ModelTurnHost>, ILogger<ModelProviderHandoffWorkflow>, IDisposable
    {
        internal HostRequest Request { get; set; }
        internal WorkSessionAuthorization Session { get; set; }
        internal HostTaskObservation? Task { get; set; }
        internal ManualTime Time { get; } = new();
        internal Adapter Adapter { get; } = new();
        internal ModelTurnHost Host { get; }
        internal ModelTurnHost PolicyHost { get; }
        internal Kora.Application.UnitTests.Interaction.InteractionFixture.TransactionalStore Questions { get; }
        internal ModelProviderHandoffWorkflow Workflow { get; }
        internal SavedProviderPreferences ProviderPreferences { get; } = new();
        internal ProviderModeConfigurationService ProviderConfiguration { get; }
        private readonly AudioControlAdmission providerAdmission;
        internal Action<SecurityAuditEvent>? OnAudit { get; set; }
        internal HostRequest? ExpectedAuditRequest { get; set; }
        internal bool Current { get; set; } = true;
        internal Action? OnRead { get; set; }
        internal Func<Task>? BeforeRead { get; set; }
        internal Action? OnVersion { get; set; }
        internal bool AuditFailure { get; set; }
        internal List<SecurityAuditEvent> Audits { get; } = [];
        internal List<string> Messages { get; } = [];
        internal List<HostRequest> LogRequests { get; } = [];
        public bool IsCurrentHost => Current;
        public bool CanInspect => CanControl;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; }
        public string Version { get { OnVersion?.Invoke(); return "1.0"; } }
        private readonly DependencyBootstrapper dependencies = new([], NullLogger<DependencyBootstrapper>.Instance);
        private readonly ReadOnlyCapabilityRegistry registry;
        private readonly List<ModelTurnHost> ownedHosts = [];
        internal Fixture(RequestOrigin origin = RequestOrigin.LocalUi)
        {
            Request = HostRequest.Create(origin);
            Session = new(Request.SessionId, new(1), true);
            Task = new(new(Request, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var store = new Kora.Application.UnitTests.Configuration.AudioControlTestStore();
            providerAdmission = new(store, store, new HostTaskCoordinator(store));
            ProviderConfiguration = new(ProviderPreferences, providerAdmission, this);
            registry = new(this, new(), new(), new(this), new(dependencies), new(runtimes), new(runtimes),
                NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            Host = Qualified(ModelProviderSelection.OllamaCandidate);
            PolicyHost = new(this, this, this, registry, this, this, Time,
                [new(ModelProviderSelection.OllamaCandidate, Gates(ModelProviderSelection.OllamaCandidate), Time.Now.AddMinutes(5), Adapter),
                new(ModelProviderSelection.CopilotCandidate, Gates(ModelProviderSelection.CopilotCandidate), Time.Now.AddMinutes(5), Adapter)], ProviderConfiguration);
            ownedHosts.Add(PolicyHost);
            Questions = new(new(Task.Task, Session, new(true, true, false, true), null, [], []));
            Workflow = new(PolicyHost, new(Questions, Time), this, this, new(Questions, Time));
        }
        internal HostActivity Root() => HostActivity.BeginRoot(Request, HostActivityLayer.Application, HostOperation.Request);
        internal ModelTurnHost Production()
        {
            var host = new ModelTurnHost(this, this, this, registry, this, this, Time, ProviderConfiguration);
            ownedHosts.Add(host);
            return host;
        }
        internal ModelTurnHost Qualified(ModelProviderSelection selection, ModelQualificationGate? evidence = null)
        {
            var host = new ModelTurnHost(this, this, this, registry, this, this, Time,
                [new(selection, evidence ?? Gates(selection), Time.Now.AddSeconds(30), Adapter)]);
            ownedHosts.Add(host);
            return host;
        }
        internal static ModelQualificationGate Gates(ModelProviderSelection selection) => selection.Provider == ModelProviderIdentity.Ollama
            ? ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost
            : ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.AllPathLifecycle
                | ModelQualificationGate.ExecutionAccount | ModelQualificationGate.IntegratedHost;
        internal ModelContextEnvelope Context(HostRequest? request = null, string text = "request-secret") =>
            new(request ?? Request, "policy", text, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        internal async Task<ModelTurnResult> Run(CancellationToken? token = null)
        {
            var turn = (await Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, Context(), Token)).Turn!;
            return await Host.RunAsync(turn, token ?? Token);
        }
        public async ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            OnRead?.Invoke();
            if (BeforeRead is { } before) { await before().ConfigureAwait(false); }
            return new SessionWorkspaceEntry(Session, null);
        }
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) =>
            ValueTask.FromResult(Task);
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (AuditFailure) { throw new IOException("audit unavailable"); }
            HostActivity.RequireCurrent().Request.Should().BeSameAs(ExpectedAuditRequest ?? Request);
            Audits.Add(auditEvent);
            OnAudit?.Invoke(auditEvent);
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (HostActivity.Current is { } current) { LogRequests.Add(current.Request); }
        }
        public void Dispose()
        {
            foreach (var host in ownedHosts) { host.DisposeAsync().AsTask().IsCompletedSuccessfully.Should().BeTrue(); }
            providerAdmission.DisposeAsync().AsTask().IsCompletedSuccessfully.Should().BeTrue();
            dependencies.Dispose();
        }

        internal sealed class SavedProviderPreferences : IModelProviderModePreferences
        {
            internal ModelProviderMode? Mode { get; set; }
            internal bool Pending { get; set; }
            internal bool Corrupt { get; set; }
            public ModelProviderMode? Load() => Pending || Corrupt ? throw new InvalidDataException("Unavailable provider preference") : Mode;
            public ModelProviderMode? ReadBack() => Mode;
            public void BeginWrite() => Pending = true;
            public void Save(ModelProviderMode mode) => Mode = mode;
            public void ConfirmWrite() => Pending = false;
        }
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class Adapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        internal ModelTurnProvenance? Provenance { get; private set; }
        internal Action? OnRun { get; set; }
        internal Task<ModelProviderReply>? Pending { get; set; }
        internal Func<Func<string, string, CancellationToken, ValueTask<CapabilityReply>>, Task>? OnTool { get; set; }
        internal ModelProviderReply Reply { get; set; } = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("answer", null));
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ReadOnlyMemory<byte> context,
            Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            Wire = context.ToArray();
            Provenance = provenance;
            OnRun?.Invoke();
            if (OnTool is { } tool) { await tool(invokeTool); }
            return Pending is { } pending ? await pending : Reply;
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        private TimerCallback? callback;
        private object? state;
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            this.state = state;
            dueTime.Should().Be(TimeSpan.FromSeconds(15));
            return new FakeTimer();
        }
        internal void FireDeadline() => callback!(state);
        private sealed class FakeTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
