using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Dependencies;
using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("origin")]
    [InlineData("channel")]
    [InlineData("inactive")]
    [InlineData("empty")]
    [InlineData("large")]
    [InlineData("duplicate")]
    [InlineData("kind")]
    [InlineData("readonly")]
    [InlineData("unavailable")]
    [InlineData("observation")]
    [InlineData("fields")]
    [InlineData("error")]
    [InlineData("use-drift")]
    [InlineData("reload-drift")]
    [InlineData("serialized-limit")]
    [InlineData("late-source")]
    [InlineData("private-after-read")]
    public async Task DiscoveryReviewRejectsInvalidOrDriftingSourcesWithoutTruncationOrLastGoodFallback(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var sources = new MetadataSources();
        var use = new MetadataUse();
        if (fault is "serialized-limit")
        {
            sources.Values = Enumerable.Range(1, 6).Select(index => sources.Values[0] with
            {
                Source = sources.Values[0].Source with { Id = "raw.source." + index, Name = "choice" + index },
                ArtifactId = "artifact." + index, Name = new string('"', 128), Description = new string('"', 256),
            }).ToArray();
        }
        using var catalogue = sources.Catalogue(use);
        var selected = fault is "serialized-limit" ? sources.Values.Select(item => item.Source).ToArray() : [sources.Values[0].Source];
        if (fault is "empty") { selected = []; }
        if (fault is "large") { selected = Enumerable.Repeat(sources.Values[0].Source, 7).ToArray(); }
        if (fault is "duplicate") { selected = [sources.Values[0].Source, sources.Values[0].Source]; }
        if (fault is "kind") { selected = [selected[0] with { Kind = CustomizationKind.McpServer }]; }
        if (fault is "readonly") { selected = [selected[0] with { IsReadOnly = true }]; }
        if (fault is "unavailable") { selected = [selected[0] with { UnavailableReason = "revoked" }]; }
        if (fault is "observation") { sources.ReadOverride = _ => sources.Values[1]; }
        if (fault is "fields") { sources.Values[0] = sources.Values[0] with { Description = new string('x', 257) }; }
        if (fault is "error") { sources.Error = new InvalidDataException("BODY-SECRET"); }
        if (fault is "use-drift") { sources.AfterRead = use.Revoke; }
        if (fault is "reload-drift")
        {
            sources.AfterRead = () =>
            {
                catalogue.ReportReloadFailure(new IOException("unavailable"));
                catalogue.Publish(catalogue.Candidates);
            };
        }
        if (fault is "private-after-read") { sources.AfterRead = () => f.ControlRevision++; }
        if (fault is "late-source")
        { sources.AfterRead = () => sources.Values[0] = sources.Values[0] with { Revision = Guid.NewGuid().ToString("N") }; }
        var target = new SessionWorkspaceEntry(f.Session with { IsActive = fault is not "inactive" }, null);
        var origin = fault is "origin" ? RequestOrigin.HostSystem : fault is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        await new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time)
            .Invoking(service => service.Review(selected, target, "Which helper?", origin, static () => true, Token))
            .Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("mode")]
    [InlineData("provider")]
    [InlineData("session")]
    [InlineData("origin")]
    [InlineData("request")]
    [InlineData("current")]
    [InlineData("duplicate")]
    public async Task DiscoveryBindingCannotBorrowAnotherOwnerRequestChannelOrReview(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var sources = new MetadataSources();
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var target = new SessionWorkspaceEntry(fault is "session" ? f.Session with { SessionId = new(Guid.NewGuid()) } : f.Session, null);
        using var snapshot = new CustomizationDiscoverySnapshot(sources, catalogue, use, [sources.Values[0]], [],
            target, fault is "request" ? "Different request" : "request-secret",
            fault is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, static () => true, f.Time);
        var host = f.Host;
        var context = f.Context(toolMode: fault is "mode" ? ModelToolMode.ReadOnly : ModelToolMode.CustomizationDiscovery);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        if (fault is "provider")
        {
            turn = new(turn.Owner, turn.Issuer, turn.Provenance with { Selection = ModelProviderSelection.CopilotCandidate },
                turn.Context, turn.Wire, turn.SessionGeneration, turn.TaskRevision, turn.ControlRevision, turn.Registration);
        }
        if (fault is "current") { snapshot.Retire(); }
        if (fault is "duplicate") { host.BindCustomizationDiscovery(turn, snapshot); }
        var called = fault is "owner" ? f.Qualified(ModelProviderSelection.OllamaCandidate) : host;
        var bind = () => called.BindCustomizationDiscovery(turn, snapshot);
        bind.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DiscoveryModeRequiresItsBoundReviewedSnapshotAndCanonicalHostExecution()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            f.Context(toolMode: ModelToolMode.CustomizationDiscovery), Token)).Turn!;
        (await f.Host.RunAsync(turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        f.Adapter.Calls.Should().Be(0);
        var sources = new MetadataSources();
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        using var snapshot = await new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time)
            .Review([sources.Values[0].Source], new(f.Session, null), "Which helper?", RequestOrigin.LocalUi, static () => true, Token);
        await f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable))
            .Invoking(service => service.SubmitCustomizationDiscovery(snapshot, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        await f.ConversationService(f.Host)
            .Invoking(service => service.SubmitCustomizationDiscovery(snapshot, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        root.Dispose();
        var host = ConfigureAgentLoop(f, f.Adapter);
        var receipt = await f.ConversationService(host, execution: host).SubmitCustomizationDiscovery(snapshot, static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
    }
}
