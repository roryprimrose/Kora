using AwesomeAssertions;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task Presentation_retirement_clears_private_selection_and_preserves_filters_without_effects()
    {
        await using var f = new Fixture();
        f.State.ActionIdFilter = "bounded.read";
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.State.UriPatternInput = "https://example.com/private";
        var reads = f.Store.Pages;
        f.State.RetirePresentation();
        f.State.ActionIdFilter.Should().Be("bounded.read");
        f.State.Records.Should().BeEmpty();
        f.State.PermissionRows.Should().BeEmpty();
        f.State.Selected.Should().BeNull();
        f.State.Reviewed.Should().BeFalse();
        f.State.BatchReviewed.Should().BeFalse();
        f.State.UriPatternInput.Should().BeEmpty();
        f.State.Inspection.Should().NotContain(f.Preview.Grant.Id.Value.ToString("D"));
        f.State.Message.Should().Contain("presentation ended");
        f.Store.Pages.Should().Be(reads);
        f.Store.Revokes.Should().Be(0);
        await f.State.RefreshAsync(f.Token);
        f.State.Records.Should().ContainSingle();
        f.State.Dispose();
        f.State.RetirePresentation();
        f.State.CanRevoke.Should().BeFalse();
        f.Store.Revokes.Should().Be(0);
    }
}
