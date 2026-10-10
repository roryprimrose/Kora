using System.Xml.Linq;

using Avalonia.Input;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class DetailWindowContractTests
{
    [Fact]
    public void ActualResponseWebDetailsButtonCarriesItsImmutableItemNotLatestResponseAndIsFullyComposed()
    {
        var response = XDocument.Parse(Read("ResponseWindow.axaml"));
        var button = response.Descendants().Single(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)
            && string.Equals(element.Attribute("Content")?.Value, "Open exact web-result details", StringComparison.Ordinal));
        button.Attribute("Tag")!.Value.Should().Be("{Binding}");
        button.Attribute("Click")!.Value.Should().Be("OnWebResultDetailsClicked");
        button.Attribute("AutomationProperties.Name")!.Value.Should().Be("Open exact web-result details");
        button.Ancestors().Single(element => string.Equals(element.Name.LocalName, "ItemsControl", StringComparison.Ordinal))
            .Attribute("ItemsSource")!.Value.Should().Be("{Binding WebResultDetailActions}");
        Read("ResponseWindow.axaml.cs").Should().Contain("Tag: DetailContentReference reference")
            .And.Contain("OpenWebResult(reference, this)").And.Contain("ReportWebResultDetailStatus(status)");
        Read("DetailWindowController.cs").Should().Contain("BindWebResultSource(viewModel.ResolveWebResultDetails)")
            .And.Contain("WebResultDetailsChanged +=").And.NotContain("WebPageGet").And.NotContain("HttpClient")
            .And.NotContain("Navigate").And.NotContain("Process.Start");
        Read("App.axaml.cs").Should().Contain("detailWindow = new DetailWindowController");
    }
    [Fact]
    public void NativeShellHasOwnedNonTopmostChromeSeparateFromReaderAndAccessibleTabOrder()
    {
        var document = XDocument.Parse(Read("DetailWindow.axaml"));
        var window = document.Root!;
        window.Name.LocalName.Should().Be("Window");
        window.Attribute("Topmost")!.Value.Should().Be("False");
        window.Attribute("WindowStartupLocation")!.Value.Should().Be("CenterOwner");
        var reader = Named(document, "ContentReader");
        reader.Name.LocalName.Should().Be("TextBox");
        reader.Attribute("IsReadOnly")!.Value.Should().Be("True");
        reader.Attribute("Grid.Row")!.Value.Should().Be("2");
        reader.Attribute("ContextMenu")!.Value.Should().Be("{x:Null}");
        reader.Attribute("ContextFlyout")!.Value.Should().Be("{x:Null}");
        reader.Attribute("IsUndoEnabled")!.Value.Should().Be("False");
        foreach (var name in new[] { "DetailTitle", "ProvenanceLabel", "ReferenceLabel", "DigestLabel", "RendererStatus" })
        {
            Named(document, name).Ancestors().Should().NotContain(reader);
        }
        var expectedOrder = new[]
        {
            "SourceToggle", "ReadingTextToggle", "SearchInput", "PreviousMatch", "NextMatch", "DisclosureConfirmation",
            "CopySelection", "CopySource", "CloseDetail", "ContentReader",
        };
        for (var index = 0; index < expectedOrder.Length; index++)
        {
            var control = Named(document, expectedOrder[index]);
            control.Attribute("TabIndex")!.Value.Should().Be(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        Named(document, "RenderedViewport").Attribute("Grid.Row")!.Value.Should().Be("2");
        Named(document, "ReadingTextToggle").Attribute("Content")!.Value.Should().Contain("selection/search");
        // Native MaxLength would silently shorten the query; the host rejects it visibly.
        Named(document, "SearchInput").Attribute("MaxLength").Should().BeNull();
        Read("DetailWindow.axaml").Should().Contain("DynamicResource").And.Contain("Rich HTML copy is not supported")
            .And.Contain("Clipboard content leaves Kora").And.NotContain("Animation").And.NotContain("WebView");
    }

    [Fact]
    public void ShellOwnsKeyboardAndClipboardInterceptionAndClearsControlsOnClosure()
    {
        var source = Read("DetailWindow.axaml.cs");
        source.Should().Contain("RoutingStrategies.Tunnel").And.Contain("DetailKeyboardNavigation.Resolve")
            .And.Contain("CopyingToClipboard += BlockAutomaticCopy")
            .And.Contain("CuttingToClipboard += BlockAutomaticCopy")
            .And.Contain("PastingFromClipboard += BlockClipboardRead")
            .And.Contain("eventArgs.Handled = true").And.Contain("BlockContextMenu")
            .And.Contain("reader.SelectionStart = 0").And.Contain("reader.SelectionEnd = 0")
            .And.Contain("reader.Text = string.Empty").And.Contain("renderedDocument.Children.Clear()")
            .And.Contain("selectable: false").And.Contain("reader.SelectAll()")
            .And.Contain("state.Close()").And.Contain("Show(owner)")
            .And.NotContain("NotifyPresenceInteraction").And.NotContain("Cancel").And.NotContain("Clipboard.");
        Read("NativeDetailClipboard.cs").Should().Contain("SetTextAsync(source)")
            .And.NotContain("TryGet").And.NotContain("Html").And.NotContain("SetFile");
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.Control, "CopySelection")]
    [InlineData(Key.X, KeyModifiers.Control, "BlockCut")]
    [InlineData(Key.Insert, KeyModifiers.Control, "CopySelection")]
    [InlineData(Key.Delete, KeyModifiers.Shift, "BlockCut")]
    [InlineData(Key.A, KeyModifiers.Control, "SelectContent")]
    [InlineData(Key.V, KeyModifiers.Control, "BlockPaste")]
    [InlineData(Key.Insert, KeyModifiers.Shift, "BlockPaste")]
    [InlineData(Key.F, KeyModifiers.Control, "FocusSearch")]
    [InlineData(Key.U, KeyModifiers.Control, "ToggleSource")]
    [InlineData(Key.F3, KeyModifiers.None, "NextMatch")]
    [InlineData(Key.F3, KeyModifiers.Shift, "PreviousMatch")]
    [InlineData(Key.Escape, KeyModifiers.None, "Close")]
    [InlineData(Key.Tab, KeyModifiers.None, "None")]
    public void KeyboardActionsAreNativeDeterministicAndCannotBypassClipboardGate(
        Key key, KeyModifiers modifiers, string expected) =>
        DetailKeyboardNavigation.Resolve(key, modifiers).ToString().Should().Be(expected);

    [Fact]
    public void DocumentationOnlyOpensItsExactCurrentPageExplicitlyAndRendererIsSharedWithGrants()
    {
        Read("DocumentationWindow.axaml").Should().Contain("Open details (native)");
        Read("DocumentationWindow.axaml.cs").Should().Contain("OpenEmbeddedPage(currentPage, this)")
            .And.Contain("currentPage = page").And.Contain("MarkdownDocumentRenderer.Render(page.Markdown)");
        Read("GrantListWindow.axaml.cs").Should().Contain("MarkdownDocumentRenderer.Render(markdown)");
        Read("MarkdownDocumentRenderer.cs").Should().Contain("Renderer.Render(markdown)").And.NotContain("Markdown.Parse");
        Read("NativeDetailRenderer.cs").Should().Contain("MaximumNestingDepth = NativeDetailProfile.MaximumNesting")
            .And.NotContain("UseAdvancedExtensions").And.NotContain("ToHtml").And.NotContain("HttpClient");
        Read("DetailWindowController.cs").Should().Contain("PrivacyClosureRequested +=").And.Contain("ClearForPrivacy()")
            .And.Contain("TryGetCopySource").And.Contain("canAccess()").And.NotContain("NotifyPresenceInteraction");
        Read("App.axaml.cs").Should().Contain("detailWindow?.Dispose()");
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attributes()
            .Any(attribute => string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, name, StringComparison.Ordinal)));

    private static string Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        if (directory is null) { throw new InvalidOperationException("The repository source root is unavailable."); }
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "Kora", name));
    }
}
