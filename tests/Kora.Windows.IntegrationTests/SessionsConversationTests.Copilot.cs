using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    internal static ProviderProfileTuple SelectedCopilotProfile => CopilotCatalogue.Profile;
    internal sealed class SelectedCopilotComposition(InteractionStorageFixture storage, ICopilotTurnRuntime runtime) : IAsyncDisposable
    {
        private readonly Composition inner = new(storage, copilot: runtime);
        internal Kora.Application.Hosting.SessionWorkspaceService Workspace => inner.Workspace;
        internal Kora.Application.Hosting.SessionConversationService Conversation => inner.Conversation;
        internal ModelTurnHost Turns => inner.Turns;
        internal HostedTransmissionReviewService Reviews => inner.Reviews;
        internal Kora.Core.Auditing.ISecurityAuditLog Audit => inner.Audit;
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("deny")]
    public async Task Native_copilot_text_stream_action_preserves_review_previews_and_final_only_retention(string terminal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var sdk = new CopilotSdkTurnRuntimeTests.Fixture(profile: CopilotCatalogue.Profile, time: f.Time);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sdk.Client.Stream = async (publish, token) =>
            {
                await publish("safe preview", token);
                entered.TrySetResult();
                await release.Task;
                return "safe preview complete";
            };
            await using var runtime = sdk.Runtime;
            await using var composed = new Composition(f, copilot: runtime);
            composed.Reviews.Offered += review =>
            {
                using var presentation = review.Presentation();
                composed.Reviews.Confirm(review, terminal is not "deny", RequestOrigin.LocalUi).Should().BeTrue();
                presentation.Complete(HostOperationOutcome.Completed);
            };
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            Task operation = Task.CompletedTask;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "original native Copilot stream";
                window.FindControl<Button>("SubmitCopilotAnswerStream")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                operation = window.ConversationOperation;
                if (terminal is not "deny")
                {
                    (await Task.WhenAny(entered.Task, operation).WaitAsync(f.Token)).Should().BeSameAs(entered.Task,
                        model.ConversationStatus);
                    model.ProvisionalAnswer.Should().Be("safe preview");
                    operation.IsCompleted.Should().BeFalse();
                    if (terminal is "cancel")
                    {
                        await model.CancelRunningConversationAsync();
                        model.ProvisionalAnswer.Should().BeEmpty();
                        operation.IsCompleted.Should().BeFalse();
                        runtime.IsQuiescent.Should().BeFalse();
                    }
                    release.TrySetResult();
                }
                await operation;
                model.ProvisionalAnswer.Should().BeEmpty();
                var stored = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                stored.Body.Should().Be("original native Copilot stream");
                stored.AssistantResponse?.Body.Should().Be(terminal is "complete" ? "safe preview complete" : null);
                stored.Outcome.Should().Be(terminal is "complete" ? ModelTurnOutcome.Succeeded
                    : terminal is "cancel" ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.DeniedEgress);
                sdk.Network.Sends.Should().Be(terminal is "deny" ? 0 : 1);
                sdk.Client.Configuration!.Events.Streaming.Should().BeTrue();
                composed.Turns.IsQuiescent.Should().BeTrue();
                runtime.IsQuiescent.Should().BeTrue();
            }
            finally
            {
                release.TrySetResult();
                await operation;
                owner.Revoke();
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(61, true, ModelTurnOutcome.Succeeded)]
    [InlineData(119, true, ModelTurnOutcome.Succeeded)]
    [InlineData(61, false, ModelTurnOutcome.DeniedEgress)]
    [InlineData(120, true, ModelTurnOutcome.Denied)]
    public async Task Native_copilot_review_beyond_sdk_default_wait_preserves_exact_request_and_durable_answer(
        int seconds, bool approve, ModelTurnOutcome expected)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var sdk = new CopilotSdkTurnRuntimeTests.Fixture(profile: CopilotCatalogue.Profile, time: f.Time);
        var clock = new CopilotSdkTurnRuntimeTests.SdkWaitClock();
        sdk.Client.WaitClock = clock;
        await using var runtime = sdk.Runtime;
        await using var composed = new Composition(f, copilot: runtime);
        var offered = new TaskCompletionSource<CopilotTransmissionReview>(TaskCreationOptions.RunContinuationsAsynchronously);
        composed.Reviews.Offered += review => offered.TrySetResult(review);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var submitted = composed.Conversation.SubmitCopilotAsync(target, "original hosted request", () => true, f.Token);
        var review = await offered.Task.WaitAsync(f.Token);
        sdk.Network.Sends.Should().Be(0);
        clock.Advance(TimeSpan.FromSeconds(seconds));
        f.Time.Now += TimeSpan.FromSeconds(seconds);
        f.Time.MonotonicTicks += TimeSpan.FromSeconds(seconds).Ticks;
        using (var presentation = review.Presentation())
        {
            composed.Reviews.Confirm(review, approve, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        }
        var receipt = await submitted;
        var message = receipt.Message;
        message.Body.Should().Be("original hosted request");
        message.Request.Should().Be(review.Transmission.Provenance.Request);
        var succeeds = expected == ModelTurnOutcome.Succeeded;
        message.Outcome.Should().Be(expected);
        if (succeeds) { message.AssistantResponse!.Body.Should().Be("genuine-sdk-boundary-answer"); }
        else { message.AssistantResponse.Should().BeNull(); }
        var stored = await f.Store.ReadConversationMessageAsync(message.Request.SessionId, message.Request.RequestId, f.Token);
        stored.Should().Be(message);
        sdk.Client.Prompt.Should().Contain("original hosted request");
        sdk.Client.WaitTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        sdk.Network.Sends.Should().Be(succeeds ? 1 : 0);
        composed.Turns.IsQuiescent.Should().BeTrue();
        runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_copilot_retains_original_request_and_only_a_separately_approved_answer(bool approve)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime();
        await using var composed = new Composition(f, copilot: runtime);
        composed.Reviews.Offered += review =>
        {
            Encoding.UTF8.GetString(review.Transmission.Body.AsSpan()).Should().Contain("original hosted request");
            review.Transmission.Profile.Should().Be(CopilotCatalogue.Profile);
            using var presentation = review.Presentation();
            composed.Reviews.Confirm(review, approve, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var receipt = await composed.Conversation.SubmitCopilotAsync(target, "original hosted request", () => true, f.Token);
        var message = receipt.Message;
        message.Body.Should().Be("original hosted request");
        message.Outcome.Should().Be(approve ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        if (approve) { message.AssistantResponse!.Body.Should().Be("genuine hosted boundary answer"); }
        else { message.AssistantResponse.Should().BeNull(); }
        var stored = await f.Store.ReadConversationMessageAsync(message.Request.SessionId, message.Request.RequestId, f.Token);
        stored.Should().Be(message);
        runtime.Request.Should().Be(message.Request);
        runtime.Sends.Should().Be(approve ? 1 : 0);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    private sealed class CopilotConversationRuntime : ICopilotTurnRuntime
    {
        public bool IsQuiescent => true;
        internal HostRequest? Request { get; private set; }
        internal int Sends { get; private set; }
        public async Task<CopilotTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission, CancellationToken token)
        {
            Request = provenance.Request;
            if (!await admitTransmission(new(provenance, profile, new Uri("https://api.githubcopilot.com/chat/completions"),
                context), token)) { return new(ModelTurnOutcome.DeniedEgress); }
            Sends++;
            return new(ModelTurnOutcome.Succeeded, "genuine hosted boundary answer");
        }
    }

    private sealed class CopilotCatalogue(InteractionStorageFixture fixture) : IProviderCapabilityCatalogue
    {
        internal static ProviderProfileTuple Profile { get; } = new(ProviderService.Copilot, new("explicit-account"), new(1),
            new("model-1"), new("default"), new("default"));
        public Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderCatalogueObservation(LocalProviderInstallation.Missing,
                Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service =>
                    new ProviderCapabilityCatalogue(service, service == ProviderService.Copilot
                        ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable,
                        service == ProviderService.Copilot ? Profile.Account : null, new(1), fixture.Time.Now,
                        fixture.Time.Now.AddMinutes(5), service == ProviderService.Copilot
                            ? [new(Profile.Model, "Test model", ModelProviderSelection.CopilotCandidate,
                                [new(Profile.Reasoning, "Default", true)], [new(Profile.Context, "Default", true)])] : [])).ToArray()));
    }
}
