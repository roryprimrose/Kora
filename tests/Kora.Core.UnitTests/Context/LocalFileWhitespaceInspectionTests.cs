using System.Text;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileWhitespaceInspectionTests
{
    [Theory]
    [InlineData("", 0, 0, 0, 0, 0)]
    [InlineData("\r\n", 1, 0, 0, 0, 0)]
    [InlineData("\n", 0, 1, 0, 0, 0)]
    [InlineData("\r", 0, 0, 1, 0, 0)]
    [InlineData("\r\r\n\n\r\n\r", 2, 1, 2, 0, 0)]
    [InlineData("\n\r", 0, 1, 1, 0, 0)]
    [InlineData("\r\n\r\n", 2, 0, 0, 0, 0)]
    [InlineData(" \t  \t ", 0, 0, 0, 2, 4)]
    [InlineData("\u00a0\u1680\u2000\u2003\u2028\u2029\u202f\u205f\u3000\ufeff🙂e\u0301", 0, 0, 0, 0, 0)]
    [InlineData("\t🙂 \r\nx\nx\r\t ", 1, 1, 1, 2, 2)]
    public void CountsOnlyExactPermittedCharactersWithoutChangingTheAdmittedText(
        string text, int pairs, int lf, int cr, int tabs, int spaces)
    {
        var file = File(text);
        var reference = file.Reference;
        var original = file.Text;
        LocalFileWhitespaceInspection.Count(file, TestContext.Current.CancellationToken).Should().Be(new LocalFileWhitespaceCounts(pairs, lf, cr, tabs, spaces));
        file.Text.Should().BeSameAs(original);
        file.Text.Should().Be(text.TrimStart('\ufeff'));
        file.Reference.Should().Be(reference);
        LocalFileLiteralFind.FindCounted(file, "x", LocalFileFindDirection.First).Count
            .Should().Be(text.Count(character => character == 'x'));
    }

    [Theory]
    [InlineData(' ', 0, 0, 0, 0, LocalFilePolicy.MaximumBytes)]
    [InlineData('\t', 0, 0, 0, LocalFilePolicy.MaximumBytes, 0)]
    [InlineData('\r', 0, 0, LocalFilePolicy.MaximumBytes, 0, 0)]
    [InlineData('\n', 0, LocalFilePolicy.MaximumBytes, 0, 0, 0)]
    public void FullActualSourceBudgetProducesFiniteExactCounts(char character, int pairs, int lf, int cr, int tabs, int spaces)
    {
        var file = File(new(character, LocalFilePolicy.MaximumBytes));
        LocalFileWhitespaceInspection.Count(file, TestContext.Current.CancellationToken).Should().Be(new LocalFileWhitespaceCounts(pairs, lf, cr, tabs, spaces));
    }

    [Fact]
    public void FullBudgetPairsAtBothBoundariesAndOriginalBomAreNotReinterpreted()
    {
        var pairs = string.Concat(Enumerable.Repeat("\r\n", LocalFilePolicy.MaximumBytes / 2));
        LocalFileWhitespaceInspection.Count(File(pairs), TestContext.Current.CancellationToken).CrLfPairs.Should().Be(LocalFilePolicy.MaximumBytes / 2);
        var file = File("\ufeff \r\n\ufeff\t");
        file.Text.Should().Be(" \r\n\ufeff\t");
        LocalFileWhitespaceInspection.Count(file, TestContext.Current.CancellationToken).Should().Be(new LocalFileWhitespaceCounts(1, 0, 0, 1, 1));
    }

    [Fact]
    public void MissingAndCancelledSourcesNeverBecomeZeroCounts()
    {
        var missing = () => LocalFileWhitespaceInspection.Count(null!);
        missing.Should().Throw<ArgumentNullException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var count = () => LocalFileWhitespaceInspection.Count(File(""), cancellation.Token);
        count.Should().Throw<OperationCanceledException>();
    }

    private static LocalFileRevision File(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var review = new LocalFileReview(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Synthetic\file.txt", "synthetic", bytes.Length, DateTimeOffset.UnixEpoch));
        return new(review, bytes, DateTimeOffset.UnixEpoch);
    }
}
