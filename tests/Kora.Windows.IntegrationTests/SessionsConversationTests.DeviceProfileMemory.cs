using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeProfileMemoryCompletesCrossConversationExplicitReviewAndOneShotLocalAnswer(bool qualified)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = qualified ? new MemoryAnswerAdapter() : null;
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true);
            var model = composition.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.DeviceProfileMemory = true;
                model.MemoryClass = MemoryContentClass.ResponsePreference;
                model.MemoryDraft = "Prefer short explicit examples";
                await model.ProposeMemoryAsync();
                var proposed = model.MemoryRecords.Single();
                proposed.Scope.Kind.Should().Be(MemoryScopeKind.DeviceProfile);
                model.SelectMemory(proposed);
                await model.InspectMemoryAsync();
                model.MemoryDetail.Should().Contain("Prefer short explicit examples").And.Contain("DeviceProfile");
                await model.ReviewMemoryAsync(true);
                f.Count("reviewed_memory").Should().Be(0);
                await model.AdmitMemoryAsync();
                var original = model.SelectedSessionRecord!.Authority;
                var later = await composition.Workspace.CreateAsync(new SessionName("Later conversation"), RequestOrigin.LocalUi, f.Token);
                await composition.Workspace.ChangeLifecycleAsync(original.SessionId, original.Generation, false, RequestOrigin.LocalUi, f.Token);
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == later.Authority.SessionId));
                await model.ReadConversationAsync();
                await model.ListMemoriesAsync();
                var memory = model.MemoryRecords.Single();
                memory.Id.Should().Be(proposed.Id);
                model.SelectMemoryContext([memory]);
                model.MemoryContextRequest = "Explain the approved preference.";
                await model.ReviewMemoryContextAsync();
                model.CanConfirmMemoryContext.Should().BeTrue(model.ConversationStatus);
                model.MemoryContextDetail.Should().Contain("Prefer short explicit examples")
                    .And.Contain("ResponsePreference").And.Contain("DeviceProfile");
                (adapter?.Calls ?? 0).Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
                await model.ConfirmMemoryContextAsync();
                var retained = (await f.Store.ReadConversationPageAsync(later.Authority.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be("Explain the approved preference.");
                retained.AssistantResponse?.Body.Should().Be(qualified ? "Answer from reviewed session fact." : null);
                model.MemoryContextOutcome.Should().Contain(qualified ? "ASSISTANT" : "HOST Unavailable");
                await model.ConfirmMemoryContextAsync();
                f.Count("conversation_messages").Should().Be(1);
                if (adapter is not null)
                {
                    adapter.Calls.Should().Be(1);
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                }
                model.DeviceProfileMemory = false;
                await model.ListMemoriesAsync();
                model.MemoryRecords.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(1);
                (await f.Tasks.ReadIncompleteAsync(100, f.Token)).Should().BeEmpty();
            }
            finally { window.ClosePrivate(); }
        });
    }
}
