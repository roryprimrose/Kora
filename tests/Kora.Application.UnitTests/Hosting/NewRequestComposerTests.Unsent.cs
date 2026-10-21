using AwesomeAssertions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Fact]
    public async Task EditingCreatingCancellingAndInspectingNeverSavesUnsentInput()
    {
        using var f = new Fixture();
        var composer = f.Composer;
        composer.Body = "  exact unsent text\r\n ";
        composer.Dirty.Should().BeTrue();
        await composer.Create();
        composer.Review();
        composer.Cancel();
        await composer.Inspect();
        composer.Body.Should().Be("  exact unsent text\r\n ");
        composer.Dirty.Should().BeTrue();
        f.Saves.Should().Be(0);
        f.Retentions.Should().Be(0);
        composer.Body = string.Empty;
        composer.Dirty.Should().BeFalse();
    }
}
