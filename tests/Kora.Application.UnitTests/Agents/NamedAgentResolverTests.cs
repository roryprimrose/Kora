using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Agents;

public sealed class NamedAgentResolverTests
{
    private const string Path = @"C:\KoraTestSources\role.agent.md";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ArtifactFileDocument Document(string name = "Readable role", string body = "Inert guidance.",
        ArtifactKind kind = ArtifactKind.Agent, string command = "role") =>
        ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"---\nname: {name}\ndescription: Role\n---\n{body}", false),
            kind, "additional", command);
    private static ArtifactDefinition Definition() =>
        Document().Definition with { UseControlId = ArtifactUseIdentity.File(Path) };

    [Fact]
    public async Task SelectsOneExactRegisteredRevisionAndRevalidatesTheActualProfileBytes()
    {
        using var catalogue = new ArtifactCatalogue([Definition()]);
        var resolver = new NamedAgentResolver(catalogue, (definition, token) =>
        {
            definition.Should().BeSameAs(catalogue.Candidates.Single());
            token.Should().Be(Token);
            return Task.FromResult(Path);
        });
        var selection = await resolver.ResolveAsync("Readable role", () => true, Token);
        selection.Path.Should().Be(Path);
        selection.IsCurrent().Should().BeTrue();
        selection.ValidateSource(Document());
        var renamed = () => selection.ValidateSource(Document("Different name"));
        renamed.Should().Throw<InvalidOperationException>();
        var edited = () => selection.ValidateSource(Document(body: "Different bytes."));
        edited.Should().Throw<InvalidOperationException>();
        catalogue.Publish([Definition()]);
        selection.IsCurrent().Should().BeFalse();
        var stale = () => selection.ValidateSource(Document());
        stale.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("case")]
    [InlineData("different-kind")]
    [InlineData("duplicate")]
    [InlineData("disabled")]
    [InlineData("not-enabled")]
    [InlineData("no-file-binding")]
    [InlineData("reload")]
    [InlineData("ineligible")]
    public async Task RefusesAmbiguityDisabledAndUntrustedAuthorityBeforeAnySourceLookup(string scenario)
    {
        var definition = Definition();
        if (scenario is "different-kind") { definition = Document(kind: ArtifactKind.Prompt).Definition; }
        if (scenario is "disabled") { definition = definition with { IsUseDisabled = true }; }
        if (scenario is "not-enabled") { definition = definition with { IsUseEnabled = false }; }
        if (scenario is "no-file-binding") { definition = definition with { UseControlId = null }; }
        var second = new ArtifactDefinition("disk.additional.agent.second", ArtifactKind.Agent, "Readable role",
            "Other source", "second", ["second"], "additional", definition.Version, definition.Digest, definition.Content)
        { UseControlId = ArtifactUseIdentity.File(Path + ".other") };
        using var catalogue = new ArtifactCatalogue(scenario is "duplicate" ? [definition, second] : [definition]);
        if (scenario is "reload") { catalogue.ReportReloadFailure(new IOException("Synthetic reload failure.")); }
        var reads = 0;
        var resolver = new NamedAgentResolver(catalogue, (_, _) => { reads++; return Task.FromResult(Path); });
        var name = scenario is "missing" ? "Missing role" : scenario is "case" ? "readable role" : "Readable role";
        await resolver.Invoking(value => value.ResolveAsync(name, () => scenario is not "ineligible", Token))
            .Should().ThrowAsync<InvalidOperationException>();
        reads.Should().Be(0);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("removed")]
    [InlineData("revoked")]
    [InlineData("reload")]
    [InlineData("reload-recovered")]
    [InlineData("use")]
    [InlineData("cancel")]
    [InlineData("source-failure")]
    public async Task RechecksAuthorityAfterAwaitedHostLookupWithoutSubstitutingAnotherSource(string scenario)
    {
        var policy = new UsePolicy();
        using var catalogue = new ArtifactCatalogue([Definition()], policy);
        var current = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var resolver = new NamedAgentResolver(catalogue, (_, _) =>
        {
            if (scenario is "removed") { catalogue.Publish([Document("Other role").Definition]); }
            if (scenario is "revoked") { current = false; }
            if (scenario is "reload" or "reload-recovered")
            { catalogue.ReportReloadFailure(new IOException("Synthetic reload failure.")); }
            if (scenario is "reload-recovered") { catalogue.Publish([Definition()]); }
            if (scenario is "use") { policy.Revoke(); }
            if (scenario is "cancel") { cancellation.Cancel(); }
            if (scenario is "source-failure") { throw new IOException("Exact source missing."); }
            return Task.FromResult(scenario is "binding" ? Path + ".other" : Path);
        });
        await resolver.Invoking(value => value.ResolveAsync("Readable role", () => current, cancellation.Token))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task AlreadyCancelledSelectionNeverReadsSources()
    {
        using var catalogue = new ArtifactCatalogue([Definition()]);
        var resolver = new NamedAgentResolver(catalogue, (_, _) => throw new InvalidOperationException("Forbidden lookup."));
        await resolver.Invoking(value => value.ResolveAsync("Readable role", () => true, new CancellationToken(true)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class UsePolicy : IArtifactUsePolicy
    {
        public event EventHandler? Changed;
        public long Revision { get; private set; }
        public bool IsDisabled(string identity) => false;
        internal void Revoke() { Revision++; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
