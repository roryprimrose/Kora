using System.Text;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Presentation;

public sealed class LocalFileCitationSelectionTests
{
    [Theory]
    [InlineData("query")]
    [InlineData("mode")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("dispose")]
    [InlineData("next")]
    public async Task ResolveAdmitsOnlyOwnedCurrentRowsAndRetiresOnEveryGenerationBoundary(string change)
    {
        using var selection = new LocalFileSearchSelection();
        var file = File();
        var result = await selection.Run("alpha", (_, mode, _) => Task.FromResult(
            new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha", mode,
                DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken)), () => true);
        var citation = result!.Citations.Single();
        selection.IsCurrent(result, () => true).Should().BeTrue();
        selection.Resolve(citation, [file], () => true).Text.Should().Be(file.Text);
        var forged = () => selection.Resolve(citation with { }, [file], () => true);
        forged.Should().Throw<InvalidOperationException>();
        var foreign = () => selection.Resolve(citation, [File()], () => true);
        foreign.Should().Throw<InvalidOperationException>();
        var ambiguous = () => selection.Resolve(citation, [file, file], () => true);
        ambiguous.Should().Throw<InvalidOperationException>();
        var current = true;
        switch (change)
        {
            case "mode": selection.Select(LocalFileRetrievalMode.MarkdownStructure); break;
            case "source": current = false; break;
            case "dispose": selection.Dispose(); break;
            case "next":
                await selection.Run("none", (_, mode, _) => Task.FromResult(
                    LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)), () => true);
                break;
            default: selection.Invalidate(); break;
        }
        selection.IsCurrent(result, () => current).Should().BeFalse();
        var retired = () => selection.Resolve(citation, [file], () => current);
        retired.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(LocalFileSearchOutcome.NoMatch)]
    [InlineData(LocalFileSearchOutcome.Stale)]
    [InlineData(LocalFileSearchOutcome.InvalidQuery)]
    [InlineData(LocalFileSearchOutcome.Denied)]
    public async Task ResolveRefusesNonSuccessfulOutcomeEvenIfItContainsARealCitation(LocalFileSearchOutcome outcome)
    {
        using var selection = new LocalFileSearchSelection();
        var file = File();
        var result = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha",
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken) with { Outcome = outcome };
        await selection.Run("alpha", (_, _, _) => Task.FromResult(result), () => true);
        var resolve = () => selection.Resolve(result.Citations.Single(), [file], () => true);
        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveUsesCoreValidationForOwnedButMalformedRowsAndSuppressesRetiredPublication()
    {
        using var selection = new LocalFileSearchSelection();
        var file = File();
        var result = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha",
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        var citation = result.Citations.Single() with { Excerpt = "forged" };
        result = result with { Citations = [citation] };
        (await selection.Run("alpha", (_, _, _) => Task.FromResult(result), () => true)).Should().BeSameAs(result);
        var resolve = () => selection.Resolve(citation, [file], () => true);
        resolve.Should().Throw<InvalidDataException>();
        selection.Invalidate();
        selection.IsCurrent(result, () => true).Should().BeFalse();
        using var fresh = new LocalFileSearchSelection();
        var absent = () => fresh.Resolve(citation, [file], () => true);
        absent.Should().Throw<InvalidOperationException>();
    }

    private static LocalFileRevision File()
    {
        var bytes = Encoding.UTF8.GetBytes("# original\r\nalpha e\u0301 🙂\r\n");
        return new(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Synthetic\duplicate.md", "exact", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
    }
}
