using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task SourceRetirementDuringQualifiedAdmissionCannotPublishPendingAcceptance()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var live = true;
        var admission = new TransformedAdmission(host, admitted => { live = false; return admitted; }, () => true);
        await using var service = f.ConversationService(admission, execution: host);
        var accepted = false;
        await service.Invoking(value => value.SubmitQueuedAsync(new(f.Session, null), "original", () => live,
            Token, accepted: () => accepted = true)).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        accepted.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
    }

    [Fact]
    public async Task SpecializedSourceCannotReplaceTheAlreadyOwnedRunningLeaseEvenWithFalselyIdleAdmission()
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(new ReportedIdleAdmission(host), execution: host);
        Task<ConversationSubmissionReceipt>? competing = null;
        service.RunningConversationChanged += state =>
        {
            if (state.Phase != ConversationExecutionPhase.Running || competing is not null) { return; }
            var request = f.Request;
            var task = f.Task;
            var retained = f.Retained;
            static ModelContextEnvelope Create(HostRequest original, DateTimeOffset now) => new(original, "policy", "competing",
                [new(new(Guid.NewGuid()), original, new(1), "reviewed local source", ModelEvidenceDisclosure.LocalOnly)],
                now, now.AddSeconds(15), ModelToolMode.None);
            competing = service.SubmitReviewedContextAsync(new(f.Session, null), "competing", Create,
                () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token);
            f.Request = request;
            f.Task = task;
            f.Retained = retained;
        };
        (await service.SubmitAsync(new(f.Session, null), "original", () => true, Token))
            .Task.State.Should().Be(HostTaskState.Succeeded);
        var failed = await this.Invoking(_ => competing!).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        failed.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(1);
    }
}
