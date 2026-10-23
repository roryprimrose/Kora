using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    private const string ChecklistSource = "- [ ] Deploy Alpha\r\n- [x] Verify Beta\r\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostAdmittedMarkdownAnswerUsesOnlyChecklistGateAndRetiresExactSourceAndFind(bool qualified)
    {
        using var f = new Fixture(pipeTablesAvailable: true, checklistsAvailable: qualified);
        var content = ChecklistContent();
        var admitted = true;
        f.Controller.OpenCurrentResponse(content, null, () => admitted).Should().BeTrue();
        var view = f.Views.Single();
        view.State.Status.Should().Contain(NativeMarkdownChecklistProfile.Name);
        view.State.RenderState.Should().Be(qualified ? DetailRenderState.Rendered : DetailRenderState.SourceFallback);
        view.State.ActiveText.Should().Contain(qualified ? "Unchecked: Deploy Alpha" : ChecklistSource);
        view.State.Content.Should().BeSameAs(content);
        view.State.Search("Alpha").Should().BeTrue();
        view.State.MatchCount.Should().Be(1);
        view.State.SetSource(true);
        view.State.ActiveText.Should().Be(ChecklistSource);
        view.State.MatchCount.Should().BeNull();
        view.State.Search("[ ]").Should().BeTrue();
        f.Controller.OpenCurrentResponse(content, null, () => admitted).Should().BeTrue();
        f.Views.Should().ContainSingle();
        var generation = view.State.Generation;
        await f.Controller.CopyAsync(content.Reference, generation, true);
        await f.Controller.CopyAsync(content.Reference, generation, true, 0, 6);
        f.Clipboard.Writes.Should().BeEmpty("current-response projection never grants copy authority");
        admitted = false;
        f.Controller.RetireUnavailableCurrentResponses();
        view.Cleared.Should().BeTrue();
        view.State.Content.Should().BeNull();
        view.State.ActiveText.Should().BeEmpty();
        view.State.SearchQuery.Should().BeEmpty();
        view.State.MatchCount.Should().BeNull();
        view.State.CompleteRender(generation, "late checklist", "Rendered", false).Should().BeFalse();
        f.Controller.OpenCurrentResponse(content, null, () => admitted).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChecklistRevocationCannotRevealOrRetireAnotherSessionAndPrivacyClosesBothProfiles(bool qualified)
    {
        using var f = new Fixture(checklistsAvailable: qualified);
        var first = ChecklistContent();
        var second = ChecklistContent(ChecklistSource.Replace("Alpha", "Other session", StringComparison.Ordinal));
        var admitted = true;
        f.Controller.OpenCurrentResponse(first, null, () => admitted).Should().BeTrue();
        f.Controller.OpenCurrentResponse(second, null, () => true).Should().BeTrue();
        f.Views[0].State.ActiveText.Should().NotContain("Other session");
        f.Views[1].State.ActiveText.Should().NotContain("Alpha");
        admitted = false;
        f.Controller.RetireUnavailableCurrentResponses();
        f.Views[0].Cleared.Should().BeTrue();
        f.Views[1].Cleared.Should().BeFalse();
        f.Views[1].State.SetSource(true);
        f.Views[1].State.ActiveText.Should().Be(second.Source);
        f.Accessible = false;
        f.Controller.ClearForPrivacy();
        f.Views[1].Cleared.Should().BeTrue();
        await f.Controller.CopyAsync(second.Reference, f.Views[1].State.Generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task PlainTextCurrentAndRetainedUserNeverBecomeMarkdownFromChecklistMarkup()
    {
        using var f = new Fixture(checklistsAvailable: true);
        var plain = ChecklistContent(kind: DetailContentKind.PlainText);
        f.Controller.OpenCurrentResponse(plain, null, () => true).Should().BeTrue();
        f.Views[0].State.Status.Should().Contain("native-text-v1 plain text");
        f.Views[0].State.ActiveText.Should().Be(ChecklistSource);
        var original = ConversationContent();
        await f.Controller.OpenConversationDetailAsync(original, null, () => true,
            _ => Task.FromResult(original), CancellationToken.None);
        f.Views[1].State.ActiveText.Should().Be(original.Source);
        f.Views[1].State.Status.Should().Contain("native-text-v1 plain text");
    }

    [Fact]
    public void EmbeddedAndHistoryMarkdownKeepTableProfileWithoutChecklistAuthority()
    {
        using var f = new Fixture(pipeTablesAvailable: true, checklistsAvailable: true);
        f.Documents.Pages[0] = f.Documents.GetStartPage() with { Markdown = TableSource };
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null)
            .Should().Contain(NativeMarkdownTableProfile.Name);
        var history = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.SessionHistory, DetailSensitivity.DisclosureConfirmationRequired,
            "History", "Exact receipt", ChecklistSource, historySession: new(Guid.NewGuid()));
        f.Controller.BindHistoryAccess(() => true);
        f.Controller.OpenHistoryDetail(history, null).Should().Contain("Unsupported Markdown extension/table");
        f.Views[1].State.ActiveText.Should().Be(ChecklistSource);
        f.Controller.OpenCurrentResponse(history, null, () => true).Should().BeFalse();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("create")]
    [InlineData("activate")]
    [InlineData("replace")]
    public void ChecklistPublicationRetainsExactAdmissionAndNativeFailureSemantics(string failure)
    {
        using var f = new Fixture(checklistsAvailable: true);
        var content = ChecklistContent();
        if (string.Equals(failure, "private", StringComparison.Ordinal)) { f.Accessible = false; }
        if (string.Equals(failure, "create", StringComparison.Ordinal)) { f.FailCreate = true; }
        if (string.Equals(failure, "activate", StringComparison.Ordinal)) { f.OnViewCreated = view => view.FailActivation = true; }
        if (string.Equals(failure, "replace", StringComparison.Ordinal))
        {
            f.Controller.OpenCurrentResponse(content, null, () => true).Should().BeTrue();
            var changed = new AdmittedDetailContent(content.Reference, content.Kind, content.Origin, content.Sensitivity,
                content.Title, content.Provenance, "different", content.SessionSource);
            f.Controller.OpenCurrentResponse(changed, null, () => true).Should().BeFalse();
            f.Views.Single().State.ActiveText.Should().Contain("Alpha");
            return;
        }
        f.Controller.OpenCurrentResponse(content, null, () => true).Should().BeFalse();
        f.Views.Should().OnlyContain(view => view.Cleared);
    }

    private static AdmittedDetailContent ChecklistContent(
        string source = ChecklistSource, DetailContentKind kind = DetailContentKind.Markdown)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        return new(new(new(Guid.NewGuid()), 1), kind, DetailContentOrigin.FinalizedResponse,
            DetailSensitivity.DisclosureConfirmationRequired, "Answer", "Host-issued test snapshot", source,
            new(request.SessionId, request.RequestId, request.TaskId));
    }
}
