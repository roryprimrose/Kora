using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Agents;

public sealed class NamedAgentResolverTests
{
    private static readonly string SourcePath = Path.GetFullPath("role.agent.md");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ArtifactFileDocument Document(string name = "Readable role", string body = "Inert guidance.",
        ArtifactKind kind = ArtifactKind.Agent, string command = "role") =>
        ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"---\nname: {name}\ndescription: Role\n---\n{body}", false),
            kind, "additional", command);
    private static ArtifactDefinition Definition() =>
        Document().Definition with { UseControlId = ArtifactUseIdentity.File(SourcePath) };

    [Fact]
    public async Task SelectsOneExactRegisteredRevisionAndRevalidatesTheActualProfileBytes()
    {
        using var catalogue = new ArtifactCatalogue([Definition()]);
        var resolver = new NamedAgentResolver(catalogue, (definition, token) =>
        {
            definition.Should().BeSameAs(catalogue.Candidates.Single());
            token.Should().Be(Token);
            return Task.FromResult(SourcePath);
        });
        var selection = await resolver.ResolveAsync("Readable role", () => true, Token);
        selection.Path.Should().Be(SourcePath);
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
        { UseControlId = ArtifactUseIdentity.File(SourcePath + ".other") };
        using var catalogue = new ArtifactCatalogue(scenario is "duplicate" ? [definition, second] : [definition]);
        if (scenario is "reload") { catalogue.ReportReloadFailure(new IOException("Synthetic reload failure.")); }
        var reads = 0;
        var resolver = new NamedAgentResolver(catalogue, (_, _) => { reads++; return Task.FromResult(SourcePath); });
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
            return Task.FromResult(scenario is "binding" ? SourcePath + ".other" : SourcePath);
        });
        await resolver.Invoking(value => value.ResolveAsync("Readable role", () => current, cancellation.Token))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RelativeHostSourceCannotProduceASelection()
    {
        using var catalogue = new ArtifactCatalogue([Definition()]);
        var reads = 0;
        var resolver = new NamedAgentResolver(catalogue, (_, _) =>
        {
            reads++;
            return Task.FromResult("role.agent.md");
        });
        var failure = await resolver.Invoking(value => value.ResolveAsync("Readable role", () => true, Token))
            .Should().ThrowAsync<ArgumentException>();
        failure.Which.ParamName.Should().Be("canonicalPath");
        reads.Should().Be(1);
    }

    [Fact]
    public async Task AlreadyCancelledSelectionNeverReadsSources()
    {
        using var catalogue = new ArtifactCatalogue([Definition()]);
        var resolver = new NamedAgentResolver(catalogue, (_, _) => throw new InvalidOperationException("Forbidden lookup."));
        await resolver.Invoking(value => value.ResolveAsync("Readable role", () => true, new CancellationToken(true)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("foreign")]
    [InlineData("stale")]
    [InlineData("disabled")]
    [InlineData("wrong-kind")]
    [InlineData("different-name")]
    [InlineData("reload")]
    [InlineData("ineligible")]
    [InlineData("cancelled")]
    public async Task NativeSourceChoiceResolvesOnlyTheExactCurrentCandidateWithoutNameFallback(string scenario)
    {
        var first = Definition();
        var other = new ArtifactDefinition("disk.additional.agent.other", ArtifactKind.Agent, first.Name,
            "Other source", "other", ["other"], "additional", first.Version, first.Digest, first.Content)
        { UseControlId = ArtifactUseIdentity.File(SourcePath + ".other") };
        if (scenario is "disabled") { other = other with { IsUseDisabled = true }; }
        if (scenario is "wrong-kind")
        {
            other = new("disk.additional.instruction.other", ArtifactKind.Instruction, first.Name,
                "Other source", "other", ["other"], "additional", first.Version, first.Digest, first.Content);
        }
        if (scenario is "different-name") { other = Document("Other role").Definition; }
        using var catalogue = new ArtifactCatalogue([first, other]);
        var reads = 0;
        var resolver = new NamedAgentResolver(catalogue, (definition, _) =>
        {
            reads++;
            definition.Should().BeSameAs(other);
            return Task.FromResult(SourcePath + ".other");
        });
        var choices = resolver.ReadCandidates("Readable role", () => true, Token);
        choices.Should().Contain(first);
        choices.Should().HaveCount(scenario is "wrong-kind" or "different-name" ? 1 : 2);
        reads.Should().Be(0, "metadata choice is not source resolution or read consent");
        if (scenario is "foreign") { other = other with { UseControlId = "foreign" }; }
        if (scenario is "stale") { catalogue.Publish([first]); }
        if (scenario is "reload") { catalogue.ReportReloadFailure(new IOException("Unavailable catalogue.")); }
        var token = scenario is "cancelled" ? new CancellationToken(true) : Token;
        if (scenario is "success")
        {
            var selected = await resolver.ResolveAsync(other, () => true, token);
            selected.Definition.Should().BeSameAs(other);
            selected.Path.Should().Be(SourcePath + ".other");
            selected.ValidateSource(Document(command: "other"));
            reads.Should().Be(1);
        }
        else
        {
            await resolver.Invoking(value => value.ResolveAsync(other, () => scenario is not "ineligible", token))
                .Should().ThrowAsync<Exception>();
            reads.Should().Be(0);
        }
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("ineligible")]
    [InlineData("cancelled")]
    public void CandidateChoicesCannotHideUnavailableAuthority(string scenario)
    {
        using var catalogue = new ArtifactCatalogue([Definition()]);
        if (scenario is "reload") { catalogue.ReportReloadFailure(new IOException("Unavailable catalogue.")); }
        var resolver = new NamedAgentResolver(catalogue, (_, _) => throw new InvalidOperationException("Forbidden source lookup."));
        var operation = () => resolver.ReadCandidates("Readable role", () => scenario is not "ineligible",
            scenario is "cancelled" ? new CancellationToken(true) : Token);
        operation.Should().Throw<Exception>();
    }

    private sealed class UsePolicy : IArtifactUsePolicy
    {
        public event EventHandler? Changed;
        public long Revision { get; private set; }
        public bool IsDisabled(string identity) => false;
        public bool IsRevisionAllowed(string identity, string digest) => true;
        internal void Revoke() { Revision++; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
