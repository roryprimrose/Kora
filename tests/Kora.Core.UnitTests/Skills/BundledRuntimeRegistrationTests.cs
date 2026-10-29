using AwesomeAssertions;

using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class BundledRuntimeRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exact_inventory_and_device_pins_roundtrip_without_execution_authority(bool confirmed)
    {
        var registration = new BundledRuntimeRegistration(new('a', 64), new('b', 64), confirmed);
        registration.InventoryDigest.Should().Be(new string('a', 64));
        registration.DeviceDigest.Should().Be(new string('b', 64));
        registration.Confirmed.Should().Be(confirmed);
        BundledRuntimeRegistration.Parse(registration.Serialize()).Should().Be(registration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kora-bundled-runtime-v2\na\nb\nconfirmed")]
    [InlineData("kora-bundled-runtime-v1\na\nb\nqualified")]
    [InlineData("kora-bundled-runtime-v1\na\nb\nconfirmed")]
    [InlineData("kora-bundled-runtime-v1\n")]
    public void Invalid_saved_state_is_never_reinterpreted_as_missing_or_qualified(string text) =>
        ((Action)(() => BundledRuntimeRegistration.Parse(text))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEF")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Invalid_digest_is_rejected_for_either_identity(string? digest)
    {
        ((Action)(() => _ = new BundledRuntimeRegistration(digest!, new('a', 64), false))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new BundledRuntimeRegistration(new('a', 64), digest!, false))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Null_text_is_not_a_saved_registration() =>
        ((Action)(() => BundledRuntimeRegistration.Parse(null!))).Should().Throw<ArgumentNullException>();

    [Theory]
    [InlineData('0')]
    [InlineData('9')]
    [InlineData('f')]
    public void Lowercase_hex_boundaries_are_valid(char digit)
    {
        var value = new BundledRuntimeRegistration(new(digit, 64), new(digit, 64), false);
        BundledRuntimeRegistration.Parse(value.Serialize()).Should().Be(value);
    }

    [Fact]
    public void Characters_below_hex_digits_are_not_saved_digests() =>
        ((Action)(() => _ = new BundledRuntimeRegistration(new('/', 64), new('a', 64), false)))
            .Should().Throw<InvalidDataException>();
}
