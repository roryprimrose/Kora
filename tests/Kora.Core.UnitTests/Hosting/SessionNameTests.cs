using AwesomeAssertions;

using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionNameTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" name")]
    [InlineData("name ")]
    [InlineData("a\nb")]
    [InlineData("a\u200Db")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    [InlineData("e\u0301")]
    public void Invalid_names_are_rejected_without_trimming_or_normalizing(string value)
    {
        var create = () => new SessionName(value);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Scalar_byte_and_utf16_boundaries_are_authoritative()
    {
        new SessionName(new('a', 120)).Value.Should().HaveLength(120);
        var supplementary = string.Concat(Enumerable.Repeat("\U0001F600", 120));
        new SessionName(supplementary).Value.Should().Be(supplementary);
        new SessionName("é 日本語").Value.Should().Be("é 日本語");
        var scalars = () => new SessionName(new('a', 121));
        scalars.Should().Throw<InvalidDataException>();
        var bytes = () => new SessionName(supplementary + "\U0001F600");
        bytes.Should().Throw<InvalidDataException>();
        var surrogate = () => new SessionName("a" + (char)0xD800);
        surrogate.Should().Throw<InvalidDataException>();
        var lowSurrogate = () => new SessionName("a" + (char)0xDC00);
        lowSurrogate.Should().Throw<InvalidDataException>();
        var missing = () => new SessionName(null!);
        missing.Should().Throw<ArgumentNullException>();
    }
}
