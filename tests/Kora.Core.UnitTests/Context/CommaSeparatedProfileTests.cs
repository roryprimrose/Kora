using System.Text;

using AwesomeAssertions;

using Kora.Core.Context;

namespace Kora.Core.UnitTests.Context;

public sealed class CommaSeparatedProfileTests
{
    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("a", 1, 1, 1)]
    [InlineData("a,b", 1, 2, 2)]
    [InlineData(",", 1, 2, 2)]
    [InlineData(",,", 1, 3, 3)]
    [InlineData("a,b,", 1, 3, 3)]
    [InlineData("\n", 1, 1, 1)]
    [InlineData("\r\n", 1, 1, 1)]
    [InlineData("\n\n", 2, 1, 1)]
    [InlineData("a\n", 1, 1, 1)]
    [InlineData("a\r\n\r\n", 2, 1, 1)]
    [InlineData("a,b\nc\n", 2, 1, 2)]
    [InlineData("a,b\r\nc,d,e", 2, 2, 3)]
    [InlineData("\"a,b\",c", 1, 2, 2)]
    [InlineData("\"a\"\"b\",c", 1, 2, 2)]
    [InlineData("\"\"", 1, 1, 1)]
    [InlineData("\"\"\"\"", 1, 1, 1)]
    [InlineData("\"a\r\nb\nc\rd\",e\r\nf,g", 2, 2, 2)]
    [InlineData(" , , ", 1, 3, 3)]
    [InlineData("é,🙂\n日本語,e\u0301", 2, 2, 2)]
    [InlineData("=HYPERLINK(x),+cmd,-1,@SUM(x)\n\"=A1\n+B2\",literal", 2, 2, 4)]
    [InlineData("a;b\tc", 1, 1, 1)]
    public void CountsCompleteSourceWithoutGuessingOrFieldValues(string text, int records, int minimum, int maximum)
    {
        var result = CommaSeparatedProfile.Inspect(text, TestContext.Current.CancellationToken);
        result.Syntax.Should().Be(records == 0 ? CommaSeparatedSyntax.Empty : CommaSeparatedSyntax.Admitted);
        result.RecordCount.Should().Be(records);
        result.MinimumFields.Should().Be(minimum);
        result.MaximumFields.Should().Be(maximum);
        result.IsRagged.Should().Be(minimum != maximum);
        result.ErrorOffset.Should().BeNull();
        result.ErrorRecord.Should().BeNull();
    }

    [Theory]
    [InlineData("a\"b", CommaSeparatedSyntax.InvalidQuoting, 1, 1)]
    [InlineData(" \"b\"", CommaSeparatedSyntax.InvalidQuoting, 1, 1)]
    [InlineData("\"a\" ", CommaSeparatedSyntax.InvalidQuoting, 3, 1)]
    [InlineData("\"a\";b", CommaSeparatedSyntax.InvalidQuoting, 3, 1)]
    [InlineData("\"", CommaSeparatedSyntax.InvalidQuoting, 1, 1)]
    [InlineData("\"\"\"", CommaSeparatedSyntax.InvalidQuoting, 3, 1)]
    [InlineData("a,b\n\"c", CommaSeparatedSyntax.InvalidQuoting, 6, 2)]
    [InlineData("é,🙂\r\nx\"y", CommaSeparatedSyntax.InvalidQuoting, 7, 2)]
    [InlineData("a\rb", CommaSeparatedSyntax.InvalidRecordSeparator, 1, 1)]
    [InlineData("a\r", CommaSeparatedSyntax.InvalidRecordSeparator, 1, 1)]
    [InlineData("\"a\r\nb\"\n\rx", CommaSeparatedSyntax.InvalidRecordSeparator, 7, 2)]
    public void RefusesProfileMismatchWithTypedExactLocationAndNoPartialCounts(
        string text, CommaSeparatedSyntax syntax, int offset, int record)
    {
        var result = CommaSeparatedProfile.Inspect(text, TestContext.Current.CancellationToken);
        result.Should().Be(new CommaSeparatedInspection(syntax, 0, 0, 0, offset, record));
    }

    [Fact]
    public void FullUtf8BudgetDerivesFiniteMaximumCountsWithoutFieldAllocations()
    {
        var token = TestContext.Current.CancellationToken;
        CommaSeparatedProfile.Inspect("warmup", token);
        var text = new string(',', ClipboardSnapshot.MaximumUtf8Bytes);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = CommaSeparatedProfile.Inspect(text, token);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.MaximumFields.Should().Be(ClipboardSnapshot.MaximumUtf8Bytes + 1);
        result.RecordCount.Should().Be(1);
        allocated.Should().BeLessThan(1024);
        CommaSeparatedProfile.Inspect(new('\n', ClipboardSnapshot.MaximumUtf8Bytes), token)
            .RecordCount.Should().Be(ClipboardSnapshot.MaximumUtf8Bytes);
        CommaSeparatedProfile.Inspect(new('é', ClipboardSnapshot.MaximumUtf8Bytes / 2), token)
            .Syntax.Should().Be(CommaSeparatedSyntax.Admitted);
        var lateInvalid = new string('a', ClipboardSnapshot.MaximumUtf8Bytes - 1) + "\"";
        CommaSeparatedProfile.Inspect(lateInvalid, token).ErrorOffset.Should().Be(ClipboardSnapshot.MaximumUtf8Bytes - 1);
        var asciiOver = () => CommaSeparatedProfile.Inspect(new('a', ClipboardSnapshot.MaximumUtf8Bytes + 1), token);
        var utf8Over = () => CommaSeparatedProfile.Inspect(new('é', ClipboardSnapshot.MaximumUtf8Bytes / 2 + 1), token);
        asciiOver.Should().Throw<InvalidDataException>();
        utf8Over.Should().Throw<InvalidDataException>();
        var malformed = () => CommaSeparatedProfile.Inspect("\ud800", token);
        malformed.Should().Throw<EncoderFallbackException>();
        var nullInput = () => CommaSeparatedProfile.Inspect(null!, token);
        nullInput.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CancellationNeverReturnsAnEmptyOrPartialSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var inspect = () => CommaSeparatedProfile.Inspect("", cancellation.Token);
        inspect.Should().Throw<OperationCanceledException>();
        inspect = () => CommaSeparatedProfile.Inspect(new(',', ClipboardSnapshot.MaximumUtf8Bytes), cancellation.Token);
        inspect.Should().Throw<OperationCanceledException>();
    }
}
