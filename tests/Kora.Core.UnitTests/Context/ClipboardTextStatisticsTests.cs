using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class ClipboardTextStatisticsTests
{
    [Theory]
    [InlineData("hello world", 11, 11, 11, 2, 1)]
    [InlineData(" \t\r\n", 4, 4, 4, 0, 2)]
    [InlineData("one\r\ntwo\rthree\n", 15, 15, 15, 3, 4)]
    [InlineData("\r\r\n\n", 4, 4, 4, 0, 4)]
    [InlineData("e\u0301 \u00e9 \U0001f642", 11, 7, 6, 3, 1)]
    [InlineData("a\u0085b\u2028c\u2029", 11, 6, 6, 3, 4)]
    [InlineData("a\u00a0b\u2003c\vd\fe", 12, 9, 9, 5, 1)]
    [InlineData("can't,stop x-y", 14, 14, 14, 2, 1)]
    [InlineData("\u200b", 3, 1, 1, 1, 1)]
    public void CountsEntireUnchangedTextUsingExplicitUnicodeAndLineRules(string text, int bytes, int units, int scalars, int words, int lines)
    {
        var snapshot = Snapshot(text);
        ClipboardTextStatistics.Measure(snapshot, TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardTextStatistics(bytes, units, scalars, words, lines));
        snapshot.Text.Should().Be(text);
    }

    [Fact]
    public void ExactByteLimitIncludesFinalScalarAndTrailingEmptyLine()
    {
        var text = new string('a', ClipboardSnapshot.MaximumUtf8Bytes - 5) + "\U0001f642\n";
        var snapshot = Snapshot(text);
        ClipboardTextStatistics.Measure(snapshot, TestContext.Current.CancellationToken).Should().Be(
            new ClipboardTextStatistics(ClipboardSnapshot.MaximumUtf8Bytes, ClipboardSnapshot.MaximumUtf8Bytes - 2,
                ClipboardSnapshot.MaximumUtf8Bytes - 3, 1, 2));
        var oversize = () => Snapshot(text + "a");
        oversize.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NullAndCancellationNeverReturnZeroCounts()
    {
        var missing = () => ClipboardTextStatistics.Measure(null!, CancellationToken.None);
        missing.Should().Throw<ArgumentNullException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => ClipboardTextStatistics.Measure(Snapshot("private"), cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
    }

    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
}
