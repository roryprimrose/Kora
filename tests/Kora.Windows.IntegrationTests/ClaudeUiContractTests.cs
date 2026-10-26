using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Diagnostics;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClaudeUiContractTests
{
    [Fact]
    public async Task Shipped_unavailable_setup_disables_actual_native_credential_control_before_network()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = NativeUxTestFixture.Create(Path.GetTempPath());
            await native.InitializeAsync();
            var f = new ClaudeAccountBrokerTests.Fixture { Approved = false };
            await using var broker = f.Create();
            await using var admission = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            using var controller = new ClaudeAccountWindowController(native.Main, broker, admission,
                NullLogger<ClaudeAccountWindowController>.Instance);
            native.Main.ConfigureClaudeAccount();
            await controller.Operation;
            Dispatcher.UIThread.RunJobs();
            var opened = controller.PresentedWindow!;
            opened.Should().NotBeNull();
            var credential = opened.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name is "ClaudeCredential");
            credential.IsEnabled.Should().BeFalse();
            credential.Text.Should().BeNullOrEmpty();
            f.Requests.Should().BeEmpty();
            opened.Close();
            controller.PresentedWindow.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_exact_disclosure_has_no_affirmative_default_and_close_denies(bool approve)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = NativeUxTestFixture.Create(Path.GetTempPath());
            await native.InitializeAsync();
            var f = new ClaudeAccountBrokerTests.Fixture();
            var reviews = new HostedTransmissionReviewService(f, f.Time);
            using var controller = new ClaudeTransmissionWindowController(native.Main, reviews,
                NullLogger<ClaudeTransmissionWindowController>.Instance);
            var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var now = f.Time.GetUtcNow();
            var context = new ModelContextEnvelope(request, "policy", "complete body", [], now, now.AddMinutes(1), ModelToolMode.None);
            var profile = new Kora.Core.Providers.ProviderProfileTuple(Kora.Core.Providers.ProviderService.Claude,
                f.Lease.Account, new(1), new("exact-model"), new("disabled"), new("8192"));
            var authorization = reviews.PrepareClaudeTurn(context, profile);
            var transmission = new ClaudeTransmission(new(new(Guid.NewGuid()), request,
                new(ModelProviderIdentity.Claude, new(Guid.NewGuid()), new(1))), profile,
                new("https://api.anthropic.com/v1/messages"), new byte[] { 123, 125 }, []);
            var decision = reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(true),
                TestContext.Current.CancellationToken).AsTask();
            Dispatcher.UIThread.RunJobs();
            var window = controller.PresentedWindow!;
            var deny = window.GetLogicalDescendants().OfType<Button>().Single(control => control.Name is "ClaudeDeny");
            var consent = window.GetLogicalDescendants().OfType<Button>().Single(control => control.Name is "ClaudeApprove");
            deny.IsDefault.Should().BeTrue();
            consent.IsDefault.Should().BeFalse();
            decision.IsCompleted.Should().BeFalse();
            if (approve) { consent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            else { window.Close(); }
            (await decision).Should().Be(approve);
            controller.PresentedWindow.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_native_Claude_submit_action_uses_same_retained_operation_without_ordinary_fallback(bool observations)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new HostedClaudeSessionRequestTests.Composition(f);
            await composition.InitializeAsync();
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(composition.Workspace,
                new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), composition.Access, f.Time,
                    NullLogger<DurableEvidenceQuery>.Instance), composition.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: composition.Conversation);
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                const string original = "  native explicit Claude body  ";
                window.FindControl<TextBox>("ConversationComposer")!.Text = original;
                if (observations) { window.FindControl<TextBox>("ClaudeObservationIds")!.Text = "application.get_version"; }
                window.FindControl<Button>(observations ? "SubmitClaudeObservations" : "SubmitClaudeConversation")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationStatus.Should().Contain("DeniedEgress");
                (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single().Body.Should().Be(original);
                composition.Http.Requests.Should().OnlyContain(request => string.Equals(request.Method, "GET", StringComparison.Ordinal));
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualNativeObservationActionReturnsGenuineAnswerOrFencesChangedAndRestoredScope(bool changeScope)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new HostedClaudeSessionRequestTests.Composition(f);
            await composition.InitializeAsync();
            var sink = new WindowsSqliteEvidenceSink(f.Paths); sink.Initialize();
            var model = new SessionsViewModel(composition.Workspace,
                new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), composition.Access, f.Time,
                    NullLogger<DurableEvidenceQuery>.Instance), composition.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: composition.Conversation);
            var window = new SessionsWindow(model); window.Show();
            var messages = 0;
            composition.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
                ? """{"input_tokens":25}""" : ++messages == 1
                    ? HostedClaudeSessionRequestTests.Proposal(HostedClaudeSessionRequestTests.VersionProposal) : ClaudeMessagesRuntimeTests.Answer);
            var offers = 0;
            composition.Reviews.ClaudeOffered += review =>
            {
                using var presentation = review.Presentation();
                if (++offers == 3 && changeScope)
                {
                    var original = model.ClaudeObservationIds;
                    model.ClaudeObservationIds = "runtime.list";
                    model.ClaudeObservationIds = original;
                }
                composition.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
            };
            try
            {
                await model.RefreshAsync(); await model.SelectAsync(model.Sessions.Single()); await model.ReadConversationAsync();
                window.FindControl<TextBox>("ClaudeObservationIds")!.Text = "application.get_version";
                window.FindControl<TextBox>("ConversationComposer")!.Text = "native original";
                window.FindControl<Button>("SubmitClaudeObservations")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be("native original");
                (retained.AssistantResponse?.Body).Should().Be(changeScope ? null : "genuine exact answer");
                if (!changeScope) { retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded); }
                composition.Http.Requests.Count(item => item.Method is "POST").Should().Be(changeScope ? 2 : 4);
                composition.Turns.IsQuiescent.Should().BeTrue(); composition.Runtime.IsQuiescent.Should().BeTrue();
            }
            finally { window.ClosePrivate(); }
        });
    }
}
