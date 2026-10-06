using AwesomeAssertions;

namespace Kora.Application.UnitTests;

public sealed class ApplicationInfoTests
{
    [Theory]
    [InlineData("4.5.6")]
    [InlineData("0.1.0")]
    [InlineData("0.1.0-beta12")]
    public void Version_preserves_the_entry_assembly_informational_version(string version)
    {
        var info = new AssemblyApplicationInfo(() => version);

        info.Version.Should().Be(version);
    }

    [Fact]
    public void Version_reports_development_when_entry_assembly_is_unavailable()
    {
        var info = new AssemblyApplicationInfo(() => null);

        info.Version.Should().Be("development");
    }

    [Fact]
    public void Default_provider_reads_the_process_entry_assembly()
    {
        var info = new AssemblyApplicationInfo();

        info.Version.Should().NotBeNullOrWhiteSpace();
        info.Version.Should().NotBe("development");
    }
}