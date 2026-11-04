using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmittedMemoryRecordsOnlyRemainingReviewedSnapshotAndRejectsAvailabilityRace(bool race)
    {
        using var f = new Fixture();
        var source = new ContextMemories(f, 2);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        var removed = source.Selection[0];
        review.RemoveWholeMemory(removed.Id);
        var receipt = await service.SubmitMemoryContextAsync(workflow, review, static () => true, Token);
        var submitted = receipt.Message.SubmittedContext!;
        submitted.MemorySources.Select(item => item.Id.Value).Should().Equal(source.Selection[1].Id);
        submitted.MemorySources.Should().NotContain(item => item.Id.Value == removed.Id);
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
