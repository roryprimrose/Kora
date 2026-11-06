using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    [InlineData(ModelTurnOutcome.QuotaExceeded)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.DeniedEgress)]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task FailedProfileMemoryHandoffCannotRetainProvenanceFromSuccessShapedProviderOutput(ModelTurnOutcome outcome)
    {
        using var f = new Fixture();
        var source = new ContextMemories(f, deviceProfile: true);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime(), MemoryScopeKind.DeviceProfile);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection,
            "question", static () => true, Token);
        f.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate, new("I used profile memory", null));
        var receipt = await service.SubmitMemoryContextAsync(workflow, review, static () => true, Token);
        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Message.SubmittedContext.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SubmittedMemoryRecordsOnlyRemainingReviewedSnapshotAndRejectsAvailabilityRace(bool race, bool deviceProfile = false)
    {
        using var f = new Fixture();
        var source = new ContextMemories(f, 2, deviceProfile);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime(),
            deviceProfile ? MemoryScopeKind.DeviceProfile : MemoryScopeKind.Session);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        var removed = source.Selection[0];
        review.RemoveWholeMemory(removed.Id);
        var receipt = await service.SubmitMemoryContextAsync(workflow, review, static () => true, Token);
        var submitted = receipt.Message.SubmittedContext!;
        var ids = deviceProfile ? submitted.ProfileMemorySources.Select(item => item.Id.Value)
            : submitted.MemorySources.Select(item => item.Id.Value);
        ids.Should().Equal(source.Selection[1].Id).And.NotContain(removed.Id);
        f.MemoryAvailability = [SubmittedHistoryAvailability.RetainedRevision];
        var reads = source.Reads;
        var audits = f.Audits.ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeSubmittedAvailabilityRead = async token =>
        {
            if (!race || f.SubmittedAvailabilityReads != 2) { return; }
            entered.SetResult();
            await release.Task.WaitAsync(token);
        };
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var inspection = service.ReadDetailAsync(new(f.Session, null), receipt.Message, static () => true, Token, submittedContext: true);
        if (race)
        {
            await entered.Task.WaitAsync(Token);
            inspection.IsCompleted.Should().BeFalse();
            f.MemoryAvailability = [SubmittedHistoryAvailability.Unavailable];
            release.SetResult();
            await inspection.Invoking(value => value).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            (await inspection).Source.Should().Contain("exact enabled memory revision is retained").And.NotContain("reviewed memory value");
        }
        source.Reads.Should().Be(reads);
        f.Audits.Should().Equal(audits);
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(1);
    }
}
