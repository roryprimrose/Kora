using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    [Fact]
    public async Task AuthorizedRenderedMarkdownCopiesFormattingAndUnchangedCompleteSourceInOneWrite()
    {
        const string markdown = "# Heading\n\n**Strong** and *emphasis* with `code & text`.\n\n"
            + "> Quote\n\n3. Third\n4. Fourth\n\n- Bullet\n\n---\n\n"
            + "```text\n<script>inert & exact</script>\n```\n";
        using var f = new Fixture();
        f.Documents.Pages[0] = f.Documents.GetStartPage() with { Markdown = markdown };
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null);
        var state = f.Views.Single().State;
        state.Search("Heading").Should().BeTrue();
        await f.Controller.CopyAsync(state.Reference, state.Generation, false);
        f.Clipboard.Writes.Should().ContainSingle().Which.Should().Be(markdown);
        var html = f.Clipboard.HtmlWrites.Should().ContainSingle().Which;
        html.Should().Contain("<h1>Heading</h1>")
            .And.Contain("<strong>Strong</strong>").And.Contain("<em>emphasis</em>")
            .And.Contain("<code>code &amp; text</code>")
            .And.Contain("<blockquote>").And.Contain("<ol start=\"3\">")
            .And.Contain("<ul>").And.Contain("<hr>")
            .And.Contain("<pre><code>&lt;script&gt;inert &amp; exact&lt;/script&gt;");
        html.Should().NotContain("<script>").And.NotContain("SHA-256").And.NotContain("Origin:")
            .And.NotContain("match").And.NotContain("Immutable embedded");
        state.Status.Should().Contain("sanitized HTML");
        state.MatchStart.Should().Be(0);
    }

    [Fact]
    public async Task RenderedSelectionCopiesOnlyExactSelectedTextWithoutReinterpretingItAsMarkup()
    {
        using var f = new Fixture();
        f.Documents.Pages[0] = f.Documents.GetStartPage() with { Markdown = "# Heading\n\n`<img src=x>` tail" };
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null);
        var state = f.Views.Single().State;
        var start = state.ActiveText.IndexOf("<img", StringComparison.Ordinal);
        await f.Controller.CopyAsync(state.Reference, state.Generation, false, start, 11);
        f.Clipboard.Writes.Should().ContainSingle().Which.Should().Be("<img src=x>");
        f.Clipboard.HtmlWrites.Should().ContainSingle().Which.Should().Be("<pre>&lt;img src=x&gt;</pre>");
        await f.Controller.CopyAsync(state.Reference, state.Generation, false, -1, 3);
        f.Clipboard.Writes.Should().ContainSingle();
        state.Status.Should().Contain("blocked");
    }

    [Theory]
    [InlineData("# Heading\n\n**Strong**", true)]
    [InlineData("<script>not executable</script>", false)]
    [InlineData("![Alternative](https://example.test/asset)", false)]
    [InlineData("```mermaid\ngraph TD; A-->B\n```", false)]
    public async Task SourceAndUnsupportedMarkdownKeepExactPlainTextWithoutRichFormats(string markdown, bool sourceView)
    {
        using var f = new Fixture();
        f.Documents.Pages[0] = f.Documents.GetStartPage() with { Markdown = markdown };
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null);
        var state = f.Views.Single().State;
        state.SetSource(sourceView);
        await f.Controller.CopyAsync(state.Reference, state.Generation, true);
        f.Clipboard.Writes.Should().ContainSingle().Which.Should().Be(markdown);
        f.Clipboard.HtmlWrites.Should().ContainSingle().Which.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TableClipboardHonorsExistingProfileAvailabilityAndCompleteCellOrder(bool available)
    {
        using var f = new Fixture(pipeTablesAvailable: available);
        f.Documents.Pages[0] = f.Documents.GetStartPage() with { Markdown = TableSource };
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null);
        var state = f.Views.Single().State;
        await f.Controller.CopyAsync(state.Reference, state.Generation, false);
        f.Clipboard.Writes.Should().ContainSingle().Which.Should().Be(TableSource);
        if (available)
        {
            f.Clipboard.HtmlWrites.Should().ContainSingle().Which.Should().Be(
                "<table><tr><th><p>Name</p></th><th><p>State</p></th></tr>"
                + "<tr><td><p>Alpha</p></td><td><p>Ready</p></td></tr></table>");
        }
        else { f.Clipboard.HtmlWrites.Should().ContainSingle().Which.Should().BeNull(); }
    }

    [Theory]
    [InlineData("https://example.test/path?q=1&x=2")]
    [InlineData("javascript:alert%281%29")]
    [InlineData("file:///C:/private/secret")]
    [InlineData("//example.test/beacon")]
    public async Task LinkDestinationsRemainVisibleInertEncodedTextAndNeverBecomeResourceAttributes(string destination)
    {
        using var f = new Fixture();
        var markdown = "[`<label>`](" + destination + ")";
        f.Documents.Pages[0] = f.Documents.GetStartPage() with { Markdown = markdown };
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null);
        var state = f.Views.Single().State;
        await f.Controller.CopyAsync(state.Reference, state.Generation, false);
        f.Clipboard.Writes.Should().ContainSingle().Which.Should().Be(markdown);
        f.Clipboard.HtmlWrites.Should().ContainSingle().Which.Should().Be(
            "<p><code>&lt;label&gt;</code> (destination: " + System.Net.WebUtility.HtmlEncode(destination) + ")</p>");
    }

    [Fact]
    public async Task PrivateMarkdownRequiresExistingConfirmationAndRevalidatesHistoryAdmission()
    {
        using var f = new Fixture();
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.SessionHistory, DetailSensitivity.DisclosureConfirmationRequired,
            "Private", "Original history", "# Private\n\nexact source",
            historySession: new(Guid.NewGuid()));
        var admitted = true;
        f.Controller.BindHistoryAccess(() => admitted);
        f.Controller.OpenHistoryDetail(content, null);
        var state = f.Views.Single().State;
        await f.Controller.CopyAsync(state.Reference, state.Generation, false);
        f.Clipboard.Writes.Should().BeEmpty();
        state.Status.Should().Contain("Confirm disclosure");
        await f.Controller.CopyAsync(state.Reference, state.Generation, true);
        f.Clipboard.Writes.Should().ContainSingle().Which.Should().Be(content.Source);
        f.Clipboard.HtmlWrites.Should().ContainSingle().Which.Should().Be("<h1>Private</h1><p>exact source</p>");
        admitted = false;
        await f.Controller.CopyAsync(state.Reference, state.Generation, true);
        f.Clipboard.Writes.Should().ContainSingle();
        f.Clipboard.HtmlWrites.Should().ContainSingle();
        state.Status.Should().Contain("blocked");
    }

    [Fact]
    public async Task RichProjectionNeverAuthorizesCurrentResponseCopyAndFailedWriteRetainsSelection()
    {
        using var f = new Fixture(checklistsAvailable: true);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
            "Final", "Original response", "# Final",
            new(request.SessionId, request.RequestId, request.TaskId));
        f.Controller.OpenCurrentResponse(content, null, static () => true).Should().BeTrue();
        var denied = f.Views.Single().State;
        await f.Controller.CopyAsync(denied.Reference, denied.Generation, true);
        await f.Controller.CopyAsync(denied.Reference, denied.Generation, true, 0, 3);
        f.Clipboard.Writes.Should().BeEmpty();
        f.Clipboard.HtmlWrites.Should().BeEmpty();
        denied.Status.Should().Contain("no clipboard authority");
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null);
        var state = f.Views[1].State;
        state.Search("Guide").Should().BeTrue();
        await f.Controller.CopyAsync(state.Reference, state.Generation, false);
        var previousHtml = f.Clipboard.HtmlWrites.Single();
        f.Clipboard.Fail = true;
        await f.Controller.CopyAsync(state.Reference, state.Generation, false);
        state.Status.Should().Contain("failed").And.NotContain("copied");
        state.MatchStart.Should().Be(0);
        f.Clipboard.Writes.Should().ContainSingle();
        f.Clipboard.HtmlWrites.Should().ContainSingle().Which.Should().Be(previousHtml);
        f.Controller.ClearForPrivacy();
        await f.Controller.CopyAsync(state.Reference, state.Generation, true);
        f.Clipboard.Writes.Should().ContainSingle();
        state.Content.Should().BeNull();
    }
}
