using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalHandoffIntentCannotDispatchAnotherProviderOrAnExpiredContext(bool expire)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var local = new LocalUnavailableAdapter();
        var hosted = new CopilotConversationRuntime();
        var catalogue = new HandoffCatalogue(f);
        var atSuccessor = new TaskCompletionSource<HostRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        catalogue.BeforeObservation = async token =>
        {
            var request = HostActivity.RequireCurrent().Request;
            if (local.Request is { } original && request != original && !atSuccessor.Task.IsCompleted)
            {
                atSuccessor.TrySetResult(request);
                await release.Task.WaitAsync(token);
            }
        };
        await using var composed = new Composition(f, adapter: local, copilot: hosted, localFirst: true,
            localFirstCatalogue: catalogue);
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composed.LocalHandoff!.ReviewRequested += () => presented.TrySetResult();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var running = composed.LocalHandoff.RunAsync(target, "exact local original", static () => true, f.Token);
        await presented.Task.WaitAsync(f.Token);
        var offer = (await composed.HandoffPresentation!.ReadPending(RequestOrigin.LocalUi, static () => true, f.Token)).Single();
        var review = composed.HandoffPresentation.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(f.Token);
        var deciding = review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, f.Token);
        try
        {
            var successor = await atSuccessor.Task.WaitAsync(f.Token);
            var retained = await f.Store.ReadConversationMessageAsync(successor.SessionId, successor.RequestId, f.Token);
            retained.Outcome.Should().BeNull();
            (await f.Store.ReadTaskAsync(successor.SessionId, successor.TaskId, f.Token))!.Task.State.Should().Be(HostTaskState.IntentRecorded);
            var selection = expire ? offer.Destination : ModelProviderSelection.OllamaCandidate;
            if (expire)
            {
                f.Time.Now = retained.Handoff!.ExpiresAt;
                selection.Should().Be(retained.Handoff.Destination);
            }
            else { selection.Should().NotBe(retained.Handoff!.Destination); }
            using (var attempt = HostActivity.BeginRoot(successor, HostActivityLayer.Application, HostOperation.Request))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await f.Store.BeginConversationExecutionAsync(successor,
                        new(new(Guid.NewGuid()), successor, selection), 0, static () => true, f.Token));
            }
            (await f.Store.ReadConversationMessageAsync(successor.SessionId, successor.RequestId, f.Token)).Should().Be(retained);
            hosted.Sends.Should().Be(0);
        }
        finally { release.TrySetResult(); }
        var result = await running;
        await deciding;
        await review.DisposeAsync();
        result.Message.AssistantResponse.Should().BeNull();
        hosted.Sends.Should().Be(0);
        f.Count("conversation_messages").Should().Be(2);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }
}
