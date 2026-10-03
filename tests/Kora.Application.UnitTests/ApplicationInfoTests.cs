using AwesomeAssertions;

namespace Kora.Application.UnitTests;

public sealed class ApplicationInfoTests
{
    [Fact]
    public void Version_uses_three_part_entry_assembly_version()
    {
        var info = new AssemblyApplicationInfo(() => new Version(4, 5, 6, 7));

        info.Version.Should().Be("4.5.6");
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