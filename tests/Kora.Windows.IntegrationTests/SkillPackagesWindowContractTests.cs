using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SkillPackagesWindowContractTests
{
    [Fact]
    public void InspectionOnlyShellExposesScreenReaderNamesForStaticAndPerFileDynamicControls()
    {
        var document = XDocument.Parse(Read("SkillPackagesWindow.axaml"));
        foreach (var name in new[] { "PackageSelector", "PackageIdentity", "SourceTabs" })
        {
            var control = document.Descendants().Single(element => element.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, name, StringComparison.Ordinal)));
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        var codeBehind = Read("SkillPackagesWindow.axaml.cs");
        codeBehind.Should().Contain("AutomationProperties.SetName(identity, $\"{file.Name} identity, digest and byte count\")")
            .And.Contain("AutomationProperties.SetName(source, $\"{file.Name} exact immutable source\")")
            .And.Contain("IsReadOnly = true").And.Contain("IsUndoEnabled = false")
            .And.Contain("ContextMenu = null").And.Contain("ContextFlyout = null")
            .And.NotContain("HttpClient").And.NotContain("Clipboard.");
    }

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
