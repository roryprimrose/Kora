using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.NativeUxFixture;

internal sealed partial class NativeUxFixtureSession
{
    private readonly List<OperationGrant> exactGrantSeeds = [];
    private bool exactUseAttempted;
    private bool exactLifecycleAttempted;
    private bool exactDenialAttempted;

    internal bool ExactGrants { get; }
    internal ExactGrantControlAdmission? ExactGrantControl { get; private set; }
    internal OperationGrant ExactTarget => ExactSeed(0);
    internal OperationGrant ExactUseTarget => ExactSeed(1);
    internal OperationGrant ExactLifecycleTarget => ExactSeed(2);
    internal OperationGrant ExactPerpetual => ExactSeed(3);
    internal HostInteractionOutcome? ExactConsumeOutcome { get; private set; }

    private OperationGrant ExactSeed(int index)
    {
        RequireValidationAccess();
        if (!ExactGrants || exactGrantSeeds.Count != 51)
        {
            throw new InvalidOperationException("The optional exact-grants scratch fixture is unavailable.");
        }
        return exactGrantSeeds[index];
    }

    private async Task InitializeExactGrantsAsync()
    {
        var authorization = new HostAuthorizationService(Interactions, TimeProvider.System);
        ExactGrantControl = new(Interactions, Interactions, new(Tasks), authorization);
        for (var index = 0; index < 51; index++)
        {
            var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
                RequestOrigin.LocalUi, new(Guid.NewGuid()));
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            await Tasks.CommitAsync(intent, 0, Token);
            await Interactions.CreateSessionAsync(request, Token);
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"synthetic-exact-grant-{index:00}")));
            var binding = new ExactOperationBinding($"fixture.inspect.{index:00}", "builtin", "synthetic",
                digest, digest, digest, digest, digest, digest, digest, digest, digest, new(1));
            var proposal = new HostOperationProposal(request, new(Guid.NewGuid()), new(1), binding,
                HostOperationEffect.BoundedRead, TimeProvider.System.GetUtcNow().AddHours(1));
            await Interactions.PublishTrustedSnapshotAsync(request, new(true, true, false, true), proposal, 0, Token);
            var question = await authorization.PresentAsync(request, Token);
            if (question.Outcome != HostInteractionOutcome.Presented || question.Question is null)
            {
                throw new InvalidOperationException("The genuine scratch host approval question was not presented.");
            }
            var scope = index == 3 ? "perpetual" : index is 1 or 2 ? "session" : "once";
            var decision = await authorization.ApproveAsync(question.Question.Key, new([scope]), RequestOrigin.LocalUi, Token);
            if (decision.Outcome != HostInteractionOutcome.Approved || decision.Grant is null)
            {
                throw new InvalidOperationException("The genuine scratch host approval was not committed.");
            }
            exactGrantSeeds.Add(decision.Grant);
            if (index == 2)
            {
                await Tasks.CommitAsync(intent.Next(HostTaskState.Succeeded), intent.Revision.Value, Token);
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
    }

    internal async Task ConsumeExactUseTargetAsync()
    {
        var target = ExactUseTarget;
        if (exactUseAttempted) { throw new InvalidOperationException("No automatic or repeated synthetic consume is admitted."); }
        exactUseAttempted = true;
        using var activity = HostActivity.BeginRoot(target.ApprovedProposal.Request, HostActivityLayer.Desktop, HostOperation.Request);
        var result = await new HostAuthorizationService(Interactions, TimeProvider.System).ConsumeAsync(
            target.ApprovedProposal.Request, target.Id, target.Revision, Token);
        if (result.Outcome != HostInteractionOutcome.Consumed || result.Grant?.UseCount != 1)
        {
            throw new InvalidOperationException("The scratch authority-only consume did not advance its exact revision.");
        }
        activity.Complete(HostOperationOutcome.Completed);
    }

    internal async Task AdvanceExactLifecycleAsync()
    {
        var target = ExactLifecycleTarget;
        if (exactLifecycleAttempted) { throw new InvalidOperationException("No repeated synthetic lifecycle transition is admitted."); }
        exactLifecycleAttempted = true;
        await Sessions.ChangeLifecycleAsync(target.ApprovedProposal.Request.SessionId, target.SessionGeneration,
            false, RequestOrigin.LocalUi, Token);
    }

    internal async Task DenyRevokedExactConsumeAsync()
    {
        var target = ExactTarget;
        var current = await Interactions.InspectExactGrantAsync(target.Id, Token);
        if (exactDenialAttempted || current?.Grant.Status != OperationGrantStatus.Revoked)
        {
            throw new InvalidOperationException("One subsequent denial probe requires a read-back revoked exact target first.");
        }
        exactDenialAttempted = true;
        using var activity = HostActivity.BeginRoot(target.ApprovedProposal.Request, HostActivityLayer.Desktop, HostOperation.Request);
        var decision = await new HostAuthorizationService(Interactions, TimeProvider.System).ConsumeAsync(
            target.ApprovedProposal.Request, target.Id, current.Grant.Revision, Token);
        if (decision.Outcome != HostInteractionOutcome.Conflict || decision.Grant is not null)
        {
            throw new InvalidOperationException("The revoked exact target did not deny subsequent authority consume.");
        }
        ExactConsumeOutcome = decision.Outcome;
        activity.Complete(HostOperationOutcome.Completed);
    }

    internal async Task<ExactFixtureObservation> ObserveExactGrantsAsync()
    {
        _ = ExactTarget;
        var inventory = new List<ExactGrantInspection>();
        ExactGrantCursor? cursor = null;
        ExactGrantCursor? head = null;
        var pages = 0;
        do
        {
            var page = await Interactions.ReadExactGrantPageAsync(cursor, Token);
            if (++pages > 51 || page.Records.Length > ExactGrantPage.MaximumRecords)
            {
                throw new InvalidDataException("Scratch exact inventory exceeded its bounded discovery contract.");
            }
            inventory.AddRange(page.Records);
            if (pages == 1) { head = page.Next; }
            cursor = page.Next;
        } while (cursor is not null);
        if (inventory.Count != 51 || head is null
            || !inventory.Select(item => item.Grant.Id).ToHashSet().SetEquals(exactGrantSeeds.Select(item => item.Id)))
        {
            throw new InvalidDataException("Scratch inventory lost exact IDs or genuine bounded paging.");
        }
        var roles = new List<ExactFixtureRole>();
        foreach (var (name, index) in new[] { ("target", 0), ("used", 1), ("lifecycle", 2), ("perpetual", 3) })
        {
            var seed = ExactSeed(index);
            var current = await Interactions.InspectExactGrantAsync(seed.Id, Token)
                ?? throw new InvalidDataException("An exact scratch role is missing.");
            var retention = await Interactions.ReadRetentionObservationAsync(seed.ApprovedProposal.Request.SessionId, Token);
            roles.Add(new(name, current, new(retention.State, retention.Generation,
                retention.ExemptionAuditSequence, retention.WorkHoldObserved, retention.Removed)));
        }
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(inventory)));
        var unrelated = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            inventory.Where(item => !exactGrantSeeds.Take(3).Any(seed => seed.Id == item.Grant.Id)).ToArray())));
        return new(head, pages, inventory.Count, digest, unrelated, roles, ExactConsumeOutcome);
    }

    internal sealed record ExactFixtureRetention(SessionRetentionState State, HostRevision Generation,
        long ExemptionAuditSequence, bool WorkHoldObserved, bool Removed);
    internal sealed record ExactFixtureRole(string Name, ExactGrantInspection Inspection, ExactFixtureRetention Retention);
    internal sealed record ExactFixtureObservation(ExactGrantCursor AuditHead, int Pages, int Count,
        string InventorySha256, string UnrelatedSha256, IReadOnlyList<ExactFixtureRole> Roles, HostInteractionOutcome? ConsumeOutcome)
    {
        public string? ConsumeOutcomeName => ConsumeOutcome?.ToString();
    }
}
