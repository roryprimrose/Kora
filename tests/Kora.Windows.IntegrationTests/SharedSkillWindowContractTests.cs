using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SharedSkillWindowContractTests
{
    [Fact]
    public void Shared_native_review_has_explicit_read_consent_accessible_exact_source_and_no_active_content_routes()
    {
        var document = XDocument.Parse(Read("SharedSkillSourcesWindow.axaml"));
        foreach (var name in new[] { "SourceSelector", "SharedStatus", "SharedPackageSelector", "SharedIdentity", "SharedSource", "UninspectedEntries" })
        {
            var control = document.Descendants().Single(element => element.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, name, StringComparison.Ordinal)));
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        foreach (var source in document.Descendants().Where(element => element.Name.LocalName is "TextBox"))
        {
            source.Attribute("IsReadOnly")!.Value.Should().Be("True");
            source.Attribute("IsUndoEnabled")!.Value.Should().Be("False");
        }
        var code = Read("SharedSkillSourcesWindow.axaml.cs");
        code.Should().Contain("package.Text").And.Contain("package.RevisionDigest")
            .And.Contain("package.SourceQualifiedIdentity").And.Contain("package.UnavailableReasons")
            .And.Contain("ContextMenu = null").And.Contain("ContextFlyout = null")
            .And.NotContain("Markdown.Parse").And.NotContain("HttpClient").And.NotContain("Process.Start")
            .And.NotContain("Clipboard").And.NotContain("File.Write").And.NotContain("ArtifactDefinition");
        var controller = Read("SharedSkillSourcesWindowController.cs");
        controller.Should().Contain("PrivacyClosureRequested").And.Contain("lifetime?.Cancel()")
            .And.Contain("exception.GetType().Name").And.NotContain("exception.Message")
            .And.NotContain("exception.ToString()");
        controller.Should().Contain("ClearPrivateContent()");
        controller.Should().Contain("RunAsync(UnregisterAsync, supersede: true)")
            .And.Contain("previous.CancelAsync()").And.Contain("opened.ClearCatalogue()")
            .And.Contain("CurrentWindow(opened, token)").And.Contain("opened.SelectedSource != source")
            .And.Contain("opened.RegisteredSources").And.Contain("ConfirmWithdrawalAsync(source, token)")
            .And.Contain("discovery.UnregisterAsync(source, confirmedSources");
        Read("SharedSkillSourcesWindow.Withdrawal.cs").Should().Contain("source.DirectoryIdentity")
            .And.Contain("source.Id:N").And.Contain("JsonSerializer.Serialize(source.ProfileRelativeRoot)")
            .And.Contain("dialog.ShowDialog<bool>").And.NotContain("StorageProvider");
    }

    private static string Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        if (directory is null) { throw new InvalidOperationException("The source contract fixture is unavailable."); }
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "Kora", name));
    }
}
