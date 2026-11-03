using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Providers;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("retained")]
    [InlineData("disable")]
    [InlineData("forget")]
    public async Task SubmittedMemorySurvivesRestartButUnavailableRevisionsAndSessionDeletionNeverRecallBodies(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Local, [HandoffCatalogue.Local]));
            var adapter = new MemoryAnswerAdapter();
            ConversationMessage retained;
            MemorySummary memory;
            await using (var first = new Composition(f, adapter: adapter, memoryContext: true,
                localRequestCatalogue: new HandoffCatalogue(f)))
            {
                await PrepareMemoryContext(first.Model);
                memory = first.Model.MemoryRecords.Single();
                first.Model.SelectMemoryContext([memory]);
                first.Model.MemoryContextRequest = "Question from the exact selected memory.";
                await first.Model.ReviewMemoryContextAsync();
                await first.Model.ConfirmMemoryContextAsync();
                retained = first.Model.ConversationMessages.Single();
                retained.SubmittedContext.Should().NotBeNull(first.Model.ConversationStatus);
                retained.SubmittedContext!.MemorySources.Should().Equal(
                    new SubmittedMemorySource(new(memory.Id), new(memory.Revision), retained.Request.SessionId));
                using var wire = JsonDocument.Parse(adapter.Wire!);
                var evidence = wire.RootElement.GetProperty("Evidence")[0];
                evidence.GetProperty("Id").GetProperty("Value").GetGuid().Should().Be(memory.Id);
                evidence.GetProperty("Revision").GetProperty("Value").GetInt64().Should().Be(memory.Revision);
                evidence.TryGetProperty("MemorySource", out _).Should().BeFalse();
                first.Model.Close();
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var opened = new List<AdmittedDetailContent>();
            await using var restarted = new Composition(f, (content, _, _, _) =>
            {
                opened.Add(content);
                return Task.FromResult("Historical metadata observed");
            }, adapter: adapter, memoryContext: true);
            var model = restarted.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            var answer = model.ConversationMessages.Single();
            answer.Should().Be(retained);
            if (state is not "retained")
            {
                await restarted.Memories.ExecuteAsync(new(state is "disable"
                        ? MemoryCommandOperation.Disable : MemoryCommandOperation.Forget,
                    answer.Request.SessionId.Value, memory.Id, memory.Revision),
                    RequestOrigin.LocalUi, static () => true, f.Token);
            }
            var clock = await f.Store.ReadRetentionAsync(answer.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var tasks = f.Count("host_tasks");
            model.ComposerBody = "independent unsent input";
            await model.OpenConversationDetailAsync(answer, submittedContext: true);
            var detail = opened.Should().ContainSingle().Subject;
            detail.ConversationMessage.Should().Be(answer);
            detail.Source.Should().Contain(state is "retained"
                    ? "exact enabled memory revision is retained" : "No newer memory substituted")
                .And.NotContain("private reviewed session decision").And.NotContain(answer.Body)
                .And.NotContain(answer.AssistantResponse!.Body);
            model.ComposerBody.Should().Be("independent unsent input");
            adapter.Calls.Should().Be(1);
            (await f.Store.ReadRetentionAsync(answer.Request.SessionId, f.Token)).Should().Be(clock);
            WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            f.Count("host_tasks").Should().Be(tasks);
            model.ComposerBody = string.Empty;
            var target = model.SelectedSessionRecord!;
            using var root = HostActivity.BeginRoot(
                new(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            var preview = await restarted.Workspace.PreviewDispositionAsync(target.Authority.SessionId,
                target.Authority.Generation, target.Metadata?.Revision.Value ?? 0, f.Token);
            await restarted.Workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, static () => true, f.Token);
            await f.Store.Invoking(store => store.ReadSubmittedContextAvailabilityAsync(answer, f.Token).AsTask())
                .Should().ThrowAsync<InvalidOperationException>();
            f.Count("conversation_messages").Should().Be(0);
            model.Close();
        });
    }
}
