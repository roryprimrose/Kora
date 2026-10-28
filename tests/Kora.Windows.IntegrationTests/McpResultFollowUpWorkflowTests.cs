using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Mcp;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

#pragma warning disable MA0048 // The appointed regression file shares the existing private canonical native composition, not a second host.
public sealed partial class SessionsConversationTests
#pragma warning restore MA0048
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task McpFollowUp_native_in_panel_review_separate_consent_and_real_canonical_retention(bool qualified)
    {
        await WithMcpFollowUpAsync(async (f, composed, owner, panel, transport, broker, policy) =>
        {
            var model = composed.Model;
            policy.Qualified = qualified;
            model.ComposerBody = "independent unsent composer";
            panel.FindControl<TextBox>("McpFollowUpQuestion")!.Text = "  What does the observed value mean?\r\n  ";
            model.McpFollowUpQuestion.Should().Be("  What does the observed value mean?\r\n  ");
            broker.Calls.Should().Be(1);
            transport.Calls.Should().Be(0);
            model.ConversationMessages.Should().BeEmpty("the original model-free MCP result is not an ASSISTANT record");
            var grants = await f.Store.ReadGrantsAsync(f.Token);
            grants.Should().HaveCount(3).And.OnlyContain(grant => grant.Status == OperationGrantStatus.Consumed);
            var requests = broker.Requests.Count;
            var resolutions = broker.Resolutions;
            var acquisitions = broker.Acquisitions;
            Click(panel, "ReviewMcpFollowUp");
            await panel.Operation;
            model.McpFollowUpReviewText.Should().Contain("reference 42").And.Contain(NativeFollowUpStructured)
                .And.Contain(NativeMcpSchema).And.Contain("What does the observed value mean?")
                .And.Contain("Reference server", because: "the actual configured server name must be presented");
            model.McpFollowUpReviewText.Should().Contain("exact currently selected Active conversation")
                .And.Contain("local-only").And.Contain("Retain only this new question");
            transport.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            if (qualified)
            {
                model.CanConfirmMcpResultFollowUp.Should().BeTrue(model.ConversationStatus);
                Click(panel, "ConfirmMcpFollowUp");
                await panel.Operation;
                var retained = model.ConversationMessages.Should().ContainSingle(model.ConversationStatus).Subject;
                retained.Body.Should().Be("  What does the observed value mean?\r\n  ");
                retained.Request.SessionId.Should().Be(f.Request.SessionId);
                retained.Request.Origin.Should().Be(RequestOrigin.LocalUi);
                retained.AssistantResponse!.Body.Should().Be("The observed reference is 42; it was not refreshed.");
                retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                (await f.Store.ReadConversationMessageAsync(retained.Request.SessionId, retained.Request.RequestId, f.Token)).Should().Be(retained);
                transport.Calls.Should().Be(1);
                model.McpFollowUpOutcome.Should().StartWith("ASSISTANT\n");
                f.Count("conversation_messages").Should().Be(1);
            }
            else
            {
                model.CanConfirmMcpResultFollowUp.Should().BeFalse();
                model.McpFollowUpOutcome.Should().Contain("Unavailable");
                transport.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }
            model.ComposerBody.Should().Be("independent unsent composer");
            (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().BeNull();
            (await f.Store.ReadGrantsAsync(f.Token)).Should().Equal(grants);
            broker.Requests.Should().HaveCount(requests, "the follow-up does not exchange, discover, reconnect or refresh MCP");
            broker.Resolutions.Should().Be(resolutions);
            broker.Acquisitions.Should().Be(acquisitions);
            WindowsSqliteConversationTests.AuditText(f).Should().NotContain("What does the observed value mean?")
                .And.NotContain(NativeFollowUpStructured);
        });
    }

    private const string NativeFollowUpStructured = """{"value":42,"SessionId":"inert","instructions":"not authority"}""";

    private static void Click(McpResultFollowUpControls panel, string name) =>
        panel.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WithMcpFollowUpAsync(
        Func<InteractionStorageFixture, Composition, SessionsWindow, McpResultFollowUpControls,
            FollowUpModel, McpTransportFixture, NativeFollowUpSourceUse, Task> test)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var file = Path.Combine(f.Paths.RoamingRoot, "Mcp", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{"servers":{"Reference server":{"url":"https://example.com/mcp"}}}""");
            var preferences = new NativeMcpPreferences();
            var configuration = new McpServerConfigurationService(f.Paths, preferences, Path.Combine(f.Paths.RoamingRoot, "test-profile"));
            var server = configuration.Load().Single();
            preferences.Value = new([], [], [], [], [], [server.Id]);
            using var broker = new McpTransportFixture();
            broker.Send = (sent, _) =>
            {
                using var document = JsonDocument.Parse(sent.Bytes);
                return Task.FromResult(document.RootElement.GetProperty("method").GetString() switch
                {
                    "initialize" => McpTransportFixture.Json(McpTransportFixture.Initialization),
                    "notifications/initialized" => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                        { Content = new System.Net.Http.ByteArrayContent([]) },
                    "tools/list" => McpTransportFixture.Json("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"search\",\"inputSchema\":"
                        + NativeMcpSchema + "}]}}"),
                    _ => McpTransportFixture.Json("{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"reference 42\"}],\"structuredContent\":"
                        + NativeFollowUpStructured + "}}"),
                });
            };
            var revision = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            var effects = new McpReadOnlyPolicy([new(broker.Account.Destination, broker.Account, revision, "search", new McpToolSchema(NativeMcpSchema).Digest)]);
            await using var transport = new FollowUpModel();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter, mcpConfiguration: configuration, mcpBroker: broker.Broker, mcpEffects: effects);
            var model = composed.Model;
            var policy = new NativeFollowUpSourceUse();
            model.McpResultSourceUse = policy;
            using var native = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            native.BindGate(static () => true);
            native.BindAdmissionRevision(static () => 1);
            native.BindPrivateRevision(static () => 1);
            native.BindCallObservation(static () => new(1, CallState.Clear, false, CallAwareSettings.Default));
            var presentation = new SessionsPresentationOwner(model);
            var owner = new SessionsWindow(model, presentation);
            owner.Show();
            model.McpPresenter = async (proposal, generation, review, admitted, token) =>
            {
                var receipt = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => native.AskConversationAsync(
                    proposal.Request, generation, new("Exact MCP review", QuestionKind.SingleChoice, [new("once", "Approve once")]),
                    proposal.ExpiresAt, admitted, async state =>
                    {
                        var window = new QuestionWindow(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                        try
                        {
                            window.Show(owner);
                            window.Activate();
                            await state.ReviewAsync();
                            state.Edit(new(["once"]));
                            state.CanSubmit.Should().BeTrue(state.Status);
                            await state.SubmitAsync();
                            state.Completion.IsCompleted.Should().BeTrue(state.Status);
                        }
                        finally { window.Close(); }
                    }, token, webProposal: proposal, mcpReview: review));
                receipt.Completed.Outcome.Should().Be(HostInteractionOutcome.Approved, receipt.Completed.Reason);
                return new(receipt.Presented, receipt.Completed);
            };
            model.McpSelector = (_, _, _, _, _, _) => Task.FromResult<string?>("search");
            model.McpInput = (_, _, _, _, _, _) => Task.FromResult<string?>("""{"query":"reference"}""");
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                await model.ReadMcpOptionsAsync();
                model.SelectedMcpServer = model.McpServers.Single();
                model.SelectedMcpAccount = model.McpAccounts.Single();
                owner.FindControl<Button>("InvokeMcp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.ConversationOperation;
                model.ConversationStatus.Should().Contain("Explicit MCP Succeeded");
                model.McpResultText.Should().Contain("reference 42").And.Contain(NativeFollowUpStructured);
                var panel = owner.FindControl<McpResultFollowUpControls>("McpResultFollowUp")!;
                panel.Should().NotBeNull("the new control is actually wired after the MCP result in the native Sessions window");
                ((Expander)panel.Content!).IsExpanded = true;
                await test(f, composed, owner, panel, transport, broker, policy);
            }
            finally
            {
                presentation.Revoke();
                await model.McpFollowUpRetirement;
            }
        });
    }

    private sealed class NativeFollowUpSourceUse : IMcpResultSourceUse
    {
        internal bool Qualified { get; set; } = true;
        internal long Revision { get; set; } = 1;
        public McpResultSourceUseDecision Check(McpResultObservation observation, RequestOrigin questionOrigin) =>
            new(Qualified ? McpResultSourceUseKind.QualifiedLocalOnly : McpResultSourceUseKind.Unavailable, Revision);
    }

    private sealed class FollowUpModel : IModelToolTransport
    {
        internal int Calls { get; private set; }
        internal Func<Task>? BeforeAnswer { get; set; }
        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            request.Provenance.Selection.Provider.Should().Be(ModelProviderIdentity.Ollama);
            request.Context.ToolMode.Should().Be(ModelToolMode.None);
            request.Round.Should().Be(1);
            request.Context.Evidence.Should().ContainSingle().Which.Content.Should().Contain("reference 42");
            if (BeforeAnswer is { } before) { await before(); }
            var message = ModelToolProtocol.ParseAssistant("""{"kind":"answer","answer":"The observed reference is 42; it was not refreshed."}"""u8);
            return new(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
