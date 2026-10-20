using AwesomeAssertions;
using Kora.Core.Context;

namespace Kora.Core.UnitTests.Context;

public sealed class ClipboardBase64PolicyTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("AA==", 1)]
    [InlineData("AAA=", 2)]
    [InlineData("AAAA", 3)]
    [InlineData("Zg==", 1)]
    [InlineData("Zm8=", 2)]
    [InlineData("Zm9v", 3)]
    [InlineData("Zm9vYg==", 4)]
    [InlineData("Zm9vYmE=", 5)]
    [InlineData("Zm9vYmFy", 6)]
    [InlineData("AZaz09+/", 6)]
    [InlineData("////", 3)]
    [InlineData("/w==", 1)]
    [InlineData("//8=", 2)]
    public void CompleteCanonicalEncodingHasExactByteCountWithoutDecodedData(string encoded, int bytes)
    {
        var result = ClipboardBase64Policy.Inspect(encoded, TestContext.Current.CancellationToken);
        result.Should().Be(new ClipboardBase64Inspection.AdmittedProfile(bytes));
    }

    [Theory]
    [InlineData("A", ClipboardBase64InvalidReason.EncodedLengthNotMultipleOfFour, 1)]
    [InlineData("AA", ClipboardBase64InvalidReason.EncodedLengthNotMultipleOfFour, 2)]
    [InlineData("AAA", ClipboardBase64InvalidReason.EncodedLengthNotMultipleOfFour, 3)]
    [InlineData("AAAA=", ClipboardBase64InvalidReason.EncodedLengthNotMultipleOfFour, 5)]
    [InlineData("AB==", ClipboardBase64InvalidReason.NonCanonicalUnusedBits, 1)]
    [InlineData("AAB=", ClipboardBase64InvalidReason.NonCanonicalUnusedBits, 2)]
    [InlineData("AAF=", ClipboardBase64InvalidReason.NonCanonicalUnusedBits, 2)]
    [InlineData("AZ==", ClipboardBase64InvalidReason.NonCanonicalUnusedBits, 1)]
    [InlineData("=AAA", ClipboardBase64InvalidReason.InvalidPadding, 0)]
    [InlineData("A===", ClipboardBase64InvalidReason.InvalidPadding, 1)]
    [InlineData("====", ClipboardBase64InvalidReason.InvalidPadding, 0)]
    [InlineData("AA=A", ClipboardBase64InvalidReason.InvalidPadding, 3)]
    [InlineData("AA==AAAA", ClipboardBase64InvalidReason.InvalidPadding, 2)]
    [InlineData("AAA=AAAA", ClipboardBase64InvalidReason.InvalidPadding, 3)]
    [InlineData("AAAA====", ClipboardBase64InvalidReason.InvalidPadding, 4)]
    [InlineData("AAAA AA=", ClipboardBase64InvalidReason.InvalidAlphabet, 4)]
    [InlineData("-AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData("_AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData(" AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData("AAA ", ClipboardBase64InvalidReason.InvalidAlphabet, 3)]
    [InlineData("A\tAA", ClipboardBase64InvalidReason.InvalidAlphabet, 1)]
    [InlineData("A\r\nA", ClipboardBase64InvalidReason.InvalidAlphabet, 1)]
    [InlineData("A\0AA", ClipboardBase64InvalidReason.InvalidAlphabet, 1)]
    [InlineData("A\u007fAA", ClipboardBase64InvalidReason.InvalidAlphabet, 1)]
    [InlineData("AéAA", ClipboardBase64InvalidReason.InvalidAlphabet, 1)]
    [InlineData("A\u200bAA", ClipboardBase64InvalidReason.InvalidAlphabet, 1)]
    [InlineData("A😀A", ClipboardBase64InvalidReason.InvalidAlphabet, 1)]
    [InlineData("@AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData("[AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData("`AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData("{AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData(":AAA", ClipboardBase64InvalidReason.InvalidAlphabet, 0)]
    [InlineData("data:text/plain;base64,AA==AAAAA", ClipboardBase64InvalidReason.InvalidAlphabet, 4)]
    public void InvalidCompleteEncodingReturnsOnlyTypedReasonAndOffset(string encoded, ClipboardBase64InvalidReason reason, int offset)
    {
        var result = ClipboardBase64Policy.Inspect(encoded, TestContext.Current.CancellationToken);
        result.Should().Be(new ClipboardBase64Inspection.InvalidProfile(reason, offset));
        result.Should().NotBeOfType<ClipboardBase64Inspection.AdmittedProfile>();
    }

    [Fact]
    public void FullSourceBoundAndCheckedCountAreExact()
    {
        var maximum = new string('A', ClipboardSnapshot.MaximumUtf8Bytes);
        ClipboardBase64Policy.Inspect(maximum, TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardBase64Inspection.AdmittedProfile(196608));
        ClipboardBase64Policy.Inspect(maximum[..^2] + "==", TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardBase64Inspection.AdmittedProfile(196606));
        ClipboardBase64Policy.Inspect(maximum[..^1] + "=", TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardBase64Inspection.AdmittedProfile(196607));
        ClipboardBase64Policy.Inspect(maximum[..^1] + "!", TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardBase64Inspection.InvalidProfile(
                ClipboardBase64InvalidReason.InvalidAlphabet, maximum.Length - 1));
        ClipboardBase64Policy.Inspect(maximum + "AAAA", TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardBase64Inspection.InvalidProfile(
                ClipboardBase64InvalidReason.EncodedSizeExceeded, maximum.Length));
    }

    [Fact]
    public void MissingInputAndCancellationCannotBecomeEmptySuccess()
    {
        var missing = () => ClipboardBase64Policy.Inspect(null!, CancellationToken.None);
        missing.Should().Throw<ArgumentNullException>();
        var cancelled = () => ClipboardBase64Policy.Inspect("", new CancellationToken(true));
        cancelled.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void EverySextetAndEveryUnusedBitCombinationObeysCanonicalGrammar()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        foreach (var character in alphabet)
        {
            ClipboardBase64Policy.Inspect(new string(character, 4), CancellationToken.None)
                .Should().Be(new ClipboardBase64Inspection.AdmittedProfile(3));
            var value = alphabet.IndexOf(character, StringComparison.Ordinal);
            var one = ClipboardBase64Policy.Inspect("A" + character + "==", CancellationToken.None);
            var two = ClipboardBase64Policy.Inspect("AA" + character + "=", CancellationToken.None);
            (one is ClipboardBase64Inspection.AdmittedProfile).Should().Be(value % 16 == 0);
            (two is ClipboardBase64Inspection.AdmittedProfile).Should().Be(value % 4 == 0);
        }
    }
}
