using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("foreign-host")]
    [InlineData("retired")]
    [InlineData("missing")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("foreign-request")]
    [InlineData("observation")]
    [InlineData("private")]
    [InlineData("target")]
    [InlineData("source-outcome")]
    [InlineData("source-task")]
    [InlineData("source-execution")]
    [InlineData("source-body")]
    [InlineData("successor-request")]
    [InlineData("successor-body")]
    [InlineData("lineage")]
    [InlineData("observation-id")]
    [InlineData("lineage-source")]
    [InlineData("review")]
    [InlineData("created")]
    [InlineData("expiry")]
    [InlineData("profile")]
    [InlineData("destination")]
    [InlineData("digest")]
    [InlineData("target-current")]
    [InlineData("source-readback")]
    [InlineData("successor-readback")]
    [InlineData("replay")]
    public async Task VerifiedSuccessorAdmissionRejectsCounterfeitReceiptsForeignAuthorityAndReplay(string change)
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var original = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        await using (var review = source.Open(original, RequestOrigin.LocalUi, static () => true)!)
        {
            await review.Refresh(Token);
            f.Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
            await review.Decide(original, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        }
        await running;
        var commit = f.LastHandoffCommit!;
        var request = commit.Successor.Message.Request;
        var context = new ModelContextEnvelope(request, original.Context.SystemPolicy, original.Context.UserRequest,
            [], original.Context.CreatedAt, original.Context.ExpiresAt, original.Context.ToolMode);
        var lineage = commit.Successor.Message.Handoff!;
        var changedLineage = change switch
        {
            "observation-id" => lineage with { Observation = Guid.NewGuid() },
            "lineage-source" => lineage with { Source = lineage.Source with { Turn = new(Guid.NewGuid()) } },
            "review" => lineage with { Review = new(lineage.Review.Request, new(Guid.NewGuid()), new(1)) },
            "created" => lineage with { CreatedAt = lineage.CreatedAt.AddTicks(-1) },
            "expiry" => lineage with { ExpiresAt = lineage.ExpiresAt.AddTicks(1) },
            "profile" => lineage with { Profile = lineage.Profile with { Account = new("foreign-account") } },
            "destination" => lineage with { Destination = lineage.Destination with { Model = new(Guid.NewGuid()) } },
            "digest" => lineage with { ContextDigest = new string('b', 64) },
            _ => lineage,
        };
        commit = commit with { Successor = commit.Successor with { Message = commit.Successor.Message with { Handoff = changedLineage } } };
        if (change is "source-outcome") { commit = commit with { Source = commit.Source with { Message = commit.Source.Message with { Outcome = ModelTurnOutcome.Unknown } } }; }
        if (change is "source-task") { commit = commit with { Source = commit.Source with { Task = new(commit.Source.Task.Request, new(3), HostTaskState.Unknown) } }; }
        if (change is "source-execution") { commit = commit with { Source = commit.Source with { Message = commit.Source.Message with { Execution = null } } }; }
        if (change is "source-body") { commit = commit with { Source = commit.Source with { Message = commit.Source.Message with { Body = "foreign original" } } }; }
        if (change is "successor-request") { commit = commit with { Successor = commit.Successor with { Message = commit.Successor.Message with { Request = HostRequest.Create(RequestOrigin.LocalUi) } } }; }
        if (change is "successor-body") { commit = commit with { Successor = commit.Successor with { Message = commit.Successor.Message with { Body = "foreign successor" } } }; }
        if (change is "lineage") { commit = commit with { Successor = commit.Successor with { Message = commit.Successor.Message with { Handoff = null } } }; }
        if (change is "source-readback") { f.HistoryMessages[commit.Source.Message.Request.RequestId] = commit.Source.Message with { Body = "changed source" }; }
        if (change is "successor-readback") { f.Retained = f.Retained! with { Body = "changed successor" }; }
        if (change is "private") { f.CanControl = false; }
        if (change is "target-current") { f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]); }
        var offer = new ModelHandoffOffer(host, original.Policy, original.Context, original.Reason, original.Question,
            original.Generation, original.TaskRevision, original.ControlRevision)
        {
            LocalUnavailable = change is "observation" ? null : original.LocalUnavailable,
            Target = change is "target" ? null : original.Target,
        };
        if (change is "retired") { workflow.Retire(offer); }
        using var activity = change is "missing" ? null : HostActivity.BeginRoot(
            change is "foreign-request" ? HostRequest.Create(RequestOrigin.LocalUi) : request,
            HostActivityLayer.Application, HostOperation.Request);
        if (change is "stopped") { activity!.Activity!.Stop(); System.Diagnostics.Activity.Current = activity.Activity; }
        if (change is "completed") { activity!.Complete(HostOperationOutcome.Completed); }
        var calls = runtime.Calls;
        var admission = await (change is "foreign-host" ? f.PolicyHost : host)
            .AdmitVerifiedHandoffAsync(offer, commit, context, Token);
        admission.Result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        admission.Result.Reason.Should().Be(ModelTurnReason.HandoffReviewStale);
        admission.Turn.Should().BeNull();
        runtime.Calls.Should().Be(calls);
        runtime.Transmissions.Should().Be(0);
        f.HandoffCommits.Should().Be(1);
    }

    [Theory]
    [InlineData("configuration")]
    [InlineData("session-store")]
    [InlineData("reviews")]
    [InlineData("provider")]
    [InlineData("held")]
    [InlineData("inactive")]
    [InlineData("foreign-session")]
    [InlineData("private")]
    [InlineData("tuple")]
    [InlineData("registration")]
    [InlineData("selection")]
    [InlineData("adapter")]
    [InlineData("qualification")]
    public async Task LocalHandoffTargetRequiresActualCurrentConfiguredAccountProfileAndQualifiedAdapter(string change)
    {
        using var f = new Fixture();
        var registration = new HandoffRegistrationSource(f, change);
        var (host, _, _, _, _) = f.VerifiedOperation(change is not "configuration", change is not "session-store",
            change is not "reviews", registration);
        using var root = f.Root();
        var policy = (await host.InitializePolicyAsync(Token)).Policy!;
        if (change is "provider") { policy = policy with { Hosted = policy.Hosted with { Provider = ModelProviderIdentity.Claude } }; }
        if (change is "held") { f.SessionProfileHeld = true; }
        if (change is "inactive") { f.ProfileRead = () => new(f.Session with { IsActive = false }, 0, SessionProviderOverrides.Inherit, false); }
        if (change is "foreign-session") { f.ProfileRead = () => new(f.Session with { SessionId = new(Guid.NewGuid()) }, 0, SessionProviderOverrides.Inherit, false); }
        if (change is "private") { f.Current = false; }
        if (change is "tuple") { f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]); }
        (await host.ResolveHandoffTargetAsync(policy, Token)).Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        f.HandoffCommits.Should().Be(0);
    }

    private sealed class HandoffRegistrationSource(Fixture fixture, string change) : IModelProviderRegistrationSource
    {
        public Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile, CancellationToken token)
        {
            if (change is "registration") { return Task.FromResult<ModelProviderRegistration?>(null); }
            return Task.FromResult<ModelProviderRegistration?>(new(
                change is "selection" ? selection with { Revision = new(2) } : selection,
                change is "qualification" ? ModelQualificationGate.None : Fixture.Gates(selection), fixture.Time.Now.AddMinutes(5),
                change is "adapter" ? fixture.Adapter : new CopilotModelTurnAdapter(new HostedRuntime(), fixture.HandoffReviews)));
        }
    }
}
