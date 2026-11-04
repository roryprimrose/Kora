using AwesomeAssertions;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class ModelSkillUseProposalTests
{
    [Fact]
    public void OneExactOpaqueSkillReferenceIsDataOnly()
    {
        const string reference = "abc12345123412341234123412341234";
        ModelSkillUseProposal.Parse("{\"skillRef\":\"" + reference + "\"}").Reference.Should().Be(reference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"SkillRef\":\"abc12345123412341234123412341234\"}")]
    [InlineData("{\"skillRef\":null}")]
    [InlineData("{\"skillRef\":1}")]
    [InlineData("{\"skillRef\":\"00000000000000000000000000000000\"}")]
    [InlineData("{\"skillRef\":\"ABC12345123412341234123412341234\"}")]
    [InlineData("{\"skillRef\":\"C:\\\\model\\\\path\"}")]
    [InlineData("{\"skillRef\":\"abc12345123412341234123412341234\",\"approved\":true}")]
    [InlineData("{\"skillRef\":\"abc12345123412341234123412341234\",\"task\":\"replacement\"}")]
    [InlineData("{\"skillRef\":\"abc12345123412341234123412341234\",\"skillRef\":\"abc12345123412341234123412341234\"}")]
    [InlineData("{\"skillRef\":\"abc12345123412341234123412341234\"} trailing")]
    public void MalformedDuplicateForeignShapeAndForgedAuthorityAreRejectedWhole(string answer) =>
        ((Action)(() => ModelSkillUseProposal.Parse(answer))).Should().Throw<InvalidDataException>();

    [Fact]
    public void CompleteByteBoundIsNotTruncated() =>
        ((Action)(() => ModelSkillUseProposal.Parse(new string(' ', 129)))).Should().Throw<InvalidDataException>();

    [Fact]
    public async Task ReadersWithoutExactSelectionSupportFailExplicitlyWithoutDiscoveryFallback()
    {
        ISharedSkillSourceReader reader = new UnsupportedReader();
        var source = new SharedSkillSource(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        var binding = new SharedSkillFileBinding(source, "example\\SKILL.md", new string('b', 48), new string('c', 48), 1);
        await ((Func<Task>)(async () => await reader.BindFileAsync([source], "selected", TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(async () => await reader.ReadFileAsync(binding, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class UnsupportedReader : ISharedSkillSourceReader
    {
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No selection fallback.");
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No discovery fallback.");
    }
}
