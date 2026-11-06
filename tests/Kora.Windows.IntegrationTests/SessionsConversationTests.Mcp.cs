using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Interaction;
using Kora.Core.Mcp;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private const string NativeMcpSchema = """{"type":"object","additionalProperties":false,"properties":{"query":{"type":"string","maxLength":32}},"required":["query"]}""";

    [Fact]
    public void Native_mcp_model_wire_exposes_only_the_exact_tool_schema_and_rejects_other_names()
    {
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            ModelProviderSelection.OllamaCandidate);
        var tool = new McpModelTool("mcp.search", NativeMcpSchema);
        var envelope = new ModelContextEnvelope(provenance.Request, "host-owned local policy", "find reference", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.McpReadOnly, mcpTool: tool);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        var request = ModelToolProtocol.CreateRequest(provenance, null, context, [], 1);
        using var wire = System.Text.Json.JsonDocument.Parse(Kora.Windows.Dependencies.OllamaToolWire.WriteRequest(request, "fake-model", []));
        var function = wire.RootElement.GetProperty("tools").EnumerateArray().Should().ContainSingle().Subject.GetProperty("function");
        function.GetProperty("name").GetString().Should().Be("mcp.search");
        function.GetProperty("parameters").GetRawText().Should().Be(NativeMcpSchema);
        const string reply = """{"model":"fake-model","done":true,"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"mcp.search","arguments":{"query":"reference"}}}]}}""";
        Kora.Windows.Dependencies.OllamaToolWire.ReadReply(Encoding.UTF8.GetBytes(reply), "fake-model", 1, mcpTool: tool)
            .Message.Should().BeOfType<ModelAssistantToolCalls>();
        FluentActions.Invoking(() => Kora.Windows.Dependencies.OllamaToolWire.ReadReply(Encoding.UTF8.GetBytes(
            reply.Replace("mcp.search", "mcp.other", StringComparison.Ordinal)), "fake-model", 1, mcpTool: tool))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, false, false, false, "application/json; charset=utf-8")]
    [InlineData(false, true, false, false, false, "APPLICATION/JSON; CHARSET=UtF-8")]
    [InlineData(false, false, false, false, false, "Application/Json; Charset=\"UtF-8\"")]
    [InlineData(false, true, false, false, false, "Application/Json; Charset=\"UtF-8\"")]
    public async Task Native_mcp_reviews_connects_selects_invokes_and_retains_actual_result_without_live_account_or_network(
        bool deny, bool explicitUser, bool cancelIgnored, bool denyResult, bool cancelSelection,
        string contentType = "application/json; charset=\"utf-8\"")
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var file = Path.Combine(f.Paths.RoamingRoot, "Mcp", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{"servers":{"reference":{"url":"https://example.com/mcp"}}}""");
            var preferences = new NativeMcpPreferences();
            var configuration = new McpServerConfigurationService(f.Paths, preferences,
                Path.Combine(f.Paths.RoamingRoot, "test-profile"));
            var server = configuration.Load().Single();
            preferences.Value = new([], [], [], [], [], [server.Id]);
            using var broker = new McpTransportFixture { ResponseContentType = contentType };
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancelIgnored)
            {
                broker.Send = async (sent, _) =>
                {
                    using var document = System.Text.Json.JsonDocument.Parse(sent.Bytes);
                    return await (document.RootElement.GetProperty("method").GetString() switch
                    {
                        "initialize" => Task.FromResult(McpTransportFixture.Json(McpTransportFixture.Initialization)),
                        "notifications/initialized" => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                            { Content = new System.Net.Http.ByteArrayContent([]) }),
                        "tools/list" => Task.FromResult(McpTransportFixture.Json(
                            "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"search\",\"inputSchema\":" + NativeMcpSchema + "}]}}")),
                        _ => Hold(),
                    });
                };
                Task<System.Net.Http.HttpResponseMessage> Hold() { entered.SetResult(); return pending.Task; }
            }
            var sourceRevision = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            var policy = new McpReadOnlyPolicy([new(broker.Account.Destination, broker.Account,
                sourceRevision, "search", new McpToolSchema(NativeMcpSchema).Digest)]);
            await using var transport = new NativeMcpModel();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter, mcpConfiguration: configuration,
                mcpBroker: broker.Broker, mcpEffects: policy);
            using var native = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            native.BindGate(static () => true);
            native.BindAdmissionRevision(static () => 1);
            native.BindPrivateRevision(static () => 1);
            native.BindCallObservation(static () => new(1, CallState.Clear, false, CallAwareSettings.Default));
            var model = composed.Model;
            var presentation = new SessionsPresentationOwner(model);
            var owner = new SessionsWindow(model, presentation);
            owner.Show();
            var stages = new List<McpReviewStage>();
            var selectorFault = string.Empty;
            model.McpPresenter = async (proposal, generation, review, admitted, token) =>
            {
                var receipt = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    native.AskConversationAsync(proposal.Request, generation,
                        new("Exact MCP review", QuestionKind.SingleChoice, [new("once", "Approve once")]),
                        proposal.ExpiresAt, admitted, async state =>
                        {
                            stages.Add(review.Stage);
                            state.IsApproval.Should().BeTrue();
                            var window = new QuestionWindow(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                            try
                            {
                                window.Show(owner);
                                window.Activate();
                                await state.ReviewAsync();
                                state.ReviewText.Should().Contain("https://example.com/mcp").And.Contain("Native delegated test account");
                                if (explicitUser)
                                {
                                    state.ReviewText.Should().Contain("Configured server: reference").And.Contain(file);
                                    review.Source!.Path.Should().Be(file);
                                }
                                if (review.Stage == McpReviewStage.Invocation)
                                { state.ReviewText.Should().Contain(NativeMcpSchema).And.Contain("""{"query":"reference"}"""); }
                                if (review.Stage == McpReviewStage.ResultContinuation)
                                {
                                    transport.Calls.Should().Be(1);
                                    broker.Disposed.Should().BeTrue();
                                    review.Continuation.Should().NotBeNull();
                                    state.ReviewText.Should().Contain(review.Continuation!.Result.Text)
                                        .And.Contain("SEPARATE ONE-USE").And.Contain("Find the reviewed reference.");
                                    state.Question!.Spec.Options.Should().ContainSingle().Which.Id.Should().Be("once");
                                }
                                if (deny || denyResult && review.Stage == McpReviewStage.ResultContinuation)
                                { await state.CancelAsync(); }
                                else
                                {
                                    state.Edit(new(["once"]));
                                    state.CanSubmit.Should().BeTrue(state.Status);
                                    await state.SubmitAsync();
                                }
                            }
                            finally { window.Close(); }
                        }, token, webProposal: proposal, mcpReview: review));
                if (!deny && !(denyResult && review.Stage == McpReviewStage.ResultContinuation))
                {
                    receipt.Completed.Outcome.Should().Be(HostInteractionOutcome.Approved, receipt.Completed.Reason);
                    receipt.Completed.Question!.Draft!.Choices.Should().Equal("once");
                }
                return new(receipt.Presented, receipt.Completed);
            };
            model.McpSelector = async (request, generation, expiresAt, tools, admitted, token) =>
            {
                tools.Should().ContainSingle().Which.Name.Should().Be("search");
                Kora.Application.Hosting.ConversationQuestionPresentation receipt;
                try
                {
                    receipt = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        native.AskConversationAsync(request, generation,
                        new("Select exact mapped tool", QuestionKind.SingleChoice, [new("tool-0", "search")], sourceId: "mcp-tool-selection"),
                        expiresAt, admitted, async state =>
                        {
                            var window = new QuestionWindow(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                            try
                            {
                                window.Show(owner);
                                if (cancelSelection)
                                {
                                    if (explicitUser) { window.Close(); }
                                    else { await state.CancelAsync(); }
                                    return;
                                }
                                state.Edit(new(["tool-0"]));
                                state.CanSubmit.Should().BeTrue(state.Status);
                                await state.SubmitAsync();
                                state.Completion.IsCompleted.Should().BeTrue(state.Status);
                            }
                            finally { window.Close(); }
                        }, token, mcpSelection: true));
                }
                catch (InvalidOperationException exception) { selectorFault = exception.Message; throw; }
                receipt.Completed.Outcome.Should().Be(cancelSelection
                    ? explicitUser ? HostInteractionOutcome.Denied : HostInteractionOutcome.Cancelled
                    : HostInteractionOutcome.Answered);
                return cancelSelection ? null : "search";
            };
            model.McpInput = async (request, generation, expiresAt, tool, admitted, token) =>
            {
                cancelSelection.Should().BeFalse("dismissed tool selection must not request arguments");
                tool.Id.Should().Be("mcp.search");
                tool.InputSchema.Should().Be(NativeMcpSchema);
                var prompt = "Enter JSON arguments for " + tool.Id
                    + ". Every required field must match this schema. Submitting input is NOT approval to send it.\n"
                    + tool.InputSchema;
                var receipt = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    native.AskConversationAsync(request, generation,
                        new(prompt, QuestionKind.Text, [],
                            maximumTextLength: McpToolSchema.MaximumArgumentsUtf8Bytes, sourceId: "mcp-tool-arguments"),
                        expiresAt, admitted, async state =>
                        {
                            var window = new QuestionWindow(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                            try
                            {
                                window.Show(owner);
                                window.FindControl<TextBox>("ExactReview")!.Text.Should().Be(prompt);
                                state.ReviewText.Should().BeEmpty("schema presentation is not an explicit review or approval");
                                window.FindControl<TextBox>("AnswerInput")!.Text = """{"query":"reference"}""";
                                state.CanSubmit.Should().BeTrue(state.Status);
                                await state.SubmitAsync();
                                state.Completion.IsCompleted.Should().BeTrue(state.Status);
                            }
                            finally { window.Close(); }
                        }, token, mcpInput: true));
                receipt.Completed.Outcome.Should().Be(HostInteractionOutcome.Answered);
                broker.Calls.Should().Be(0, "input alone is not invocation approval");
                return receipt.Completed.Question!.Draft!.Text;
            };
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                await model.ReadMcpOptionsAsync();
                model.SelectedMcpServer = model.McpServers.Single();
                model.SelectedMcpAccount = model.McpAccounts.Single();
                model.ComposerBody = "Find the reviewed reference.";
                owner.FindControl<Button>(explicitUser ? "InvokeMcp" : "SubmitMcp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (cancelIgnored)
                {
                    await entered.Task.WaitAsync(f.Token);
                    owner.FindControl<Button>("CancelMcp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    owner.ConversationCancellationOperation.IsCompleted.Should().BeFalse();
                    owner.ConversationOperation.IsCompleted.Should().BeFalse();
                    model.IsMcpActive.Should().BeTrue();
                    model.McpResultText.Should().BeEmpty();
                    broker.Disposed.Should().BeFalse();
                    pending.SetResult(McpTransportFixture.Json(McpTransportFixture.Result));
                    await owner.ConversationCancellationOperation;
                }
                await owner.ConversationOperation;
                if (explicitUser)
                {
                    model.ConversationMessages.Should().BeEmpty("explicit tools do not fabricate model turns or assistant answers");
                    model.ComposerBody.Should().Be("Find the reviewed reference.");
                    model.ConversationStatus.Should().Contain(deny ? "Explicit MCP Denied"
                        : cancelIgnored || cancelSelection ? "Explicit MCP Cancelled" : "Explicit MCP Succeeded");
                    if (cancelIgnored) { model.ConversationStatus.Should().Contain("Unknown").And.Contain("Remote processing may have occurred"); }
                    transport.Calls.Should().Be(0);
                }
                else
                {
                    var retained = model.ConversationMessages.Should().ContainSingle(model.ConversationStatus).Subject;
                    retained.Outcome.Should().Be(deny || denyResult ? ModelTurnOutcome.Denied
                        : cancelSelection ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Succeeded,
                    model.ConversationStatus + " | reviews " + string.Join(", ", stages)
                    + " | connected " + broker.Connected + " | exchanges " + string.Join(", ", broker.Methods)
                    + " | calls " + broker.Calls + " | selector " + selectorFault);
                    (retained.AssistantResponse?.Body).Should().Be(deny || denyResult || cancelSelection ? null : "Actual reviewed MCP reference: 42.");
                    (await f.Store.ReadConversationMessageAsync(retained.Request.SessionId, retained.Request.RequestId, f.Token))
                        .Should().Be(retained);
                }
                broker.Calls.Should().Be(deny || cancelSelection ? 0 : 1);
                transport.Calls.Should().Be(explicitUser || deny || cancelSelection ? 0 : denyResult ? 1 : 2);
                if (!deny && !cancelSelection)
                {
                    using var call = System.Text.Json.JsonDocument.Parse(broker.Requests[^1].Bytes);
                    call.RootElement.GetProperty("method").GetString().Should().Be("tools/call");
                    var parameters = call.RootElement.GetProperty("params");
                    parameters.GetProperty("name").GetString().Should().Be("search");
                    parameters.GetProperty("arguments").GetRawText().Should().Be("""{"query":"reference"}""");
                }
                if (deny || denyResult || cancelIgnored || cancelSelection) { model.McpResultText.Should().BeEmpty(); }
                else { model.McpResultText.Should().Contain("reference 42").And.Contain("mcp.search").And.Contain("Observed UTC:"); }
                broker.Disposed.Should().Be(!deny);
                stages.Should().Equal(deny || cancelSelection ? [McpReviewStage.Connection]
                    : explicitUser ? [McpReviewStage.Connection, McpReviewStage.ToolSelection, McpReviewStage.Invocation]
                    : [McpReviewStage.Connection, McpReviewStage.ToolSelection, McpReviewStage.Invocation, McpReviewStage.ResultContinuation]);
                if (cancelSelection) { broker.Methods.Should().Equal("initialize", "notifications/initialized", "tools/list"); }
                var grants = await f.Store.ReadGrantsAsync(f.Token);
                if (deny) { grants.Should().BeEmpty(); }
                else
                {
                    grants.Should().HaveCount(cancelSelection ? 1 : explicitUser || denyResult ? 3 : 4)
                        .And.OnlyContain(grant => grant.Status == Kora.Core.Authorization.OperationGrantStatus.Consumed);
                }
                model.IsMcpActive.Should().BeFalse();
            }
            finally { presentation.Revoke(); }
        });
    }

    private sealed class NativeMcpPreferences : IArtifactDirectoryPreferences
    {
        internal ArtifactDirectorySettings Value { get; set; } = ArtifactDirectorySettings.Empty;
        public ArtifactDirectorySettings Load() => Value;
        public void Save(ArtifactDirectorySettings settings) => Value = settings;
    }

    private sealed class NativeMcpModel : IModelToolTransport
    {
        internal int Calls { get; private set; }
        internal List<ModelToolRequest> Requests { get; } = [];
        internal Func<Task>? BeforeFreshAnswer { get; set; }
        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request);
            request.Provenance.Selection.Provider.Should().Be(ModelProviderIdentity.Ollama);
            if (request.Context.ToolMode == ModelToolMode.None && BeforeFreshAnswer is { } before) { await before(); }
            var message = ModelToolProtocol.ParseAssistant(request.Context.ToolMode == ModelToolMode.None
                ? """{"kind":"answer","answer":"The unchanged observed reference is 42; no source was refreshed."}"""u8
                : request.Round == 1
                ? """{"kind":"toolCalls","calls":[{"callId":"reference_1","capabilityId":"mcp.search","input":{"query":"reference"}}]}"""u8
                : """{"kind":"answer","answer":"Actual reviewed MCP reference: 42."}"""u8, mcpTool: request.Context.McpTool);
            return new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
