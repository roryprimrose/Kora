using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class NativeHandoffUiContractTests
{
    [Fact]
    public void NativeReviewHasNoDefaultApprovalOrActiveContentAndIsComposedFromTheActualWorkflow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "ModelHandoffWindow.axaml"));
        var controls = document.Descendants().ToArray();
        XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "Approve", "Decline", "Cancel", "Remove", "Inspect" })
        {
            var button = controls.Single(control => string.Equals(control.Attribute(names + "Name")?.Value, name, StringComparison.Ordinal));
            button.Attribute("IsEnabled")!.Value.Should().Be("False");
            button.Attribute("IsDefault").Should().BeNull();
            button.Attribute("IsCancel").Should().BeNull();
        }
        controls.Single(control => string.Equals(control.Attribute(names + "Name")?.Value, "Offers", StringComparison.Ordinal))
            .Attribute("SelectedIndex")!.Value.Should().Be("-1");
        controls.Should().NotContain(control => string.Equals(control.Name.LocalName, "TextBox", StringComparison.Ordinal)
            || string.Equals(control.Name.LocalName, "WebView", StringComparison.Ordinal)
            || string.Equals(control.Name.LocalName, "SelectableTextBlock", StringComparison.Ordinal));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "App.HandoffPresentation.cs"));
        composition.Should().Contain("GetRequiredService<ModelHandoffPresentation>");
        File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs")).Should()
            .Contain("AddSingleton<ModelHandoffPresentation>").And.Contain("AddSingleton<ModelProviderHandoffWorkflow>");
        XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants()
            .Any(control => string.Equals(control.Attribute("Click")?.Value, "OnReviewHandoffClicked", StringComparison.Ordinal)).Should().BeTrue();
    }
}
