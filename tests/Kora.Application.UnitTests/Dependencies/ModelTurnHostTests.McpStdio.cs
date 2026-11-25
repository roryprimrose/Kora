using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Mcp;
using System.Net;
using McpProcessAuthorization = Kora.Core.Mcp.McpConnectionIdentity.McpProcessAuthorization;
using IMcpProcessPreparation = Kora.Core.Mcp.IMcpConnectionBroker.IMcpProcessPreparation;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(null, true, false)]
    [InlineData("launch", true, false)]
    [InlineData("cleanup", true, false)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    public async Task Original_user_stdio_operation_uses_same_task_and_host_mapping_without_a_delegated_account(
        string? deniedProcessAction, bool mapped, bool differentPreparation)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        File.WriteAllText(source.File,
            """{"servers":{"reference":{"command":"C:\\Installed\\server.exe","args":["--stdio"],"cwd":"C:\\Workspace","env":{}}}}""");
        source.Configuration.Load();
        var descriptor = await source.Configuration.ResolveAsync(source.ServerId, null, Token);
        var wire = new McpBroker(source.Account);
        var broker = new StdioBroker(descriptor.Identity, wire)
        {
            PreparationIdentity = differentPreparation ? source.Account : descriptor.Identity,
        };
        var approvals = new McpApprovals(f) { DeniedProcessAction = deniedProcessAction };
        var policy = mapped
            ? new McpReadOnlyPolicy([new(descriptor.Identity, descriptor.SourceRevision, "search", new McpToolSchema(McpSchema).Digest)])
            : new McpReadOnlyPolicy([]);
        var service = McpService(f, f.Host, source, broker, approvals, policy);
        (await service.ResolveMcpIdentityAsync(source.ServerId, null, Token)).Should().Be(descriptor.Identity);
        await FluentActions.Awaiting(() => service.ResolveMcpIdentityAsync(source.ServerId, source.Account, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, descriptor.Identity,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token);
        if (deniedProcessAction is null && mapped && !differentPreparation)
        {
            result.Outcome.Should().Be(McpExplicitOutcome.Succeeded);
            result.Result.Should().NotBeNull();
            wire.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        }
        else
        {
            result.Outcome.Should().NotBe(McpExplicitOutcome.Succeeded);
            result.Result.Should().BeNull();
            wire.Methods.Should().NotContain("tools/call");
        }
        if (deniedProcessAction is not null || differentPreparation) { broker.Launched.Should().BeFalse(); wire.Methods.Should().BeEmpty(); }
        else { broker.Launched.Should().BeTrue(); wire.Disposed.Should().BeTrue(); }
        broker.PreparationReleased.Should().BeTrue();
        approvals.ProcessActions.Should().Equal(differentPreparation ? [] : deniedProcessAction is "launch" ? ["launch"] : ["launch", "cleanup"]);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Transport_metadata_cannot_be_reinterpreted_as_another_destination_or_delegated_account()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var https = await source.Configuration.ResolveAsync(source.ServerId, source.Account, Token);
        var other = new McpEndpoint(new("https://other.example.com/mcp"));
        FluentActions.Invoking(() => new McpReadOnlyMapping(other, source.Account, https.SourceRevision, "search", new McpToolSchema(McpSchema).Digest))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new McpConnectionDescriptor(https.ServerId, https.ServerName, https.SourcePath,
            https.SourceRevision, other, source.Account, https.UseControlRevision)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new McpReview(McpReviewStage.Connection, other.Address, source.Account, "user", null, null, null))
            .Should().Throw<InvalidDataException>();
        var review = new McpReview(McpReviewStage.Connection, source.Account.Destination.Address, source.Account, "user", null, null, null);
        review.Endpoint.Should().Be(source.Account.Destination.Address);
        var local = new McpConnectionIdentity(new McpStdioEndpoint(@"C:\Installed\server.exe", [], @"C:\Workspace",
            new Dictionary<string, string>(StringComparer.Ordinal)));
        var localSource = new McpConnectionDescriptor(https.ServerId, https.ServerName, https.SourcePath,
            https.SourceRevision, local, https.UseControlRevision);
        FluentActions.Invoking(() => localSource.Account).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => localSource.Endpoint).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => (review with { Identity = local }).Account).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => (review with { Identity = local }).Endpoint).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => source.Configuration.ResolveAsync(source.ServerId, null, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => source.Configuration.ResolveAsync(source.ServerId, local, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => source.Configuration.ResolveAsync(source.ServerId,
            new McpConnectionIdentity(new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), other)), Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await using var unavailable = f.ConversationService(f.Host);
        await FluentActions.Awaiting(() => unavailable.ResolveMcpIdentityAsync(source.ServerId, source.Account, Token))
            .Should().ThrowAsync<Kora.Application.Mcp.McpUnavailableException>();
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("cleanup")]
    public async Task Process_consent_requires_fresh_native_once_even_with_an_existing_reusable_grant(string action)
    {
        using var f = new Fixture();
        f.Request = new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, f.Request.Origin, new(Guid.NewGuid()));
        using var root = f.Root();
        var (review, proposal) = ProcessReview(f, action);
        var reusable = new OperationGrant(new(Guid.NewGuid()), new(1), proposal,
            OperationGrantScope.Perpetual, f.Session.Generation, RequestOrigin.LocalUi, f.Time.GetUtcNow());
        f.Questions.Change(snapshot => snapshot with { Proposal = proposal, Grants = [reusable] });
        var service = new HostAuthorizationService(f.Questions, f.Time);

        var presented = await service.PresentMcpOnceAsync(f.Request, review, Token);
        presented.Question.Should().NotBeNull();
        presented.Grant.Should().BeNull();
        f.Questions.Snapshot.Grants.Should().Equal(reusable);
        var question = presented.Question!;
        foreach (var choice in new[] { "session", "perpetual" })
        {
            (await service.ApproveAsync(question.Key, new([choice]), RequestOrigin.LocalUi, Token))
                .Outcome.Should().NotBe(HostInteractionOutcome.Approved);
        }
        f.Questions.Change(snapshot => snapshot with { Policy = snapshot.Policy with { IsProtectedCall = false } });
        (await service.ApproveAsync(question.Key, new(["once"]), RequestOrigin.ActivatedVoice, Token))
            .Outcome.Should().NotBe(HostInteractionOutcome.Approved);
        f.Questions.Snapshot.Grants.Should().Equal(reusable);
        f.Questions.Change(snapshot => snapshot with
        {
            Questions = snapshot.Questions.SetItem(0, question with
            {
                Spec = new QuestionSpec("Unbound process approval", QuestionKind.SingleChoice,
                    [new("once", "Once")], purpose: "authorization", sourceId: "other-process"),
            }),
        });
        (await service.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi, Token))
            .Outcome.Should().NotBe(HostInteractionOutcome.Approved);
        f.Questions.Change(snapshot => snapshot with { Questions = snapshot.Questions.SetItem(0, question) });
        f.Questions.Change(snapshot => snapshot with
        {
            Questions = snapshot.Questions.SetItem(0, question with
            {
                Spec = new QuestionSpec("Reusable process approval", QuestionKind.SingleChoice,
                    [new("session", "This session")], purpose: "authorization", sourceId: "mcp-process-" + action),
            }),
        });
        (await service.ApproveAsync(question.Key, new(["session"]), RequestOrigin.LocalUi, Token))
            .Outcome.Should().NotBe(HostInteractionOutcome.Approved);
        f.Questions.Change(snapshot => snapshot with
        {
            Questions = snapshot.Questions.SetItem(0, question with
            {
                Spec = new QuestionSpec("Combined process approval", QuestionKind.MultipleChoice,
                    [new("once", "Once"), new("session", "This session")], maximum: 2,
                    purpose: "authorization", sourceId: "mcp-process-" + action),
            }),
        });
        (await service.ApproveAsync(question.Key, new(["once", "session"]), RequestOrigin.LocalUi, Token))
            .Outcome.Should().NotBe(HostInteractionOutcome.Approved);
        f.Questions.Change(snapshot => snapshot with { Questions = snapshot.Questions.SetItem(0, question) });
        var approved = await service.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi, Token);
        approved.Outcome.Should().Be(HostInteractionOutcome.Approved);
        approved.Grant!.Scope.Should().Be(OperationGrantScope.Once);
        approved.Grant.CreatorChannel.Should().Be(RequestOrigin.LocalUi);
        var spoken = new OperationGrant(new(Guid.NewGuid()), new(1), proposal,
            OperationGrantScope.Once, f.Session.Generation, RequestOrigin.ActivatedVoice, f.Time.GetUtcNow());
        f.Questions.Change(snapshot => snapshot with { Grants = snapshot.Grants.Add(spoken) });
        (await service.ConsumeAsync(f.Request, spoken.Id, spoken.Revision, Token))
            .Outcome.Should().NotBe(HostInteractionOutcome.Consumed);
        var consumed = await service.ConsumeAsync(f.Request, approved.Grant.Id, approved.Grant.Revision, Token);
        consumed.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        consumed.Grant!.Status.Should().Be(OperationGrantStatus.Consumed);
        (await service.ConsumeAsync(f.Request, reusable.Id, reusable.Revision, Token))
            .Outcome.Should().NotBe(HostInteractionOutcome.Consumed);
    }

    [Theory]
    [InlineData("action")]
    [InlineData("binary")]
    [InlineData("missing-binary")]
    public async Task Process_review_refuses_mismatched_host_action_or_held_binary_without_creating_a_question(string change)
    {
        using var f = new Fixture();
        f.Request = new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, f.Request.Origin, new(Guid.NewGuid()));
        using var root = f.Root();
        var (review, proposal) = ProcessReview(f, "launch");
        f.Questions.Change(snapshot => snapshot with { Proposal = proposal });
        review = change switch
        {
            "action" => review with { ProcessAction = "cleanup" },
            "binary" => review with { NativeProcessIdentity = "different-held-binary" },
            _ => review with { NativeProcessIdentity = null },
        };
        var result = await new HostAuthorizationService(f.Questions, f.Time)
            .PresentMcpOnceAsync(f.Request, review, Token);
        result.Outcome.Should().NotBe(HostInteractionOutcome.Approved);
        result.Question.Should().BeNull();
        f.Questions.Snapshot.Questions.Should().BeEmpty();
        f.Questions.Snapshot.Grants.Should().BeEmpty();
    }

    private static (McpReview Review, HostOperationProposal Proposal) ProcessReview(Fixture f, string action)
    {
        static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var identity = new McpConnectionIdentity(new McpStdioEndpoint(@"C:\Installed\server.exe", ["--stdio"],
            @"C:\Workspace", new Dictionary<string, string>(StringComparer.Ordinal)));
        var review = new McpReview(McpReviewStage.Connection, identity, "Credential-free installed process", null, null, null)
        { ProcessAction = action, NativeProcessIdentity = "held-native-binary-and-directory" };
        var binding = new ExactOperationBinding("mcp.process." + action, "mcp", "stdio." + action,
            Hash("connection"), Hash("source"), Hash("source"), Hash("implementation"), review.InvocationDigest,
            Hash("connection"), identity.Digest, identity.Digest, Hash("transform"), new(1));
        return (review, new(f.Request, new(Guid.NewGuid()), new(1), binding,
            HostOperationEffect.BoundedWrite, f.Time.GetUtcNow().AddMinutes(1)));
    }

    private sealed class StdioBroker(McpConnectionIdentity identity, McpBroker wire) : IMcpConnectionBroker,
        IMcpProcessPreparation, IMcpConnection
    {
        internal bool Launched { get; private set; }
        internal bool PreparationReleased { get; private set; }
        internal McpConnectionIdentity? PreparationIdentity { get; init; }
        McpConnectionIdentity IMcpProcessPreparation.Identity => PreparationIdentity ?? identity;
        public McpConnectionIdentity Identity => identity;
        public string NativeIdentity => "held-native-binary";
        public McpAccountReference Account => throw new InvalidOperationException("Stdio must not access delegated accounts.");
        public McpEndpoint Endpoint => throw new InvalidOperationException("Stdio must not access HTTPS destinations.");
        public IReadOnlyList<IPAddress> RemoteAddresses => [];
        public CancellationToken AdmissionWithdrawn => CancellationToken.None;
        public Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token) =>
            throw new InvalidOperationException("Stdio must not read account credentials.");
        public Task<IMcpConnection> ConnectAsync(McpAccountReference account, CancellationToken token) =>
            throw new InvalidOperationException("Stdio must not launch through the HTTPS route.");
        public Task<IMcpProcessPreparation> PrepareStdioAsync(McpConnectionIdentity selected, CancellationToken token)
        {
            selected.Should().Be(identity);
            return Task.FromResult<IMcpProcessPreparation>(this);
        }
        public async Task<IMcpConnection> ConnectAsync(McpProcessAuthorization authorization, CancellationToken token)
        {
            authorization.Identity.Should().Be(identity);
            authorization.NativeIdentity.Should().Be(NativeIdentity);
            await authorization.ValidateLaunchAsync(token);
            await authorization.ValidateCleanupAsync(token);
            Launched = true;
            return this;
        }
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(Launched && !wire.Disposed);
        public Task<McpConnectionReply<McpServerCapabilities>> InitializeAsync(CancellationToken token) => wire.InitializeAsync(token);
        public Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token) =>
            wire.ListAsync(kind, cursor, token);
        public Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token) =>
            wire.InvokeAsync(name, schema, arguments, token);
        public Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token) =>
            wire.ReadResourceAsync(uri, token);
        public Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token) =>
            wire.ReadPromptAsync(prompt, arguments, token);
        public async ValueTask DisposeAsync()
        {
            if (PreparationReleased) { return; }
            PreparationReleased = true;
            if (Launched) { await wire.DisposeAsync(); }
        }
    }
}
