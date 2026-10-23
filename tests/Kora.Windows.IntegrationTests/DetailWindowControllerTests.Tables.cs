using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    private const string TableSource = "| Name | State |\n| --- | --- |\n| Alpha | Ready |";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TableDetailUsesExactImmutableAdmissionAndPrivacyClosureRegardlessOfProfileAvailability(bool qualified)
    {
        using var fixture = new Fixture(pipeTablesAvailable: qualified);
        fixture.Documents.Pages[0] = fixture.Documents.GetStartPage() with { Markdown = TableSource };
        var page = fixture.Documents.GetStartPage();
        var status = fixture.Controller.OpenEmbeddedPage(page, null);
        status.Should().Contain(NativeMarkdownTableProfile.Name);
        var view = fixture.Views.Single();
        view.State.RenderState.Should().Be(qualified ? DetailRenderState.Rendered : DetailRenderState.SourceFallback);
        view.State.ActiveText.Should().Contain(qualified ? "Column 1 (Name): Alpha" : TableSource);
        fixture.Controller.OpenEmbeddedPage(page, null).Should().Contain("Activated");
        fixture.Views.Should().ContainSingle();
        view.State.SetSource(true);
        view.State.ActiveText.Should().Be(TableSource);
        var generation = view.State.Generation;
        fixture.Accessible = false;
        fixture.Controller.ClearForPrivacy();
        view.Cleared.Should().BeTrue();
        view.State.ActiveText.Should().BeEmpty();
        view.State.Content.Should().BeNull();
        await fixture.Controller.CopyAsync(view.State.Reference, generation, true);
        fixture.Clipboard.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredMarkdownAnswerCannotRevealTableSourceOrCopyAcrossSessionOwnership(bool qualified)
    {
        static AdmittedDetailContent Answer(string source)
        {
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            return new(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
                DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
                "Answer", "Host-admitted immutable Markdown", source,
                new(request.SessionId, request.RequestId, request.TaskId));
        }
        using var fixture = new Fixture(pipeTablesAvailable: qualified);
        var first = Answer(TableSource);
        var second = Answer(TableSource.Replace("Alpha", "Other session", StringComparison.Ordinal));
        var admitted = true;
        fixture.Controller.OpenCurrentResponse(first, null, () => admitted).Should().BeTrue();
        fixture.Controller.OpenCurrentResponse(second, null, () => true).Should().BeTrue();
        var firstView = fixture.Views[0];
        var secondView = fixture.Views[1];
        firstView.State.ActiveText.Should().Contain("Alpha").And.NotContain("Other session");
        secondView.State.ActiveText.Should().Contain("Other session").And.NotContain("Alpha");
        var generation = firstView.State.Generation;
        admitted = false;
        firstView.State.ActiveText.Should().BeEmpty();
        firstView.State.Content.Should().BeNull();
        fixture.Controller.RetireUnavailableCurrentResponses();
        firstView.Cleared.Should().BeTrue();
        secondView.Cleared.Should().BeFalse();
        secondView.State.ActiveText.Should().Contain("Other session");
        await fixture.Controller.CopyAsync(first.Reference, generation, true);
        fixture.Clipboard.Writes.Should().BeEmpty();
        fixture.Controller.OpenCurrentResponse(first, null, () => admitted).Should().BeFalse();
    }
}
