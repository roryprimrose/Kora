using System.Security.Cryptography;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Theory]
    [InlineData("retained")]
    [InlineData("missing")]
    [InlineData("revision")]
    [InlineData("malformed")]
    [InlineData("removed-answer")]
    [InlineData("cancel")]
    public async Task CurrentHistoryAvailabilityReadsMetadataOnlyAndNeverReconstructsMissingChangedOrCorruptSources(string state)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var source = await Submit(f, f.Request.SessionId, "source body, not a metadata field");
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var context = ConversationHistoryContext.CreateEnvelope(request, "follow-up",
            [new(source.Message, ConversationHistoryRole.User)], f.Time.Now, f.Time.Now.AddSeconds(15));
        var profile = new ProviderProfileTuple(ProviderService.Local, new("device"), new(1), new("model"), new("default"), new("default"));
        var submitted = SubmittedAnswerContext.Capture(provenance, new(1), profile, context, context.Serialize(), f.Time.Now);
        ConversationSubmissionReceipt answer;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "follow-up", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            answer = await f.Store.CompleteConversationExecutionAsync(request,
                new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance, new("genuine final", null))
                { SubmittedContext = submitted }, static () => true, f.Token);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var exact = await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
        exact.Should().Be(answer.Message);
        var clock = await f.Store.ReadRetentionAsync(request.SessionId, f.Token);
        if (state is "missing" or "removed-answer")
        {
            using var connection = f.OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM conversation_messages WHERE request_id=$request;";
            command.Parameters.AddWithValue("$request", (state is "missing" ? source.Message.Request.RequestId : request.RequestId).Value.ToString("D"));
            command.ExecuteNonQuery().Should().Be(1);
        }
        if (state is "revision" or "malformed")
        {
            using var connection = f.OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = state is "revision"
                ? "UPDATE conversation_messages SET revision=revision+1 WHERE request_id=$request;"
                : "UPDATE conversation_messages SET payload='malformed' WHERE request_id=$request;";
            command.Parameters.AddWithValue("$request", source.Message.Request.RequestId.Value.ToString("D"));
            command.ExecuteNonQuery().Should().Be(1);
        }
        var audit = AuditText(f);
        var dataHash = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (state is "cancel") { cancellation.Cancel(); }
        var observe = () => f.Store.ReadSubmittedHistoryAvailabilityAsync(exact, cancellation.Token).AsTask();
        if (state is "cancel") { await observe.Should().ThrowAsync<OperationCanceledException>(); }
        else if (state is "malformed") { await observe.Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(); }
        else if (state is "removed-answer" or "missing" or "revision")
        { await observe.Should().ThrowAsync<InvalidDataException>(); }
        else
        {
            (await observe()).Should().Equal(state is "retained"
                ? SubmittedHistoryAvailability.RetainedRevision : SubmittedHistoryAvailability.Unavailable);
        }
        if (state is "retained" or "cancel")
        { (await f.Store.ReadRetentionAsync(request.SessionId, f.Token)).Should().Be(clock); }
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(dataHash);
        AuditText(f).Should().Be(audit);
    }

    [Fact]
    public async Task MissingHistoryBeforeTerminalCommitCannotRetainMetadataOrSuccessfulAnswer()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var source = await Submit(f, f.Request.SessionId, "earlier source");
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var context = ConversationHistoryContext.CreateEnvelope(request, "follow-up",
            [new(source.Message, ConversationHistoryRole.User)], f.Time.Now, f.Time.Now.AddSeconds(15));
        var submitted = SubmittedAnswerContext.Capture(provenance, new(1),
            new(ProviderService.Local, new("device"), new(1), new("model"), new("default"), new("default")),
            context, context.Serialize(), f.Time.Now);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, new(1), 0, "follow-up", static () => true, f.Token);
        await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
        var audit = AuditText(f);
        using (var connection = f.OpenRaw())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM conversation_messages WHERE request_id=$request;";
            command.Parameters.AddWithValue("$request", source.Message.Request.RequestId.Value.ToString("D"));
            command.ExecuteNonQuery().Should().Be(1);
        }
        await f.Store.Invoking(store => store.CompleteConversationExecutionAsync(request,
            new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance, new("must not retain", null))
            { SubmittedContext = submitted }, static () => true, f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
        using var raw = f.OpenRaw();
        using var read = raw.CreateCommand();
        read.CommandText = "SELECT payload FROM conversation_messages WHERE request_id=$request;";
        read.Parameters.AddWithValue("$request", request.RequestId.Value.ToString("D"));
        var actual = JsonSerializer.Deserialize<ConversationMessage>((string)read.ExecuteScalar()!)!;
        actual.AssistantResponse.Should().BeNull();
        actual.SubmittedContext.Should().BeNull();
        actual.Outcome.Should().BeNull();
        AuditText(f).Should().Be(audit);
    }
}
