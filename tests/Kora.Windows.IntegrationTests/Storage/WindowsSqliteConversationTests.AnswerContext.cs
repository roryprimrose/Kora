using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Theory]
    [InlineData(32768)]
    [InlineData(32769)]
    public async Task CompleteSubmittedContextCellAtStorageLimitRoundTripsButOverflowWritesNoAnswerTaskSuccessOrAudit(int bytes)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var selections = new List<ConversationHistorySelection>();
        for (var index = 0; index < 8; index++)
        {
            var source = await Submit(f, f.Request.SessionId, "earlier source marker");
            selections.Add(new(source.Message, ConversationHistoryRole.User));
        }
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        const string marker = "original private user marker:";
        var body = marker + new string('\u0800', ConversationText.MaximumUtf16Characters - marker.Length);
        var envelope = ConversationHistoryContext.CreateEnvelope(request, body, selections, f.Time.Now, f.Time.Now.AddSeconds(15));
        var option = new string('<', 128);
        var submitted = SubmittedAnswerContext.Capture(provenance, new(1),
            new(ProviderService.Local, new(option), new(1), new(option), new(option), new(option)),
            envelope, envelope.Serialize(), f.Time.Now)!;
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, new(1), 0, body, static () => true, f.Token);
        var dispatch = await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
        var cell = dispatch.Message with
        {
            Revision = 3, Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome,
            AssistantResponse = new(provenance, "a"), SubmittedContext = submitted,
        };
        var framing = JsonSerializer.SerializeToUtf8Bytes(cell).Length - 1;
        cell = cell with { AssistantResponse = new(provenance, new string('a', bytes - framing)) };
        cell.AssistantResponse!.Validate();
        submitted.Validate();
        submitted.CanonicalUtf8Bytes.Should().BeLessThan(ModelContextEnvelope.MaximumInputUtf8Bytes);
        JsonSerializer.SerializeToUtf8Bytes(cell).Length.Should().Be(bytes);
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(cell)).Should().Be(bytes);
        JsonSerializer.SerializeToUtf8Bytes(cell with { SubmittedContext = null }).Length
            .Should().BeLessThan(ConversationMessage.MaximumStorageUtf8Bytes);
        var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance,
            new(cell.AssistantResponse.Body, null)) { SubmittedContext = submitted };
        var audit = AuditText(f);
        var clock = await f.Store.ReadRetentionAsync(request.SessionId, f.Token);
        var hash = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        f.Time.Now = f.Time.Now.AddSeconds(1);
        if (bytes == ConversationMessage.MaximumStorageUtf8Bytes)
        {
            var completed = await f.Store.CompleteConversationExecutionAsync(request, result, static () => true, f.Token);
            completed.Message.Should().Be(cell);
            completed.Task.State.Should().Be(HostTaskState.Succeeded);
            completed.Task.Revision.Value.Should().Be(3);
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            (await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token)).Should().Be(cell);
        }
        else
        {
            await f.Store.Invoking(store => store.CompleteConversationExecutionAsync(request, result,
                static () => true, f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>()
                .WithMessage("The complete conversation cell exceeds 32768 UTF-8 bytes; nothing was retained partially.");
            (await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token)).Should().Be(dispatch.Message);
            (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token)).Should().Be(dispatch.Task);
            dispatch.Message.AssistantResponse.Should().BeNull();
            dispatch.Message.SubmittedContext.Should().BeNull();
            dispatch.Message.Outcome.Should().BeNull();
            (await f.Store.ReadRetentionAsync(request.SessionId, f.Token)).Should().Be(clock);
            AuditText(f).Should().Be(audit);
            SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(hash);
        }
        using var connection = f.OpenRaw();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT payload FROM conversation_messages WHERE request_id=$request;";
        read.Parameters.AddWithValue("$request", request.RequestId.Value.ToString("D"));
        var payload = (string)read.ExecuteScalar()!;
        Encoding.UTF8.GetByteCount(payload).Should().Be(bytes == 32768 ? 32768 : JsonSerializer.SerializeToUtf8Bytes(dispatch.Message).Length);
        JsonSerializer.Deserialize<ConversationMessage>(payload).Should().Be(bytes == 32768 ? cell : dispatch.Message);
        using var success = connection.CreateCommand();
        success.CommandText = """
            SELECT count(*) FROM security_audit_events
            WHERE json_extract(envelope,'$.Request.RequestId.Value')=$request
              AND json_extract(envelope,'$.Audit.ActionId')='conversation.answer'
              AND json_extract(envelope,'$.Audit.Outcome')=$outcome;
            """;
        success.Parameters.AddWithValue("$request", request.RequestId.Value.ToString("D"));
        success.Parameters.AddWithValue("$outcome", (int)SecurityAuditOutcome.Succeeded);
        ((long)success.ExecuteScalar()!).Should().Be(bytes == 32768 ? 1 : 0);
        AuditText(f).Should().NotContain(marker).And.NotContain(cell.AssistantResponse.Body).And.NotContain("earlier source marker");
    }

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
