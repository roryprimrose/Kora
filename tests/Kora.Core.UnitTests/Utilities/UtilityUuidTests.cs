using AwesomeAssertions;
using Kora.Core.Utilities;

namespace Kora.Core.UnitTests.Utilities;

public sealed class UtilityUuidTests
{
    [Fact]
    public void ReportsEveryVersionAndVariantNibbleInCanonicalWireOrder()
    {
        const string hex = "0123456789abcdef";
        foreach (var version in Enumerable.Range(0, 16))
        {
            foreach (var variant in Enumerable.Range(0, 16))
            {
                var text = $"12345678-9abc-{hex[version]}def-{hex[variant]}123-456789abcdef";
                var value = UtilityUuid.Parse(text);
                value.Canonical.Should().Be(text);
                value.VersionNibble.Should().Be(version);
                value.IsRfcVariant.Should().Be(variant is >= 8 and <= 11);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("12345678-9abc-4def-8123-456789abcdeF")]
    [InlineData("123456789abc4def8123456789abcdef")]
    [InlineData("{12345678-9abc-4def-8123-456789abcdef}")]
    [InlineData("urn:uuid:12345678-9abc-4def-8123-456789abcdef")]
    [InlineData(" 12345678-9abc-4def-8123-456789abcdef")]
    [InlineData("12345678-9abc-4def-8123-456789abcdef ")]
    [InlineData("12345678\u22129abc-4def-8123-456789abcdef")]
    [InlineData("12345678-9abc-4def-8123-456789abcdeg")]
    [InlineData("12345678-9abc-4def-8123-456789abcde/")]
    [InlineData("12345678-9abc-4def-8123-456789abcde\uFF11")]
    public void RejectsNonCanonicalInput(string text)
    {
        var act = () => UtilityUuid.Parse(text);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsNullAndEveryMisplacedSeparatorOrNonAsciiCharacter()
    {
        var act = () => UtilityUuid.Parse(null!);
        act.Should().Throw<ArgumentNullException>();
        const string text = "12345678-9abc-4def-8123-456789abcdef";
        foreach (var index in Enumerable.Range(0, 36))
        {
            var chars = text.ToCharArray();
            chars[index] = index is 8 or 13 or 18 or 23 ? '0' : '\0';
            var invalid = () => UtilityUuid.Parse(new string(chars));
            invalid.Should().Throw<InvalidDataException>();
        }
        var huge = () => UtilityUuid.Parse(new string('a', 10000));
        huge.Should().Throw<InvalidDataException>();
    }
}
