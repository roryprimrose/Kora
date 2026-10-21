using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class ClipboardJsonSyntaxTests
{
    [Theory]
    [InlineData("{}", JsonValueKind.Object)]
    [InlineData("[]", JsonValueKind.Array)]
    [InlineData("\"\\uD83D\\uDE42\\n\\\"\\\\\\u0000\"", JsonValueKind.String)]
    [InlineData("true", JsonValueKind.True)]
    [InlineData("false", JsonValueKind.False)]
    [InlineData("null", JsonValueKind.Null)]
    [InlineData(" \r\n -1.234e+99 \t ", JsonValueKind.Number)]
    [InlineData("0", JsonValueKind.Number)]
    [InlineData("1e99999", JsonValueKind.Number)]
    [InlineData("{\"duplicate\":1,\"duplicate\":false}", JsonValueKind.Object)]
    [InlineData("{\"array\":[null,true,false,1,\"x\",{}]}", JsonValueKind.Object)]
    public void CompleteStandardValuesIncludingDuplicatesAreSyntaxNotSemanticEvidence(string text, JsonValueKind kind)
    {
        var source = Snapshot(text);
        var before = source.GetUtf8Bytes();
        var result = ClipboardJsonSyntax.Validate(source, TestContext.Current.CancellationToken);
        result.Should().Be(new ClipboardJsonSyntaxResult(ClipboardJsonSyntaxOutcome.Valid, kind));
        source.Text.Should().Be(text);
        source.GetUtf8Bytes().Should().Equal(before);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\r\n\t ")]
    [InlineData("{} []")]
    [InlineData("null true")]
    [InlineData("/*comment*/ {}")]
    [InlineData("{\"x\":1 // comment\n}")]
    [InlineData("{\"x\":1,}")]
    [InlineData("[1,]")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData("1.")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("\"\\x12\"")]
    [InlineData("\"line\nbreak\"")]
    [InlineData("{")]
    [InlineData("\uFEFF{}")]
    public void StrictWholeSourceRefusalHasOnlyTypedReasonAndNumericPosition(string text)
    {
        var result = ClipboardJsonSyntax.Validate(Snapshot(text), TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(ClipboardJsonSyntaxOutcome.InvalidSyntax);
        result.RootKind.Should().BeNull();
        result.LineNumber.Should().BeGreaterThanOrEqualTo(0);
        result.BytePositionInLine.Should().BeGreaterThanOrEqualTo(0);
        result.ToString().Should().NotContain(text.Trim() is { Length: > 8 } value ? value : "private-marker");
    }

    [Fact]
    public void PositionsUseZeroBasedUtf8BytesWithoutPropertyOrExceptionContent()
    {
        var result = ClipboardJsonSyntax.Validate(Snapshot("{\n\"private-marker-é\":}"), TestContext.Current.CancellationToken);
        result.Should().Be(new ClipboardJsonSyntaxResult(ClipboardJsonSyntaxOutcome.InvalidSyntax,
            LineNumber: 1, BytePositionInLine: 20));
        result.ToString().Should().NotContain("private").And.NotContain("JsonException");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Depth64IsValidAnd65IsExplicitRefusal(bool objects)
    {
        string Nest(int count) => string.Concat(Enumerable.Repeat(objects ? "{\"a\":" : "[", count))
            + "null" + new string(objects ? '}' : ']', count);
        ClipboardJsonSyntax.Validate(Snapshot(Nest(64)), TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Valid);
        var result = ClipboardJsonSyntax.Validate(Snapshot("\n " + Nest(65)), TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(ClipboardJsonSyntaxOutcome.DepthLimit);
        result.RootKind.Should().BeNull();
        result.LineNumber.Should().Be(1);
        result.BytePositionInLine.Should().Be(1 + 64 * (objects ? 5 : 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullByteBoundaryIsParsedWithoutTruncatingAnySuffix(bool multibyte)
    {
        var text = "\"" + new string(multibyte ? 'é' : 'x', (ClipboardSnapshot.MaximumUtf8Bytes - 2) / (multibyte ? 2 : 1)) + "\"";
        Snapshot(text).Utf8Bytes.Should().Be(ClipboardSnapshot.MaximumUtf8Bytes);
        ClipboardJsonSyntax.Validate(Snapshot(text), TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Valid);
        ClipboardJsonSyntax.Validate(Snapshot(text[..^1] + "x"), TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.InvalidSyntax);
        var oversize = () => Snapshot(text + " ");
        oversize.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SourceCanonicalPolicyRejectsEmptyNulAndMalformedSurrogatesBeforeSyntax()
    {
        var empty = () => Snapshot("");
        empty.Should().Throw<InvalidDataException>();
        var nul = () => Snapshot("\"\0\"");
        nul.Should().Throw<InvalidDataException>();
        var malformed = () => Snapshot("\"\ud800\"");
        malformed.Should().Throw<EncoderFallbackException>();
        // Escaped surrogate syntax does not claim semantic Unicode scalar validation.
        ClipboardJsonSyntax.Validate(Snapshot("\"\\ud800\""), TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Valid);
        var missing = () => ClipboardJsonSyntax.Validate(null!, CancellationToken.None);
        missing.Should().Throw<ArgumentNullException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var run = () => ClipboardJsonSyntax.Validate(Snapshot("{}"), cancelled.Token);
        run.Should().Throw<OperationCanceledException>();
    }

    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
}
