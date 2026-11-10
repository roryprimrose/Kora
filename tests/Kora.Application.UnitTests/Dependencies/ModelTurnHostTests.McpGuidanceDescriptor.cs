using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Dependencies;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("withdraw")]
    [InlineData("source")]
    [InlineData("notify")]
    [InlineData("capability")]
    [InlineData("dispose")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("host")]
    [InlineData("failed-source")]
    [InlineData("failed-control")]
    [InlineData("source-files")]
    [InlineData("failed-source-files")]
    public async Task OnlyPreparedTrustedRuntimeProducesNonrenewingGuidanceAdmission(string change)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        using var source = new McpSource(audit: f.CancelAudit);
        using var withdrawal = new CancellationTokenSource();
        var broker = new McpBroker(source.Account) { Withdrawal = withdrawal.Token };
        using var policy = await source.Policy();
        var reference = new DeclarativeMcpToolReference(source.ServerId,
            (await source.Configuration.ResolveHttpsAsync(source.ServerId, source.Account, Token)).SourceRevision,
            "search", new Kora.Core.Mcp.McpToolSchema(McpSchema).Digest);
        McpReadOnlyPolicy.DescriptorAdmission? captured = null;
        var entered = false;
        var admitted = true;
        var model = new McpModelTransport
        {
            BeforeFirstReply = async () =>
            {
                entered = true;
                captured = policy.CaptureGuidanceDescriptor(reference);
                captured.IsCurrent().Should().BeTrue();
                var calls = broker.Methods.ToArray();
                source.BeforeLoad = () => throw new InvalidOperationException("Authoring must not read MCP configuration.");
                if (change is "save")
                {
                    await using var guidance = new SkillRefinementFixture(source: true, mcpReference: reference, mcpPolicy: policy);
                    guidance.Model.Answer("NEW literal guidance; mcp.other is inert prose");
                    var proposal = (await guidance.Propose())!;
                    proposal.Proposed.ManifestBytes.ToArray().Should().Equal(guidance.Draft.ManifestBytes.ToArray());
                    var saved = (await guidance.Service.Save(proposal, guidance.Authority, new SkillRefinementReview(),
                        guidance.Eligible, Token))!;
                    saved.Document.McpReference.Should().Be(reference);
                    saved.Document.IsAvailableForInvocation.Should().BeFalse();
                    guidance.Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
                }
                else
                {
                    source.BeforeLoad = null;
                    switch (change)
                    {
                        case "withdraw": withdrawal.Cancel(); break;
                        case "source": source.Configuration.Load(); break;
                        case "notify": source.Configuration.NotifyChanged(); break;
                        case "capability": source.Capabilities!.SetToolEnabled(source.ServerId, "search", false); break;
                        case "dispose": source.Configuration.Dispose(); break;
                        case "expiry": f.Time.Now += TimeSpan.FromMinutes(3); break;
                        case "clock": f.Time.Now -= TimeSpan.FromMinutes(3); break;
                        case "host": admitted = false; break;
                        case "source-files": _ = source.Configuration.SourceFiles; break;
                        case "failed-source-files":
                            source.BeforeLoad = () => throw new IOException("Synthetic source-list observation failure.");
                            FluentActions.Invoking(() => source.Configuration.SourceFiles).Should().Throw<IOException>();
                            source.BeforeLoad = null;
                            break;
                        case "failed-source":
                            source.BeforeLoad = () => throw new IOException("Synthetic source observation failure.");
                            FluentActions.Invoking(() => source.Configuration.Load()).Should().Throw<IOException>();
                            source.BeforeLoad = null;
                            break;
                        case "failed-control":
                            source.BeforeLoad = () => throw new IOException("Synthetic capability control read failure.");
                            FluentActions.Invoking(() => source.Capabilities!.SetToolEnabled(source.ServerId, "search", false))
                                .Should().Throw<IOException>();
                            source.BeforeLoad = null;
                            break;
                    }
                    captured.IsCurrent().Should().BeFalse();
                    FluentActions.Invoking(() => policy.CaptureGuidanceDescriptor(reference)).Should().Throw<InvalidOperationException>();
                }
                broker.Methods.Should().Equal(calls, "descriptor checks and guidance authoring cannot execute MCP");
                source.BeforeLoad = null;
            }
        };
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var host = f.ConfiguredProfiles(adapter: loop);
        var approvals = new McpApprovals(f);
        var service = McpService(f, host, source, broker, approvals, policy);
        var operation = service.SubmitMcpAsync(new(f.Session, null), "Original runtime request", source.ServerId,
            source.Account, approvals.Present, McpSelect, () => admitted, Token);
        if (change is "dispose" or "capability" or "expiry" or "host" or "failed-control" or "failed-source")
        {
            var failure = await FluentActions.Awaiting(async () => await operation)
                .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
            if (change is "dispose") { failure.Which.InnerException.Should().BeOfType<ObjectDisposedException>(); }
            else { failure.Which.InnerException.Should().BeOfType<InvalidOperationException>(); }
        }
        else { _ = await operation; }
        entered.Should().BeTrue();
        captured!.IsCurrent().Should().BeFalse("prepared runtime proof never survives its physical owner");
        broker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task RuntimeDescriptorRetiresBeforeUsedInvocationAndPhysicalTransportJoin()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        using var source = new McpSource(audit: f.CancelAudit);
        using var withdrawal = new CancellationTokenSource();
        var joining = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var broker = new McpBroker(source.Account)
        { Withdrawal = withdrawal.Token, PendingDispose = joining, EnteredDispose = entered };
        using var policy = await source.Policy();
        var reference = new DeclarativeMcpToolReference(source.ServerId,
            (await source.Configuration.ResolveHttpsAsync(source.ServerId, source.Account, Token)).SourceRevision,
            "search", new Kora.Core.Mcp.McpToolSchema(McpSchema).Digest);
        McpReadOnlyPolicy.DescriptorAdmission? captured = null;
        var model = new McpModelTransport
        {
            BeforeFirstReply = () =>
            {
                captured = policy.CaptureGuidanceDescriptor(reference);
                broker.AfterExchange = method =>
                {
                    if (method is "tools/call")
                    {
                        captured.IsCurrent().Should().BeFalse("use retires authoring authority before dispatch");
                        FluentActions.Invoking(() => policy.CaptureGuidanceDescriptor(reference))
                            .Should().Throw<InvalidOperationException>();
                    }
                };
                return Task.CompletedTask;
            }
        };
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.ConfiguredProfiles(adapter: loop), source, broker, approvals, policy);
        var operation = service.SubmitMcpAsync(new(f.Session, null), "Original runtime request", source.ServerId,
            source.Account, approvals.Present, McpSelect, static () => true, Token);
        await entered.Task;
        try
        {
            captured!.IsCurrent().Should().BeFalse();
            operation.IsCompleted.Should().BeFalse("retirement does not mean physical disposal completed");
            broker.Disposed.Should().BeFalse();
        }
        finally { joining.TrySetResult(); }
        _ = await operation;
        broker.Disposed.Should().BeTrue();
        broker.Methods.Count(method => method is "tools/call").Should().Be(1);
    }

    [Theory]
    [InlineData(McpReviewStage.Connection)]
    [InlineData(McpReviewStage.ToolSelection)]
    public async Task DeniedRuntimePreparationCannotProduceGuidanceDescriptor(McpReviewStage stage)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        using var source = new McpSource(audit: f.CancelAudit);
        using var withdrawal = new CancellationTokenSource();
        var broker = new McpBroker(source.Account) { Withdrawal = withdrawal.Token };
        using var policy = await source.Policy();
        var reference = new DeclarativeMcpToolReference(source.ServerId,
            (await source.Configuration.ResolveHttpsAsync(source.ServerId, source.Account, Token)).SourceRevision,
            "search", new Kora.Core.Mcp.McpToolSchema(McpSchema).Digest);
        var approvals = new McpApprovals(f) { DeniedStage = stage };
        var model = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var service = McpService(f, f.ConfiguredProfiles(adapter: loop), source, broker, approvals, policy);
        var result = await service.SubmitMcpAsync(new(f.Session, null), "Original runtime request", source.ServerId,
            source.Account, approvals.Present, McpSelect, static () => true, Token);
        result.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Message.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        approvals.Stages.Last().Should().Be(stage);
        FluentActions.Invoking(() => policy.CaptureGuidanceDescriptor(reference)).Should().Throw<InvalidOperationException>();
        broker.Methods.Should().NotContain("tools/call");
        broker.Disposed.Should().Be(stage is McpReviewStage.ToolSelection);
        model.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("configuration")]
    [InlineData("use")]
    [InlineData("capability")]
    [InlineData("source")]
    public void DescriptorObservationRefusesUnknownOrFailedControlOwners(string failure)
    {
        using var f = new Fixture();
        using var source = new McpSource(audit: failure is "missing" ? null : f.CancelAudit);
        if (failure is "source") { source.Configuration.NotifyChanged(); }
        if (failure is "configuration")
        {
            source.BeforeLoad = () => throw new IOException("Synthetic preference read failure.");
            FluentActions.Invoking(() => source.DescriptorDirectories!.Load()).Should().Throw<IOException>();
            source.BeforeLoad = null;
        }
        if (failure is "use")
        {
            source.DescriptorUseStore.FailWrite = true;
            FluentActions.Invoking(() => source.DescriptorUse!.SetDisabled(Kora.Core.Artifacts.ArtifactUseIdentity.Mcp(source.ServerId), true))
                .Should().Throw<IOException>();
        }
        if (failure is "capability")
        {
            source.BeforeLoad = () => throw new IOException("Synthetic capability read failure.");
            FluentActions.Invoking(() => source.Capabilities!.SetToolEnabled(source.ServerId, "search", false))
                .Should().Throw<IOException>();
            source.BeforeLoad = null;
        }
        source.BeforeLoad = () => throw new InvalidOperationException("Descriptor capture cannot read preferences.");
        source.Configuration.CaptureDescriptorObservation().Should().BeNull();
    }
}
