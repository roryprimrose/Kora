using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Mcp;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(McpCapabilityKind.Resource)]
    [InlineData(McpCapabilityKind.ResourceTemplate)]
    [InlineData(McpCapabilityKind.Prompt)]
    public async Task McpTextReadsCompleteWithoutAModelAndKeepExactOutgoingArguments(McpCapabilityKind kind)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var host = f.Host;
        var service = CapabilityService(f, host, source, broker, approvals);
        var result = await service.ReadMcpContentAsync(new(f.Session, null), source.ServerId, source.Account, kind,
            approvals.Present, SelectCapability, ContentArguments, static () => true, Token);
        result.Outcome.Should().Be(McpExplicitOutcome.Succeeded);
        result.Content!.Document.Display.Should().Contain("observed reference");
        result.Content.Capability.Kind.Should().Be(kind);
        result.Content.ToString().Should().Be(nameof(McpContentResult));
        result.Result.Should().BeNull();
        broker.Disposed.Should().BeTrue();
        broker.Methods.Should().NotContain("tools/call");
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection, McpReviewStage.Invocation);
        if (kind == McpCapabilityKind.ResourceTemplate)
        { broker.LastParams.Should().Be("""{"uri":"data://item/reference"}"""); }
        if (kind == McpCapabilityKind.Prompt)
        { broker.LastParams.Should().Be("""{"name":"review","arguments":{"value":"reference"}}"""); }
    }

    [Theory]
    [InlineData(McpCapabilityKind.Tool)]
    [InlineData(McpCapabilityKind.Resource)]
    [InlineData(McpCapabilityKind.Prompt)]
    public async Task McpDisabledCategoriesNeverConnect(McpCapabilityKind kind)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        source.Settings = WithSelection(source.Settings,
            source.Settings.McpCapabilities.SetCategory(source.ServerId, McpCapabilitySelection.Category(kind), false));
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var result = kind == McpCapabilityKind.Tool
            ? await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account, approvals.Present,
                McpSelect, (_, _, _, _, _, _) => throw new InvalidOperationException("No input expected"), static () => true, Token)
            : await service.ReadMcpContentAsync(new(f.Session, null), source.ServerId, source.Account, kind,
                approvals.Present, SelectCapability, ContentArguments, static () => true, Token);
        result.Outcome.Should().Be(McpExplicitOutcome.Denied);
        broker.Methods.Should().BeEmpty();
        approvals.Stages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task McpExplicitlyEnabledTemplateUsesItsOwnCategoryChoiceButHonorsResolvedResourceExclusions(
        bool categoryEnabled, bool resourceExcluded)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var selection = source.Settings.McpCapabilities
            .SetCategory(source.ServerId, McpCapabilityCategory.Resources, categoryEnabled)
            .SetItem(source.ServerId, McpCapabilityKind.ResourceTemplate, "data://item/{value}", true);
        if (resourceExcluded)
        { selection = selection.SetItem(source.ServerId, McpCapabilityKind.Resource, "data://item/reference", false); }
        source.Settings = WithSelection(source.Settings, selection);
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var result = await CapabilityService(f, f.Host, source, broker, approvals).ReadMcpContentAsync(
            new(f.Session, null), source.ServerId, source.Account, McpCapabilityKind.ResourceTemplate,
            approvals.Present, SelectCapability, ContentArguments, static () => true, Token);
        result.Outcome.Should().Be(resourceExcluded ? McpExplicitOutcome.Denied : McpExplicitOutcome.Succeeded);
        if (resourceExcluded)
        {
            result.Content.Should().BeNull();
            result.DurableState.Should().Be(HostTaskState.Unknown);
            broker.Methods.Should().NotContain("resources/read");
            approvals.Stages.Should().NotContain(McpReviewStage.Invocation);
        }
        else
        {
            result.Content!.Document.Display.Should().Contain("observed reference");
            result.Content.Capability.Kind.Should().Be(McpCapabilityKind.ResourceTemplate);
            broker.Methods.Should().Contain("resources/read");
            broker.LastParams.Should().Be("""{"uri":"data://item/reference"}""");
        }
        broker.Disposed.Should().BeTrue();
        broker.Methods.Should().NotContain("tools/call");
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("resolved-template")]
    [InlineData("embedded-resource")]
    [InlineData("embedded-category")]
    [InlineData("cancelled-selection")]
    [InlineData("unknown-selection")]
    [InlineData("binary-content")]
    public async Task McpContentExclusionsAndUnsupportedContentNeverPublishPartialResults(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var kind = fault is "embedded-resource" or "embedded-category" or "binary-content" ? McpCapabilityKind.Prompt
            : fault is "resolved-template" ? McpCapabilityKind.ResourceTemplate : McpCapabilityKind.Resource;
        if (fault is "item" or "embedded-resource" or "resolved-template")
        {
            var uri = fault is "resolved-template" ? "data://item/reference" : "data://item";
            source.Settings = WithSelection(source.Settings,
                source.Settings.McpCapabilities.SetItem(source.ServerId, McpCapabilityKind.Resource, uri, false));
        }
        if (string.Equals(fault, "embedded-category", StringComparison.Ordinal))
        {
            source.Settings = WithSelection(source.Settings,
                source.Settings.McpCapabilities.SetCategory(source.ServerId, McpCapabilityCategory.Resources, false));
        }
        var broker = new CapabilityBroker(source.Account) { BinaryPrompt = fault is "binary-content" };
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var result = await service.ReadMcpContentAsync(new(f.Session, null), source.ServerId, source.Account, kind,
            approvals.Present, fault switch
            {
                "cancelled-selection" => (_, _, _, _, _, _) => Task.FromResult<McpCapability?>(null),
                "unknown-selection" => (_, _, _, _, _, _) => Task.FromResult<McpCapability?>(
                    new(McpCapabilityKind.Resource, "data://other", "Other", "{}")),
                _ => SelectCapability,
            }, ContentArguments, static () => true, Token);
        result.Outcome.Should().NotBe(McpExplicitOutcome.Succeeded);
        result.Content.Should().BeNull();
        broker.Disposed.Should().BeTrue();
        if (fault is "item" or "resolved-template" or "cancelled-selection" or "unknown-selection")
        { broker.Methods.Should().NotContain("resources/read"); }
        if (fault is "cancelled-selection") { result.Outcome.Should().Be(McpExplicitOutcome.Cancelled); }
    }

    [Theory]
    [InlineData(McpCapabilityKind.Resource)]
    [InlineData(McpCapabilityKind.ResourceTemplate)]
    [InlineData(McpCapabilityKind.Prompt)]
    public async Task McpTextModelUseRequiresSeparateOneUseConsentAndRetainsOnlyGenuineAnswers(McpCapabilityKind kind)
    {
        const string volatileSource = "observed reference: volatile-mcp-body-not-a-saved-context";
        using var f = new Fixture();
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var transport = new ContentModelTransport();
        await using var loop = new BoundedModelTurnAdapter(transport, f.Time, f);
        var host = f.ConfiguredProfiles(adapter: loop);
        var approvals = new McpApprovals(f);
        var broker = new CapabilityBroker(source.Account)
        {
            ReplyResult = (method, parameters) => method switch
            {
                "resources/read" => JsonSerializer.Serialize(new
                {
                    contents = new[] { new { uri = parameters.GetProperty("uri").GetString(), text = volatileSource } },
                }),
                "prompts/get" => JsonSerializer.Serialize(new
                {
                    messages = new object[]
                    {
                        new { role = "user", content = new { type = "text", text = "Use this observed reference." } },
                        new { role = "assistant", content = new
                        {
                            type = "resource", resource = new { uri = "data://item", text = volatileSource },
                        } },
                    },
                }),
                _ => null,
            },
        };
        var service = CapabilityService(f, host, source, broker, approvals);
        var observations = 0;
        var receipt = await service.SubmitMcpContentAsync(new(f.Session, null), "Explain the reviewed reference.",
            source.ServerId, source.Account, kind, approvals.Present, SelectCapability, ContentArguments,
            static () => true, Token, onResult: (_, _) => observations++);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed reference is available.");
        transport.Requests.Should().ContainSingle();
        transport.Requests[0].Context.ToolMode.Should().Be(ModelToolMode.None);
        transport.Requests[0].Context.Tools.Should().BeEmpty();
        transport.Requests[0].Context.McpTool.Should().BeNull();
        transport.Requests[0].Context.UserRequest.Should().Be("Explain the reviewed reference.");
        transport.Requests[0].Context.Evidence.Single().Content.Should().Contain(volatileSource);
        if (kind == McpCapabilityKind.Prompt)
        {
            using var evidence = JsonDocument.Parse(transport.Requests[0].Context.Evidence.Single().Content);
            evidence.RootElement.GetProperty("Blocks").EnumerateArray().Select(block => block.GetProperty("Role").GetString())
                .Should().Equal("user", "assistant");
        }
        observations.Should().Be(1);
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection,
            McpReviewStage.Invocation, McpReviewStage.ResultContinuation);
        broker.Disposed.Should().BeTrue();
        receipt.Message.Observations.Should().BeNull();
        receipt.Message.SubmittedContext.Should().BeNull();
        var readback = await f.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
            receipt.Message.Request.RequestId, Token);
        readback.Should().Be(receipt.Message);
        readback.Body.Should().Be("Explain the reviewed reference.");
        readback.AssistantResponse!.Body.Should().Be("The observed reference is available.");
        readback.Observations.Should().BeNull();
        readback.SubmittedContext.Should().BeNull();
        JsonSerializer.Serialize(readback).Should().NotContain(volatileSource);
        f.Messages.Should().NotContain(message => message.Contains("observed reference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task McpDeclinedContentConsentDoesNotSendReadTextToAModel()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var transport = new ContentModelTransport();
        await using var loop = new BoundedModelTurnAdapter(transport, f.Time, f);
        var approvals = new McpApprovals(f) { DeniedStage = McpReviewStage.ResultContinuation };
        var broker = new CapabilityBroker(source.Account);
        var service = CapabilityService(f, f.ConfiguredProfiles(adapter: loop), source, broker, approvals);
        var receipt = await service.SubmitMcpContentAsync(new(f.Session, null), "Explain the reviewed reference.",
            source.ServerId, source.Account, McpCapabilityKind.Resource, approvals.Present,
            SelectCapability, ContentArguments, static () => true, Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        transport.Requests.Should().BeEmpty();
        broker.Methods.Should().Contain("resources/read");
        broker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task McpDiscoveryIsPaginatedAndSavedChoicesRequireTheExactIssuedCurrentSnapshot()
    {
        using var f = new Fixture();
        using var activity = f.Root();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account) { PaginatedResources = true };
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var inventory = await service.DiscoverMcpCapabilitiesAsync(new(f.Session, null), source.ServerId,
            source.Account, approvals.Present, static () => true, Token);
        inventory.Items.Should().HaveCount(5);
        inventory.ServerName.Should().Be("reference");
        inventory.ToString().Should().Be(nameof(McpCapabilityInventory));
        inventory.Items.Single(item => item.Kind == McpCapabilityKind.Tool).UnavailableReason.Should().NotBeNull();
        inventory.Capabilities.Should().Be(new McpServerCapabilities(true, true, true));
        broker.Methods.Count(method => string.Equals(method, "resources/list", StringComparison.Ordinal)).Should().Be(2);
        broker.Methods.Should().NotContain("resources/read").And.NotContain("prompts/get").And.NotContain("tools/call");
        broker.Disposed.Should().BeTrue();
        var selection = inventory.Selection.SetCategory(source.ServerId, McpCapabilityCategory.Resources, false)
            .SetItem(source.ServerId, McpCapabilityKind.Prompt, "review", false);
        await service.SaveMcpCapabilitiesAsync(inventory, selection, false, static () => true, Token);
        source.Configuration.Load().Single().IsIncluded.Should().BeFalse();
        source.Settings.McpCapabilities.IsEnabled(source.ServerId, McpCapabilityKind.Resource, "data://new").Should().BeFalse();
        source.Settings.McpCapabilities.IsEnabled(source.ServerId, McpCapabilityKind.Prompt, "review").Should().BeFalse();
        await FluentActions.Awaiting(() => service.SaveMcpCapabilitiesAsync(inventory, selection, true, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task McpCapabilityDisableReenableDuringReadRetiresTheOldRequest()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        source.Settings = WithSelection(source.Settings,
            source.Settings.McpCapabilities.SetCategory(source.ServerId, McpCapabilityCategory.Resources, true));
        var broker = new CapabilityBroker(source.Account)
        {
            AfterExchange = method =>
            {
                if (!string.Equals(method, "resources/read", StringComparison.Ordinal)) { return; }
                source.Settings = WithSelection(source.Settings,
                    source.Settings.McpCapabilities.SetItem(source.ServerId, McpCapabilityKind.Resource, "data://item", false)
                        .SetItem(source.ServerId, McpCapabilityKind.Resource, "data://item", true));
            },
        };
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var result = await service.ReadMcpContentAsync(new(f.Session, null), source.ServerId, source.Account,
            McpCapabilityKind.Resource, approvals.Present, SelectCapability, ContentArguments, static () => true, Token);
        source.Settings.McpCapabilities.IsEnabled(source.ServerId, McpCapabilityKind.Resource, "data://item").Should().BeTrue();
        result.Outcome.Should().NotBe(McpExplicitOutcome.Succeeded);
        result.Content.Should().BeNull();
        broker.Disposed.Should().BeTrue();
    }

    private static ArtifactDirectorySettings WithSelection(ArtifactDirectorySettings current, McpCapabilitySelection selection) =>
        new(current.SkillDirectories, current.InstructionDirectories, current.PromptDirectories, current.AgentDirectories,
            current.McpConfigurationFiles, current.IncludedMcpServerIds, current.UseDefaultDirectories, selection);

    private static Task<McpCapability?> SelectCapability(HostRequest request, HostRevision generation, DateTimeOffset expires,
        IReadOnlyList<McpCapability> items, Func<bool> admitted, CancellationToken token) =>
        Task.FromResult<McpCapability?>(items.Single());

    private static Task<string?> ContentArguments(HostRequest request, HostRevision generation, DateTimeOffset expires,
        McpCapability capability, Func<bool> admitted, CancellationToken token) =>
        Task.FromResult<string?>("""{"value":"reference"}""");

    private static SessionConversationService CapabilityService(Fixture f, ModelTurnHost host, McpSource source,
        IMcpConnectionBroker broker, McpApprovals approvals, bool settingsAvailable = true)
    {
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        return new(f, f, workspace, f, host, f.Time, NullLogger<SessionConversationService>.Instance, host,
            f.CancelAudit, approvals, mcpConfiguration: source.Configuration, mcpBroker: broker, mcpEffects: new([]),
            explicitTaskCoordinator: new(f),
            mcpSettings: settingsAvailable ? new(source.Preferences, f.CancelAudit) : null);
    }

    private sealed class ContentModelTransport : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        internal Action? BeforeReply { get; init; }
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            BeforeReply?.Invoke();
            var answer = ModelToolProtocol.ParseAssistant("""{"kind":"answer","answer":"The observed reference is available."}"""u8);
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, answer,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(answer).Length)));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapabilityBroker(McpAccountReference account) : IMcpConnectionBroker, IMcpConnection
    {
        internal bool BinaryPrompt { get; init; }
        internal bool PaginatedResources { get; init; }
        internal Action<string>? AfterExchange { get; init; }
        internal Action? AfterDispose { get; init; }
        internal Func<string, JsonElement, string?>? ReplyResult { get; init; }
        internal string? ErrorMethod { get; init; }
        internal bool Disposed { get; private set; }
        internal List<string> Methods { get; } = [];
        internal string? LastParams { get; private set; }
        public McpAccountReference Account => account;
        public McpEndpoint Endpoint => account.Destination;
        public IReadOnlyList<IPAddress> RemoteAddresses => [IPAddress.Parse("8.8.8.8")];
        public Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<McpAccountChoice>>([new(account, "Test delegated user")]);
        public Task<IMcpConnection> ConnectAsync(McpAccountReference selected, CancellationToken token) =>
            Task.FromResult<IMcpConnection>(this);
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(true);
        public Task NotifyAsync(ImmutableArray<byte> notification, CancellationToken token) => Task.CompletedTask;
        public Task<ImmutableArray<byte>> ExchangeAsync(ImmutableArray<byte> wire, int maximumResponseBytes, CancellationToken token)
        {
            using var request = JsonDocument.Parse(wire.AsMemory());
            var root = request.RootElement;
            var method = root.GetProperty("method").GetString()!;
            var parameters = root.GetProperty("params");
            Methods.Add(method);
            LastParams = parameters.GetRawText();
            if (string.Equals(method, ErrorMethod, StringComparison.Ordinal))
            {
                return Task.FromResult(ImmutableArray.CreateRange(Encoding.UTF8.GetBytes(
                    "{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText()
                    + ",\"error\":{\"code\":-32603,\"message\":\"synthetic server failure\"}}")));
            }
            var result = ReplyResult?.Invoke(method, parameters) ?? (method switch
            {
                "initialize" => """{"protocolVersion":"2025-06-18","capabilities":{"tools":{},"resources":{},"prompts":{}},"serverInfo":{"name":"fake","version":"1"}}""",
                "tools/list" => "{\"tools\":[{\"name\":\"search\",\"inputSchema\":" + McpSchema + "}]}",
                "resources/list" => PaginatedResources && parameters.TryGetProperty("cursor", out _)
                    ? """{"resources":[{"name":"Other","uri":"data://other"}]}"""
                    : """{"resources":[{"name":"Reference","uri":"data://item"}]"""
                        + (PaginatedResources ? ""","nextCursor":"opaque"}""" : "}"),
                "resources/templates/list" => """{"resourceTemplates":[{"name":"Reference template","uriTemplate":"data://item/{value}"}]}""",
                "prompts/list" => """{"prompts":[{"name":"review","arguments":[{"name":"value","required":true}]}]}""",
                "resources/read" => JsonSerializer.Serialize(new
                {
                    contents = new[] { new { uri = parameters.GetProperty("uri").GetString(), text = "observed reference" } },
                }),
                "prompts/get" => BinaryPrompt
                    ? """{"messages":[{"role":"user","content":{"type":"image","data":"AA==","mimeType":"image/png"}}]}"""
                    : """{"messages":[{"role":"user","content":{"type":"text","text":"Use this observed reference."}},{"role":"assistant","content":{"type":"resource","resource":{"uri":"data://item","text":"observed reference"}}}]}""",
                _ => throw new InvalidDataException("Unexpected fake MCP method."),
            });
            AfterExchange?.Invoke(method);
            return Task.FromResult(ImmutableArray.CreateRange(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":"
                + root.GetProperty("id").GetRawText() + ",\"result\":" + result + "}")));
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            AfterDispose?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}