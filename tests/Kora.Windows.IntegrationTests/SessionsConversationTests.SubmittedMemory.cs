using System.Text.Json;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
    [InlineData("retained", true)]
    [InlineData("disable", true)]
    [InlineData("forget", true)]
    [InlineData("edit", true)]
    [InlineData("profile-mismatch", true)]
    [InlineData("removed", true)]
    public async Task SubmittedMemorySurvivesRestartButUnavailableRevisionsAndSessionDeletionNeverRecallBodies(
        string state, bool deviceProfile = false)
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
                await PrepareMemoryContext(first.Model, deviceProfile);
                memory = first.Model.MemoryRecords.Single();
                first.Model.SelectMemoryContext([memory]);
                first.Model.MemoryContextRequest = "Question from the exact selected memory.";
                await first.Model.ReviewMemoryContextAsync();
                await first.Model.ConfirmMemoryContextAsync();
                retained = first.Model.ConversationMessages.Single();
                retained.SubmittedContext.Should().NotBeNull(first.Model.ConversationStatus);
                if (deviceProfile)
                {
                    retained.SubmittedContext!.ProfileMemorySources.Should().Equal(
                        new SubmittedProfileMemorySource(new(memory.Id), new(memory.Revision), new(memory.Scope.Identity)));
                }
                else
                {
                    retained.SubmittedContext!.MemorySources.Should().Equal(
                        new SubmittedMemorySource(new(memory.Id), new(memory.Revision), retained.Request.SessionId));
                }
                using var wire = JsonDocument.Parse(adapter.Wire!);
                var evidence = wire.RootElement.GetProperty("Evidence")[0];
                evidence.GetProperty("Id").GetProperty("Value").GetGuid().Should().Be(memory.Id);
                evidence.GetProperty("Revision").GetProperty("Value").GetInt64().Should().Be(memory.Revision);
                evidence.TryGetProperty("MemorySource", out _).Should().BeFalse();
                evidence.TryGetProperty("ProfileMemorySource", out _).Should().BeFalse();
                var metadata = JsonSerializer.Serialize(retained.SubmittedContext);
                metadata.Should().NotContain("private reviewed session decision").And.NotContain("ContentClass")
                    .And.NotContain("Lineage").And.NotContain("Receipt");
                first.Model.Close();
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var opened = new List<AdmittedDetailContent>();
            Func<CancellationToken, Task<AdmittedDetailContent>>? revalidate = null;
            await using var restarted = new Composition(f, (content, _, resolve, _) =>
            {
                opened.Add(content);
                revalidate = resolve;
                return Task.FromResult("Historical metadata observed");
            }, adapter: adapter, memoryContext: true);
            var model = restarted.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            var answer = model.ConversationMessages.Single();
            answer.Should().Be(retained);
            if (state is "disable" or "forget" or "edit")
            {
                var command = new MemoryCommand(state switch
                    {
                        "disable" => MemoryCommandOperation.Disable,
                        "edit" => MemoryCommandOperation.Edit,
                        _ => MemoryCommandOperation.Forget,
                    }, answer.Request.SessionId.Value, memory.Id, memory.Revision,
                    Candidate: state is "edit" ? new(Kora.Core.Memory.MemoryContentClass.Decision, "new private value") : null);
                if (deviceProfile)
                {
                    await restarted.Memories.ExecuteDeviceProfileAsync(command,
                        RequestOrigin.LocalUi, static () => true, f.Token);
                }
                else
                {
                    await restarted.Memories.ExecuteAsync(command,
                        RequestOrigin.LocalUi, static () => true, f.Token);
                }
            }
            var clock = await f.Store.ReadRetentionAsync(answer.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var tasks = f.Count("host_tasks");
            model.ComposerBody = "independent unsent input";
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            window.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(answer);
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>().Single(control =>
                string.Equals(control.Name, "OpenSubmittedAnswerContext", StringComparison.Ordinal)
                && control.DataContext is ConversationMessage message && message.Request == answer.Request);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await owner.DetailOperation;
            var detail = opened.Should().ContainSingle().Subject;
            detail.ConversationMessage.Should().Be(answer);
            detail.Source.Should().Contain(state is "retained" or "profile-mismatch" or "removed"
                    ? "exact enabled memory revision is retained" : "No newer memory substituted")
                .And.NotContain("private reviewed session decision").And.NotContain(answer.Body)
                .And.NotContain(answer.AssistantResponse!.Body);
            if (deviceProfile) { detail.Source.Should().Contain("Selected DEVICE-PROFILE memory"); }
            var reader = new Kora.Application.Presentation.DetailViewerState(detail, static () => true);
            reader.TryGetCopySource(true, true, out _).Should().BeFalse();
            reader.CanInteractWithFinalDocument.Should().BeFalse();
            (await revalidate!(f.Token)).IsSameSnapshot(detail).Should().BeTrue();
            model.ComposerBody.Should().Be("independent unsent input");
            adapter.Calls.Should().Be(1);
            (await f.Store.ReadRetentionAsync(answer.Request.SessionId, f.Token)).Should().Be(clock);
            WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            f.Count("host_tasks").Should().Be(tasks);
            if (state is "profile-mismatch" or "removed")
            {
                f.Mutate(state is "profile-mismatch"
                    ? "UPDATE memory_profile SET profile_id='" + Guid.NewGuid().ToString("D") + "';"
                    : "DELETE FROM reviewed_memory;");
                var hash = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
                await revalidate.Invoking(resolve => resolve!(f.Token)).Should().ThrowAsync<InvalidDataException>();
                SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(hash);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("host_tasks").Should().Be(tasks);
                adapter.Calls.Should().Be(1);
                model.ComposerBody = string.Empty;
                window.ClosePrivate();
                return;
            }
            if (state is "retained" && deviceProfile)
            {
                await restarted.Memories.ExecuteDeviceProfileAsync(new(MemoryCommandOperation.Disable,
                    answer.Request.SessionId.Value, memory.Id, memory.Revision),
                    RequestOrigin.LocalUi, static () => true, f.Token);
                var changed = await revalidate(f.Token);
                changed.IsSameSnapshot(detail).Should().BeFalse("a live native inspector must not keep asserting source availability");
                changed.Source.Should().Contain("No newer memory substituted");
            }
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
            f.Count("reviewed_memory").Should().Be(1, "profile retention is independent; Session records retain tombstones");
            window.ClosePrivate();
            model.Close();
        });
    }
}
