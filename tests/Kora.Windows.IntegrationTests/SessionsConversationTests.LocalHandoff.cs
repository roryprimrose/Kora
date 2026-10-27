using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifiedLocalUnavailableUsesActualProducerSqliteNativeReviewAndIndependentPhysicalEgress(bool approveEgress)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var local = new LocalUnavailableAdapter();
            var hosted = new CopilotConversationRuntime();
            await using var composed = new Composition(f, adapter: local, copilot: hosted, localFirst: true);
            var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            composed.LocalHandoff!.ReviewRequested += () => presented.TrySetResult();
            composed.Reviews.Offered += exact =>
            {
                hosted.Sends.Should().Be(0);
                exact.Transmission.Profile.Should().Be(CopilotCatalogue.Profile);
                exact.Transmission.Provenance.Request.SessionId.Should().Be(f.Request.SessionId);
                Encoding.UTF8.GetString(exact.Transmission.Body.AsSpan()).Should().Contain("private original local-first input");
                using var presentation = exact.Presentation();
                composed.Reviews.Confirm(exact, approveEgress, RequestOrigin.LocalUi).Should().BeTrue();
                presentation.Complete(HostOperationOutcome.Completed);
            };
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                window.FindControl<TextBox>("ConversationComposer")!.Text = "private original local-first input";
                window.FindControl<Button>("SubmitLocalFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await presented.Task.WaitAsync(f.Token);
                var offers = await composed.HandoffPresentation!.ReadPending(RequestOrigin.LocalUi, static () => true, f.Token);
                var offer = offers.Should().ContainSingle().Subject;
                offer.LocalUnavailable.Should().NotBeNull("only the production runtime owner can issue this proof");
                offer.LocalUnavailable!.IsCurrent.Should().BeTrue();
                local.Calls.Should().Be(1);
                local.IsQuiescent.Should().BeTrue();
                f.Count("conversation_messages").Should().Be(1, "review preparation must not create the successor");
                hosted.Sends.Should().Be(0);
                await using var review = composed.HandoffPresentation.Open(offer, RequestOrigin.LocalUi, static () => true)!;
                await review.Refresh(f.Token);
                review.Preview.Should().Contain("private original local-first input");
                await review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, f.Token);
                await window.ConversationOperation;
                review.Completion.Should().NotBeNull();
                var messages = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages;
                messages.Should().HaveCount(2);
                messages[0].Outcome.Should().Be(ModelTurnOutcome.Unavailable);
                messages[0].AssistantResponse.Should().BeNull();
                messages[1].Request.RequestId.Should().NotBe(messages[0].Request.RequestId);
                messages[1].Request.TaskId.Should().NotBe(messages[0].Request.TaskId);
                messages[1].Request.Origin.Should().Be(messages[0].Request.Origin);
                messages[1].Handoff!.Source.Should().Be(messages[0].Execution);
                messages[1].Handoff!.ExpiresAt.Should().Be(offer.Context.ExpiresAt);
                messages[1].Handoff!.Profile.Should().Be(CopilotCatalogue.Profile);
                messages[1].Outcome.Should().Be(approveEgress ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
                messages[1].AssistantResponse?.Body.Should().Be(approveEgress ? "genuine hosted boundary answer" : null);
                hosted.Sends.Should().Be(approveEgress ? 1 : 0);
                composed.Turns.IsQuiescent.Should().BeTrue();
                (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
                var replayRequest = new HostRequest(new(Guid.NewGuid()), offer.Context.Request.SessionId,
                    new(Guid.NewGuid()), offer.Context.Request.Origin);
                var replayContext = new ModelContextEnvelope(replayRequest, offer.Context.SystemPolicy,
                    offer.Context.UserRequest, [], offer.Context.CreatedAt, offer.Context.ExpiresAt, offer.Context.ToolMode);
                using (var replay = HostActivity.BeginRoot(offer.Context.Request, HostActivityLayer.Application, HostOperation.Request))
                {
                    await Assert.ThrowsAsync<InvalidDataException>(async () =>
                        await f.Store.CommitVerifiedLocalHandoffAsync(offer.LocalUnavailable, offer.Question,
                            offer.Context, replayContext, offer.Target!.Profile, offer.Target.Selection,
                            offer.Target.Session, static () => true, f.Token));
                }
                f.Count("conversation_messages").Should().Be(2, "retained lineage or repeated approval cannot create another intent");
                hosted.Sends.Should().Be(approveEgress ? 1 : 0);
                using var connection = f.OpenRaw();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT group_concat(envelope) FROM security_audit_events;";
                var audit = (string)command.ExecuteScalar()!;
                audit.Should().NotContain("private original local-first input").And.NotContain("genuine hosted boundary answer");
            }
            finally { window.ClosePrivate(); }
        });
    }

    private sealed class LocalUnavailableAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal HostRequest? Request { get; private set; }
        internal ModelTurnOutcome Outcome { get; set; } = ModelTurnOutcome.Unavailable;
        internal bool JoinConfirmed { get; set; } = true;
        internal TaskCompletionSource<ModelProviderReply>? Pending { get; set; }
        public bool IsQuiescent => JoinConfirmed && Pending?.Task.IsCompleted != false;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            Request = provenance.Request;
            return Pending?.Task ?? Task.FromResult(new ModelProviderReply(Outcome, provenance.Selection,
                Outcome == ModelTurnOutcome.Succeeded ? new("genuine local answer", null) : null,
                Reason: ModelTurnReason.ProviderOutcome));
        }
    }

    private sealed class HandoffPolicyGate(ISessionWorkspaceAccess access) : IHostAuthorizationPolicyGate
    {
        public T WithCurrent<T>(HostAuthorizationPolicy baseline, Func<HostAuthorizationPolicy, T> commit) =>
            commit(baseline with { IsUnlocked = access.CanControl });
    }

    private sealed class HandoffCatalogue(InteractionStorageFixture fixture) : IProviderCapabilityCatalogue
    {
        internal static ProviderProfileTuple Local { get; } = CopilotCatalogue.Profile with { Service = ProviderService.Local };
        internal Func<CancellationToken, Task>? BeforeObservation { get; set; }
        public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            if (BeforeObservation is { } before) { await before(cancellationToken); }
            cancellationToken.ThrowIfCancellationRequested();
            return new ProviderCatalogueObservation(LocalProviderInstallation.Installed,
                Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service =>
                {
                    var profile = service == ProviderService.Local ? Local : CopilotCatalogue.Profile;
                    var current = service is ProviderService.Local or ProviderService.Copilot;
                    return new ProviderCapabilityCatalogue(service,
                        current ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable,
                        current ? profile.Account : null, new(1), fixture.Time.Now, fixture.Time.Now.AddMinutes(5),
                        current ? [new(profile.Model, "Exact configured model",
                            service == ProviderService.Local ? ModelProviderSelection.OllamaCandidate : ModelProviderSelection.CopilotCandidate,
                            [new(profile.Reasoning, "Default", true)], [new(profile.Context, "Default", true)])] : []);
                }).ToArray());
        }
    }
}
