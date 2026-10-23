using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class StaticHtmlNativeTests
{
    [Fact]
    public async Task NativeStaticControlsKeepHeadingLabelsTextSourceAndClosureWithoutActiveControls()
    {
        await HeadlessSession.RunAsync(() =>
        {
            const string source = "<h1>First</h1><h2>Second</h2><h3>Third</h3><h4>Fourth</h4><h5>Fifth</h5><h6>Sixth</h6>"
                + "<ul><li>Item</li></ul><blockquote>Quote</blockquote><pre>x &lt; y\n\tz</pre>";
            var content = Content(source);
            var result = new StaticHtmlDetailRenderer(NullLogger<StaticHtmlDetailRenderer>.Instance, true)
                .Render(content, TestContext.Current.CancellationToken);
            var controls = StaticHtmlDocumentRenderer.CreateControls(result);
            controls.Should().HaveCount(9);
            controls.Should().OnlyContain(control => control is TextBlock);
            var headings = controls.Take(6).Cast<TextBlock>().ToArray();
            for (var i = 0; i < headings.Length; i++)
            {
                var name = ControlAutomationPeer.CreatePeerForElement(headings[i])!.GetName();
                name.Should().Contain($"Heading level {i + 1}").And.Contain(headings[i].Text!);
            }
            var state = new DetailViewerState(content);
            state.CompleteRender(state.Generation, result.SemanticText, result.Status, false).Should().BeTrue();
            var window = new DetailWindow(state, result, (_, _, _) => Task.CompletedTask);
            window.FindControl<StackPanel>("RenderedDocument")!.Children.Should().HaveCount(9);
            window.GetVisualDescendants().OfType<TextBox>().Should().OnlyContain(control =>
                control.ContextMenu == null && control.ContextFlyout == null);
            state.SetSource(true);
            window.FindControl<TextBox>("ContentReader")!.Text.Should().Be(source);
            state.SetSource(false);
            state.ActiveText.Should().Be(result.SemanticText);
            window.ClearAndClose();
            state.Content.Should().BeNull();
            window.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            window.FindControl<StackPanel>("RenderedDocument")!.Children.Should().BeEmpty();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SourceFallbackNativeControlsRemainExactAndForeignProjectionIsRejected()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var content = Content("<script>not trusted</script>\r\n");
            var unavailable = new StaticHtmlDetailRenderer(NullLogger<StaticHtmlDetailRenderer>.Instance)
                .Render(content, TestContext.Current.CancellationToken);
            var controls = StaticHtmlDocumentRenderer.CreateControls(unavailable).Cast<TextBlock>().ToArray();
            controls.Should().HaveCount(2);
            controls[0].Text.Should().Contain("qualification unavailable");
            controls[1].Text.Should().Be(content.Source);
            var rendered = new StaticHtmlDetailRenderer(NullLogger<StaticHtmlDetailRenderer>.Instance, true)
                .Render(Content("<p>exact</p>"), TestContext.Current.CancellationToken);
            var foreign = () => StaticHtmlDocumentRenderer.CreateControls(rendered with { Source = "<p>foreign</p>" });
            foreign.Should().Throw<InvalidDataException>();
            var missing = () => StaticHtmlDocumentRenderer.CreateControls(rendered with { StaticHtml = null });
            missing.Should().Throw<InvalidDataException>();
            return Task.CompletedTask;
        });
    }

    private static AdmittedDetailContent Content(string source) => new(new(new(Guid.NewGuid()), 1),
        DetailContentKind.StaticHtml, DetailContentOrigin.EmbeddedDocument, DetailSensitivity.DisclosureConfirmationRequired,
        "Static result", "Immutable host test fixture", source);
}
