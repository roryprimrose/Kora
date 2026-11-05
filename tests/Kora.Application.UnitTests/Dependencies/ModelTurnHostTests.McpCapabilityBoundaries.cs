using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("pages", 32, "*page bound*")]
    [InlineData("bytes", 18, "*byte bound*")]
    [InlineData("duplicate", 2, "*duplicate identities*")]
    [InlineData("items", 2, "*item bound*")]
    [InlineData("cursor", 2, "*repeated*cursor*")]
    [InlineData("total-items", 2, "*complete*item bound*")]
    public async Task McpIncompleteInventoriesNeverBecomeCompletedObservations(string fault, int expectedPages, string message)
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var pages = 0;
        var broker = new CapabilityBroker(source.Account)
        {
            ReplyResult = (method, _) =>
            {
                if (string.Equals(method, "initialize", StringComparison.Ordinal)) { return CapabilityInitialization(string.Equals(fault, "total-items", StringComparison.Ordinal) ? """{"tools":{},"resources":{}}""" : """{"resources":{}}"""); }
                if (string.Equals(method, "tools/list", StringComparison.Ordinal))
                {
                    pages++;
                    return JsonSerializer.Serialize(new
                    {
                        tools = Enumerable.Range(0, McpCapabilityProtocol.MaximumItems)
                            .Select(index => new { name = "tool" + index, inputSchema = new { type = "object" } }),
                    });
                }
                if (!string.Equals(method, "resources/list", StringComparison.Ordinal)) { return null; }
                pages++;
                var count = string.Equals(fault, "items", StringComparison.Ordinal) && pages == 1 ? McpCapabilityProtocol.MaximumItems : 1;
                var resources = Enumerable.Range(0, count).Select(index => new
                {
                    name = "Reference",
                    uri = "data://item/" + (string.Equals(fault, "duplicate", StringComparison.Ordinal) ? "same" : pages + "-" + index),
                    description = string.Equals(fault, "bytes", StringComparison.Ordinal) ? new string('x', 2048) : "bounded",
                }).ToArray();
                if (string.Equals(fault, "items", StringComparison.Ordinal))
                {
                    var compact = resources.Select(item => new { name = "R", item.uri });
                    return pages == 1
                        ? JsonSerializer.Serialize(new { resources = compact, nextCursor = "second" })
                        : JsonSerializer.Serialize(new { resources = compact });
                }
                return string.Equals(fault, "bytes", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new
                    {
                        resources = Enumerable.Range(0, 7).Select(index => resources[0] with { uri = resources[0].uri + "-" + index }),
                        nextCursor = pages.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    })
                    : string.Equals(fault, "total-items", StringComparison.Ordinal)
                        ? JsonSerializer.Serialize(new { resources })
                        : JsonSerializer.Serialize(new
                        {
                            resources,
                            nextCursor = string.Equals(fault, "cursor", StringComparison.Ordinal) ? "repeated" : pages.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        });
            },
        };
        var approvals = new McpApprovals(f);
        await using var invocation = CapabilityUse(f, source, broker, approvals);
        await FluentActions.Awaiting(() => invocation.DiscoverAllAsync(Token))
            .Should().ThrowAsync<InvalidDataException>().WithMessage(message);
        pages.Should().Be(expectedPages);
        invocation.Completed.Should().BeFalse();
        invocation.Content.Should().BeNull();
        invocation.Result.Should().BeNull();
        await invocation.DisposeAsync();
        broker.Disposed.Should().BeTrue();
        broker.Methods.Should().NotContain("resources/read").And.NotContain("prompts/get").And.NotContain("tools/call");
        approvals.Stages.Should().Equal(McpReviewStage.Connection);
        f.Task!.Task.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [Fact]
    public async Task McpDiscoverySkipsUnadvertisedPrimitivesWithoutInventingAuthority()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account)
        {
            ReplyResult = (method, _) => string.Equals(method, "initialize", StringComparison.Ordinal) ? CapabilityInitialization("""{"prompts":{}}""") : null,
        };
        var approvals = new McpApprovals(f);
        var inventory = await CapabilityService(f, f.Host, source, broker, approvals).DiscoverMcpCapabilitiesAsync(
            new(f.Session, null), source.ServerId, source.Account, approvals.Present, static () => true, Token);
        inventory.Capabilities.Should().Be(new McpServerCapabilities(false, false, true));
        inventory.Items.Should().ContainSingle(item => item.Kind == McpCapabilityKind.Prompt);
        broker.Methods.Should().Equal("initialize", "prompts/list");
        broker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("initialize", McpCapabilityKind.Resource)]
    [InlineData("resources/list", McpCapabilityKind.Resource)]
    [InlineData("resources/read", McpCapabilityKind.Resource)]
    [InlineData("prompts/get", McpCapabilityKind.Prompt)]
    public async Task McpProtocolErrorsAreFailedNotSuccessfulOrPartiallyPublished(string method, McpCapabilityKind kind)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account) { ErrorMethod = method };
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var result = await service.ReadMcpContentAsync(new(f.Session, null), source.ServerId, source.Account, kind,
            approvals.Present, SelectCapability, ContentArguments, static () => true, Token);
        result.Outcome.Should().Be(McpExplicitOutcome.Failed);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Content.Should().BeNull();
        result.Result.Should().BeNull();
        broker.Methods.Last().Should().Be(method);
        broker.Methods.Should().NotContain("tools/call");
        broker.Disposed.Should().BeTrue();
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task McpUnpreparedStateAndRepeatedPreparationFailBeforeAnyAdditionalExchange()
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        await using var invocation = CapabilityUse(f, source, broker, approvals);
        FluentActions.Invoking(() => _ = invocation.Source).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => _ = invocation.DeclaredCapabilities).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => invocation.ConsentContentAsync("Explain.", "Conversation", Token))
            .Should().ThrowAsync<InvalidOperationException>();
        broker.Methods.Should().BeEmpty();
        await invocation.DiscoverAllAsync(Token);
        var methods = broker.Methods.ToArray();
        await FluentActions.Awaiting(() => invocation.DiscoverAllAsync(Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*single-use*");
        broker.Methods.Should().Equal(methods);
        broker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(McpCapabilityKind.Tool)]
    [InlineData((McpCapabilityKind)99)]
    public async Task McpContentRejectsNonContentKindsBeforeConnection(McpCapabilityKind kind)
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        await using var invocation = CapabilityUse(f, source, broker, approvals);
        await FluentActions.Awaiting(() => invocation.ReadContentAsync(kind, SelectCapability, ContentArguments, Token))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        invocation.Completed.Should().BeFalse();
        invocation.Content.Should().BeNull();
        broker.Methods.Should().BeEmpty();
        approvals.Stages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(McpCapabilityKind.ResourceTemplate)]
    [InlineData(McpCapabilityKind.Prompt)]
    public async Task McpDismissedContentArgumentsCancelWithoutSendingARead(McpCapabilityKind kind)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var result = await service.ReadMcpContentAsync(new(f.Session, null), source.ServerId, source.Account, kind,
            approvals.Present, SelectCapability, (_, _, _, _, _, _) => Task.FromResult<string?>(null), static () => true, Token);
        result.Outcome.Should().Be(McpExplicitOutcome.Cancelled);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Content.Should().BeNull();
        broker.Methods.Should().NotContain("resources/read").And.NotContain("prompts/get").And.NotContain("tools/call");
        broker.Disposed.Should().BeTrue();
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection);
        f.Retained.Should().BeNull();
    }

    [Fact]
    public async Task McpInventoryOnlyInvocationCannotSelectOrExecuteATool()
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        await using var invocation = CapabilityUse(f, source, broker, approvals, await source.Policy());
        await FluentActions.Awaiting(() => invocation.PrepareAsync(Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*no tool-selection authority*");
        invocation.Completed.Should().BeFalse();
        invocation.Result.Should().BeNull();
        broker.Methods.Should().Equal("initialize", "tools/list");
        approvals.Stages.Should().Equal(McpReviewStage.Connection);
        await invocation.DisposeAsync();
        broker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task McpContentBindingRequiresOriginalHostToolFreeLocalPreparedSingleUseTurn()
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        await using var invocation = CapabilityUse(f, source, broker, approvals);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            f.Context(toolMode: ModelToolMode.None), Token)).Turn!;
        FluentActions.Invoking(() => f.PolicyHost.BindMcpContent(turn, invocation)).Should().Throw<InvalidOperationException>();
        var toolTurn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        FluentActions.Invoking(() => f.Host.BindMcpContent(toolTurn, invocation)).Should().Throw<InvalidOperationException>();
        var hosted = new ModelTurn(turn.Owner, turn.Issuer, turn.Provenance with { Selection = ModelProviderSelection.CopilotCandidate },
            turn.Context, turn.Wire, turn.SessionGeneration, turn.TaskRevision, turn.ControlRevision, turn.Registration);
        FluentActions.Invoking(() => f.Host.BindMcpContent(hosted, invocation)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => f.Host.BindMcpContent(turn, invocation)).Should().Throw<InvalidOperationException>();
        broker.Methods.Should().BeEmpty();
        await invocation.ReadContentAsync(McpCapabilityKind.Resource, SelectCapability, ContentArguments, Token);
        invocation.Completed.Should().BeTrue();
        invocation.Content.Should().NotBeNull();
        f.Host.BindMcpContent(turn, invocation);
        FluentActions.Invoking(() => f.Host.BindMcpContent(turn, invocation)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => invocation.BindTurn(turn)).Should().Throw<InvalidOperationException>();
        broker.Methods.Should().Equal("initialize", "resources/list", "resources/read");
        broker.Disposed.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("risk")]
    [InlineData("unavailable")]
    [InlineData("denied")]
    public async Task McpObservedTextCannotAcquireModelConsentThroughRiskOrUnavailableInput(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f)
        {
            DeniedStage = string.Equals(fault, "denied", StringComparison.Ordinal) ? McpReviewStage.ResultContinuation : null,
        };
        await using var invocation = CapabilityUse(f, source, broker, approvals);
        await invocation.ReadContentAsync(McpCapabilityKind.Resource, SelectCapability, ContentArguments, Token);
        await FluentActions.Awaiting(() => invocation.ConsentContentAsync(fault switch
        {
            "risk" => "password=synthetic-fixture",
            "unavailable" => "{",
            _ => "Explain the reference.",
        }, "Conversation", Token)).Should().ThrowAsync<InvalidOperationException>();
        invocation.Completed.Should().BeTrue("the read completed, not a model turn");
        broker.Disposed.Should().BeTrue();
        if (string.Equals(fault, "denied", StringComparison.Ordinal))
        {
            approvals.LastReview!.ContentContinuation!.ToString().Should().Be(nameof(McpContentContinuationReview));
            approvals.Stages.Should().Contain(McpReviewStage.ResultContinuation);
        }
        else { approvals.Stages.Should().NotContain(McpReviewStage.ResultContinuation); }
        f.Adapter.Calls.Should().Be(0);
        f.Retained.Should().BeNull();
    }

    private static string CapabilityInitialization(string capabilities) =>
        """{"protocolVersion":"2025-06-18","capabilities":""" + capabilities
        + ""","serverInfo":{"name":"fake","version":"1"}}""";

    private static McpInvocation CapabilityUse(Fixture f, McpSource source, CapabilityBroker broker,
        McpApprovals approvals, McpReadOnlyPolicy? policy = null) =>
        new(source.Configuration, broker, policy ?? new([]), approvals, f, f, f.CancelAudit,
            f.Time, f.Request, f.Session.Generation, source.ServerId, source.Account, approvals.Present, null, static () => true);
}