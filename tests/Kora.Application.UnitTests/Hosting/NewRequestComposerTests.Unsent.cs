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

    [Theory]
    [InlineData("unchanged")]
    [InlineData("edit")]
    [InlineData("aba")]
    [InlineData("cancel")]
    [InlineData("private")]
    [InlineData("permission")]
    [InlineData("revision")]
    [InlineData("close")]
    [InlineData("busy")]
    [InlineData("target")]
    [InlineData("create")]
    public async Task CapturedCloseEligibilityRequiresUnchangedExactInputAndPrivateSource(string change)
    {
        using var f = new Fixture();
        var c = f.Composer;
        c.Body = "original";
        if (change is "target") { await c.Create(); }
        var current = c.CaptureUnsentCloseEligibility();
        current().Should().BeTrue();
        if (change is "edit") { c.Body = "new input"; }
        if (change is "aba") { c.Body = "new"; c.Body = "original"; }
        if (change is "cancel") { c.Cancel(); }
        if (change is "private") { f.Access.CanInspect = false; }
        if (change is "permission") { f.Access.CanControl = false; }
        if (change is "revision") { f.Access.ControlRevision++; }
        if (change is "close") { c.Dispose(); }
        Task? creating = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (change is "busy")
        {
            f.CreateWait = () => release.Task;
            creating = c.Create();
        }
        if (change is "target")
        {
            f.Entry = f.Entry! with { Metadata = f.Entry!.Metadata! with { Revision = new(2) } };
            await c.Inspect();
        }
        if (change is "create") { await c.Create(); }
        current().Should().Be(change is "unchanged");
        release.SetResult();
        if (creating is not null) { await creating; }
    }
}
