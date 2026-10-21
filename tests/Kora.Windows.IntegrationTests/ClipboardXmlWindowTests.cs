using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardXmlWindowTests
{
    [Fact]
    public async Task ExplicitAccessibleButtonChecksOnlyTheExactInertSourceAndLeavesFindUnchanged()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var snapshot = Snapshot("<?xml version=\"1.0\"?><private>text</private>");
            var calls = 0;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected XML failure"));
            window.Show(snapshot, UnexpectedMutation, UnexpectedMutation, () => true, validateXml: (source, current, token) =>
            {
                calls++;
                source.Should().BeSameAs(snapshot);
                current().Should().BeTrue();
                return ClipboardXmlPolicy.Validate(source, token);
            });
            var status = Status(window);
            var button = Button(window, "Validate this snapshot as XML");
            var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
            peer.GetName().Should().Be("Validate this snapshot as XML");
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Button);
            calls.Should().Be(0);
            status.Text.Should().BeNull();
            var find = window.GetVisualDescendants().OfType<TextBox>().Single();
            find.Text = "private";
            calls.Should().Be(0);
            await ((AsyncCommand)Button(window, "Find first").Command!).ExecuteAsync();
            var findStatus = window.GetVisualDescendants().OfType<NamedTextBlock>()
                .Single(control => AutomationProperties.GetName(control) is "Clipboard literal Find outcome and exact UTF-16 location");
            var previous = findStatus.Text;
            await ((AsyncCommand)button.Command!).ExecuteAsync();
            calls.Should().Be(1);
            status.Text.Should().StartWith("WellFormedXML").And.NotContain("private");
            findStatus.Text.Should().Be(previous);
            window.GetVisualDescendants().OfType<NamedTextBlock>()
                .Single(control => AutomationProperties.GetName(control) is "Clipboard snapshot plain text content").Text.Should().Be(snapshot.Text);
            window.ClearAndClose();
            status.Text.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("replace")]
    [InlineData("same-id")]
    [InlineData("revoke")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task RetiredControllerAndQueuedButtonsCannotRetargetOrPublish(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var original = Snapshot("<r/>");
            ClipboardSnapshot? current = original;
            var eligible = true;
            var calls = 0;
            var views = new List<ClipboardPreviewWindow>();
            using var controller = new ClipboardPreviewWindowController(() => eligible ? current : null,
                _ => UnexpectedMutation(), () => current = null, UnexpectedMutation,
                () => { var window = new ClipboardPreviewWindow(_ => { }); views.Add(window); return window; },
                _ => throw new InvalidOperationException("Unexpected controller failure"),
                validateXml: (source, admitted, token) =>
                {
                    calls++;
                    admitted().Should().BeTrue();
                    return ClipboardXmlPolicy.Validate(source, token);
                });
            controller.Refresh();
            var old = views[0];
            var status = Status(old);
            var queued = (AsyncCommand)Button(old, "Validate this snapshot as XML").Command!;
            await queued.ExecuteAsync();
            status.Text.Should().StartWith("WellFormedXML");
            switch (change)
            {
                case "privacy":
                case "owner":
                case "call": eligible = false; break;
                case "replace": current = Snapshot("<new/>"); break;
                case "same-id": current = new(original.SourceId, original.SnapshotId, original.Request, "<new/>", 1, original.CapturedAt); break;
                case "revoke": current = null; break;
                case "close": old.Close(); break;
                default: controller.Dispose(); break;
            }
            await queued.ExecuteAsync();
            status.Text.Should().BeNull();
            old.Content.Should().BeNull();
            calls.Should().Be(1);
            eligible = true;
            current = Snapshot("<new/>");
            controller.Refresh();
            await queued.ExecuteAsync();
            calls.Should().Be(1);
            current.Text.Should().Be("<new/>");
            if (views.Count > 1) { Status(views[^1]).Text.Should().BeNull(); }
        });
    }

    [Theory]
    [InlineData("during-parse")]
    [InlineData("during-publish")]
    [InlineData("reentrant-close")]
    public async Task ReentrantCallbacksRetireDerivedResults(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var eligible = true;
            var window = new ClipboardPreviewWindow(_ => { });
            window.Show(Snapshot("<r/>"), UnexpectedMutation, UnexpectedMutation, () => eligible,
                validateXml: (snapshot, current, token) =>
                {
                    if (change is "during-parse") { eligible = false; }
                    if (change is "reentrant-close")
                    {
                        window.ClearAndClose();
                        return new(snapshot, ClipboardXmlOutcome.Invalid, ClipboardXmlReason.MalformedOrProhibitedXml, 1, 1);
                    }
                    return ClipboardXmlPolicy.Validate(snapshot, token);
                });
            var status = Status(window);
            status.PropertyChanged += (_, args) =>
            {
                if (change is "during-publish" && args.Property == TextBlock.TextProperty && status.Text is not null) { eligible = false; }
            };
            await ((AsyncCommand)Button(window, "Validate this snapshot as XML").Command!).ExecuteAsync();
            status.Text.Should().BeNull();
            window.Content.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("<private>\n<x></private>", "Invalid XML")]
    [InlineData("<!DOCTYPE r [<!ENTITY a 'secret'>]><r>&a;</r>", "Invalid XML")]
    public async Task InvalidNativeOutcomeDisclosesNoSourceNamesOrExceptionText(string text, string expected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected XML failure"));
            window.Show(Snapshot(text), UnexpectedMutation, UnexpectedMutation, () => true,
                validateXml: (snapshot, _, token) => ClipboardXmlPolicy.Validate(snapshot, token));
            await ((AsyncCommand)Button(window, "Validate this snapshot as XML").Command!).ExecuteAsync();
            Status(window).Text.Should().StartWith(expected).And.Contain("Line").And.Contain("column")
                .And.NotContain("private").And.NotContain("secret").And.NotContain("DOCTYPE");
            window.ClearAndClose();
        });
    }

    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
    private static Button Button(Window window, string caption) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption));
    private static NamedTextBlock Status(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(control => AutomationProperties.GetName(control) is "XML well-formedness outcome for this exact clipboard snapshot");
    private static Task UnexpectedMutation() => throw new InvalidOperationException("XML validation cannot invoke clipboard mutations.");

    [Fact]
    public void MaintainedParserConfigurationDisablesEveryExternalInterpretation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kora.slnx"))) { root = root.Parent; }
        root.Should().NotBeNull();
        var policy = File.ReadAllText(Path.Combine(root!.FullName, "src", "Kora.Core", "Context", "ClipboardXmlPolicy.cs"));
        policy.Should().Contain("DtdProcessing = DtdProcessing.Prohibit")
            .And.Contain("XmlResolver = null")
            .And.Contain("ValidationType = ValidationType.None")
            .And.Contain("ConformanceLevel = ConformanceLevel.Document")
            .And.Contain("MaxCharactersInDocument = ClipboardSnapshot.MaximumUtf8Bytes")
            .And.Contain("MaxCharactersFromEntities = ClipboardSnapshot.MaximumUtf8Bytes")
            .And.Contain("using var text = new StringReader(snapshot.Text)")
            .And.Contain("using var reader = XmlReader.Create(text")
            .And.NotContain("exception.Message")
            .And.NotContain("XmlSchema").And.NotContain("XslCompiledTransform")
            .And.NotContain("HttpClient").And.NotContain("File.");
    }
}
