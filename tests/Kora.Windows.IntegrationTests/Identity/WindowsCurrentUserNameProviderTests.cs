using AwesomeAssertions;

using Kora.Windows.Identity;

namespace Kora.Windows.IntegrationTests.Identity;

public sealed class WindowsCurrentUserNameProviderTests
{
    [Theory]
    [InlineData("Rory Primrose", "Rory")]
    [InlineData(@"DOMAIN\rory", "rory")]
    [InlineData("rory@example.com", "rory")]
    [InlineData("rory.primrose", "rory")]
    [InlineData("Mary-Jane Watson", "Mary-Jane")]
    public void ExtractAddressName_returns_a_suitable_local_address_name(
        string value,
        string expected)
    {
        WindowsCurrentUserNameProvider.ExtractAddressName(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void ExtractAddressName_rejects_unusable_values(string? value)
    {
        WindowsCurrentUserNameProvider.ExtractAddressName(value).Should().BeNull();
    }
}
