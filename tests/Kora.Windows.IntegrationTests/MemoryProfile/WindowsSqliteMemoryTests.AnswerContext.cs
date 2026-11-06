using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Providers;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteMemoryTests
{
    [Theory]
    [InlineData(32768)]
    [InlineData(32769)]
    public async Task ProfileAnswerCompleteCellIncludesExactMetadataAtStorageLimitAndRejectsOverflowAtomically(int bytes)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var service = ProfileService(f);
        var memory = await AdmitProfile(f, service);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var metadataRevision = target.Metadata?.Revision.Value ?? 0;
        var request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var body = new string('\u0800', ConversationText.MaximumUtf16Characters);
        var source = new SubmittedProfileMemorySource(memory.Id, memory.Revision, memory.Lineage.Profile);
        var envelope = new ModelContextEnvelope(request, SessionMemoryContextPolicy.Policy, "question",
            [new(new(source.Id.Value), request, source.Revision, memory.Candidate!.Value!, ModelEvidenceDisclosure.LocalOnly)
                { ProfileMemorySource = source }], f.Time.Now, f.Time.Now.AddSeconds(15), ModelToolMode.None);
        var option = new string('<', 128);
        var submitted = SubmittedAnswerContext.Capture(provenance, target.Authority.Generation,
            new(ProviderService.Local, new(option), new(1), new(option), new(option), new(option)),
            envelope, envelope.Serialize(), f.Time.Now)!;
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, target.Authority.Generation, metadataRevision, body, static () => true, f.Token);
        var dispatch = await f.Store.BeginConversationExecutionAsync(request, provenance, metadataRevision, static () => true, f.Token);
        var cell = dispatch.Message with
        {
            Revision = 3, Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome,
            AssistantResponse = new(provenance, "a"), SubmittedContext = submitted,
        };
        var framing = JsonSerializer.SerializeToUtf8Bytes(cell).Length - 1;
        cell = cell with { AssistantResponse = new(provenance, new string('a', bytes - framing)) };
        JsonSerializer.SerializeToUtf8Bytes(cell).Length.Should().Be(bytes);
        cell.AssistantResponse!.Validate();
        submitted.Version.Should().Be(3);
        submitted.ProfileMemorySources.Should().Equal(source);
        JsonSerializer.Serialize(submitted).Should().NotContain(memory.Candidate.Value).And.NotContain("Lineage");
        var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance,
            new(cell.AssistantResponse.Body, null)) { SubmittedContext = submitted };
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var hash = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        if (bytes == ConversationMessage.MaximumStorageUtf8Bytes)
        {
            var completed = await f.Store.CompleteConversationExecutionAsync(request, result, static () => true, f.Token);
            completed.Message.Should().Be(cell);
            completed.Task.State.Should().Be(HostTaskState.Succeeded);
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            (await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token)).Should().Be(cell);
            (await f.Store.ReadSubmittedContextAvailabilityAsync(cell, f.Token)).Should()
                .Equal(SubmittedHistoryAvailability.RetainedRevision);
        }
        else
        {
            await f.Store.Invoking(store => store.CompleteConversationExecutionAsync(request, result,
                static () => true, f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>()
                .WithMessage("The complete conversation cell exceeds 32768 UTF-8 bytes; nothing was retained partially.");
            (await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token)).Should().Be(dispatch.Message);
            (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token)).Should().Be(dispatch.Task);
            WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(hash);
        }
    }
}
