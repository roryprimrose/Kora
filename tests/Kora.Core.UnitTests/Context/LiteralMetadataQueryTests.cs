using AwesomeAssertions;

using Kora.Core.Context;

namespace Kora.Core.UnitTests.Context;

public sealed class LiteralMetadataQueryTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" exact spaces ")]
    [InlineData("* [literal] .\\not-a-path")]
    [InlineData("é漢😀")]
    public void AcceptsLiteralUnmodifiedSingleLineUnicode(string query)
    {
        ((Action)(() => LiteralMetadataQuery.Validate(query))).Should().NotThrow();
        LiteralMetadataQuery.Validate(new string('a', 256));
        LiteralMetadataQuery.Validate(new string('é', 256));
        LiteralMetadataQuery.Validate(string.Concat(Enumerable.Repeat("😀", 128)));
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\u202e")]
    [InlineData("\u200b")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void RejectsUnsafeUnicodeWithoutNormalization(string query) =>
        ((Action)(() => LiteralMetadataQuery.Validate(query))).Should().Throw<InvalidDataException>();

    [Fact]
    public void RejectsNullMalformedAndOverBoundQueriesExplicitly()
    {
        ((Action)(() => LiteralMetadataQuery.Validate(null!))).Should().Throw<ArgumentNullException>();
        foreach (var query in new[] { new string('\ud800', 1), new string('\udc00', 1),
            new string('a', 257), new string('界', 171), string.Concat(Enumerable.Repeat("😀", 129)) })
        {
            ((Action)(() => LiteralMetadataQuery.Validate(query))).Should().Throw<InvalidDataException>();
        }
    }
}
