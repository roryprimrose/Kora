using System.Text;

using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string MetadataSubject = "10000000-0000-0000-0000-000000000001";
    private const string MetadataGet = """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"sessions.get","input":{"id":"10000000-0000-0000-0000-000000000001"}}]}""";

    private static ModelTurnHost MetadataHost(Fixture fixture, IModelTurnAdapter adapter)
    {
        fixture.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        return fixture.ConfiguredProfiles(adapter: adapter);
    }

    private static void MetadataSource(Fixture fixture)
    {
        var entry = DiscoveryEntry(1, "Untrusted name", active: false);
        fixture.DiscoveryEntries = new() { [entry.Authority.SessionId.Value] = entry };
        var request = new HostRequest(new(Guid.NewGuid()), entry.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        fixture.RemainingProjection = value => value with
        {
            Session = fixture.DiscoveryEntries[entry.Authority.SessionId.Value],
            Queue = new(entry.Authority.SessionId, entry.Authority.Generation, 0, []),
            Tasks = [new(new(request, new(1), HostTaskState.Unknown), new(1), "private diagnostic never projected", false, null)],
            OmittedTasks = 1,
            PendingQuestions = [new(new(request, new(Guid.Parse("10000000-0000-0000-0000-000000000005")), new(1)),
                new(1), QuestionStatus.Pending, QuestionKind.Text, null, DateTimeOffset.UnixEpoch.AddMinutes(2))],
            OmittedQuestions = 2,
        };
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task SessionMetadata_admits_then_reviews_then_discloses_one_exact_source_and_retains_only_in_caller(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        using var root = f.Root();
        MetadataSource(f);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var transport = new ToolTransport(MetadataGet, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = MetadataHost(f, loop);
        await using var service = f.ConversationService(host, execution: host);
        var reviews = 0;
        var receipt = await service.SubmitSessionMetadataAsync(target, new(Guid.Parse(MetadataSubject)), "What work and observed expiry?",
            origin, () => true, (review, _) =>
            {
                reviews++;
                f.RetainCalls.Should().Be(1);
                f.Task!.Task.State.Should().Be(HostTaskState.IntentRecorded);
                transport.Requests.Should().BeEmpty();
                review.Snapshot.Active.Should().BeFalse();
                review.Snapshot.OmittedQuestions.Should().Be(2);
                review.Snapshot.EarliestObservedQuestionExpiry.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(2));
                review.Preview.Should().NotContain("private diagnostic");
                return Task.FromResult(SessionMetadataReview.Decision.Confirm);
            }, Token);
        reviews.Should().Be(1);
        receipt.Message.Request.SessionId.Should().Be(target.Authority.SessionId);
        receipt.Message.AssistantResponse.Should().NotBeNull();
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.RetainCalls.Should().Be(1);
        f.ExecutionStarts.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(request => request.Context.ToolMode == ModelToolMode.SessionMetadata
            && request.Context.Tools.Count == 1 && request.Provenance.Request.SessionId == target.Authority.SessionId);
        var result = transport.Requests[1].Messages.OfType<ModelToolResultMessage>().Single();
        Encoding.UTF8.GetString(result.Result.AsSpan()).Should().Contain("earliestObservedQuestionExpiry")
            .And.Contain("omittedQuestions").And.NotContain("questionId").And.NotContain("private diagnostic");
        f.DiscoveryEntries![Guid.Parse(MetadataSubject)].Authority.IsActive.Should().BeFalse();
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("unknown-decision")]
    [InlineData("stale")]
    [InlineData("source-failure")]
    [InlineData("cancel-token")]
    public async Task SessionMetadata_refusal_or_stale_preparation_retains_truthful_caller_terminal_without_execution(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        MetadataSource(f);
        var transport = new ToolTransport(MetadataGet, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = MetadataHost(f, loop);
        await using var service = f.ConversationService(host, execution: host);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var receipt = await service.SubmitSessionMetadataAsync(new(f.Session, null), new(Guid.Parse(MetadataSubject)),
            "Original question", RequestOrigin.LocalUi, () => true, (_, _) =>
            {
                if (change is "stale") { f.DiscoveryEntries![Guid.Parse(MetadataSubject)] = DiscoveryEntry(1, "Changed", false); }
                if (change is "source-failure") { throw new IOException("private source content"); }
                if (change is "cancel-token") { cancellation.Cancel(); }
                return Task.FromResult(change is "cancel" ? SessionMetadataReview.Decision.Cancel
                    : change is "unknown-decision" ? (SessionMetadataReview.Decision)99 : SessionMetadataReview.Decision.Confirm);
            }, cancellation.Token);
        receipt.Message.Outcome.Should().Be(change is "cancel-token" ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Denied);
        receipt.Message.AssistantResponse.Should().BeNull();
        transport.Requests.Should().BeEmpty();
        f.RetainCalls.Should().Be(1);
        f.ExecutionStarts.Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains("private source content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionMetadata_unavailable_local_admission_reads_and_presents_no_cross_session_metadata()
    {
        using var f = new Fixture();
        using var root = f.Root();
        MetadataSource(f);
        var reads = 0;
        f.RemainingProjection = value => { reads++; return value; };
        var host = f.ConfiguredProfiles(production: true);
        await using var service = f.ConversationService(host, execution: host);
        var receipt = await service.SubmitSessionMetadataAsync(new(f.Session, null), new(Guid.Parse(MetadataSubject)), "Original",
            RequestOrigin.LocalUi, () => true, (_, _) => throw new InvalidOperationException("review must not occur"), Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        reads.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("omission")]
    public async Task SessionMetadata_continuation_changes_prevent_source_result_or_second_model_send(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        MetadataSource(f);
        var projection = f.RemainingProjection!;
        var transport = new ToolTransport(MetadataGet, FinalAnswer)
        {
            AfterRequest = _ =>
            {
                if (change is "rename") { f.DiscoveryEntries![Guid.Parse(MetadataSubject)] = DiscoveryEntry(1, "Changed", false); }
                if (change is "privacy") { f.CanControl = false; }
                if (change is "control") { f.ControlRevision++; }
                if (change is "omission") { f.RemainingProjection = value => projection(value) with { OmittedQuestions = 3 }; }
            }
        };
        await using var loop = CreateLoop(f, transport);
        var host = MetadataHost(f, loop);
        await using var service = f.ConversationService(host, execution: host);
        try
        {
            var receipt = await service.SubmitSessionMetadataAsync(new(f.Session, null), new(Guid.Parse(MetadataSubject)), "Original",
                RequestOrigin.LocalUi, () => true, (_, _) => Task.FromResult(SessionMetadataReview.Decision.Confirm), Token);
            receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        catch (ConversationSubmissionUnconfirmedException)
        { change.Should().BeOneOf("privacy", "control"); }
        transport.Requests.Should().HaveCount(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task SessionMetadata_joins_noncooperative_provider_before_releasing_cancelled_caller()
    {
        using var f = new Fixture();
        using var root = f.Root();
        MetadataSource(f);
        var provider = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport { Pending = provider.Task, AfterRequest = _ => entered.SetResult() };
        await using var loop = CreateLoop(f, transport);
        var host = MetadataHost(f, loop);
        await using var service = f.ConversationService(host, execution: host);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = service.SubmitSessionMetadataAsync(new(f.Session, null), new(Guid.Parse(MetadataSubject)), "Original",
            RequestOrigin.LocalUi, () => true, (_, _) => Task.FromResult(SessionMetadataReview.Decision.Confirm), cancellation.Token);
        await Task.WhenAny(run, entered.Task).WaitAsync(Token);
        entered.Task.IsCompleted.Should().BeTrue("provider admission must actually reach the coordinated provider barrier");
        cancellation.Cancel();
        run.IsCompleted.Should().BeFalse();
        host.IsQuiescent.Should().BeFalse();
        provider.SetResult(ToolTransport.Reply(transport.Requests.Single(), FinalAnswer));
        try
        {
            var receipt = await run;
            receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        }
        catch (ConversationSubmissionUnconfirmedException) { }
        host.IsQuiescent.Should().BeTrue();
        transport.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SessionMetadata_invocation_is_separate_one_use_and_rejects_foreign_inputs_without_reads_of_bodies()
    {
        using var f = new Fixture();
        using var root = f.Root();
        MetadataSource(f);
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var current = true;
        using var invocation = new SessionMetadataInvocation(workspace, new(Guid.Parse(MetadataSubject)), () => current);
        (await invocation.ValidateAsync(Token)).Should().BeFalse();
        (await invocation.InvokeAsync(SessionMetadataTool.Id, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        await invocation.PrepareAsync("Original", Token);
        await invocation.Invoking(value => value.PrepareAsync("replacement", Token)).Should().ThrowAsync<InvalidOperationException>();
        (await invocation.ConfirmAsync(Token)).Should().BeTrue();
        (await invocation.ConfirmAsync(Token)).Should().BeFalse();
        (await invocation.InvokeAsync("sessions.list", "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        (await invocation.InvokeAsync(SessionMetadataTool.Id, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        (await invocation.InvokeAsync(SessionMetadataTool.Id, $$"""{"id":"{{MetadataSubject}}"}""", Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
        (await invocation.InvokeAsync(SessionMetadataTool.Id, $$"""{"id":"{{MetadataSubject}}"}""", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        current = false;
        (await invocation.ValidateAsync(Token)).Should().BeFalse();
        invocation.Dispose();
        (await invocation.ValidateAsync(Token)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionMetadata_passive_timeout_or_cancellation_physically_joins_noncooperative_exact_metadata_read(bool deadline)
    {
        using var f = new Fixture();
        using var root = f.Root();
        MetadataSource(f);
        var clock = new ManualTime { ExpectedDeadline = TimeSpan.FromSeconds(10) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken sourceToken = default;
        f.MetadataToken = value => sourceToken = value;
        f.BeforeRead = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance, overviewTime: clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var read = workspace.ReadSessionMetadataAsync(new(Guid.Parse(MetadataSubject)), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Token);
            if (deadline) { clock.FireDeadline(); }
            else { cancellation.Cancel(); }
            sourceToken.IsCancellationRequested.Should().BeTrue();
            read.IsCompleted.Should().BeFalse();
        }
        finally { release.TrySetResult(); }
        if (deadline) { await read.Invoking(async value => await value).Should().ThrowAsync<TimeoutException>(); }
        else { await read.Invoking(async value => await value).Should().ThrowAsync<OperationCanceledException>(); }
        f.RetainCalls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
    }
}
