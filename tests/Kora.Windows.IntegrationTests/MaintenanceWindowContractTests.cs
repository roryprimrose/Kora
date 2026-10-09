using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class MaintenanceWindowContractTests
{
    [Fact]
    public void NotifyOnlyShellExposesScreenReaderNamesAndPoliteLiveStatusRegions()
    {
        var document = XDocument.Parse(Read("MaintenanceWindow.axaml"));
        foreach (var binding in new[]
        {
            "CurrentVersion", "Disclosure", "Channel", "NetworkEnabled", "CheckCommand", "Status",
            "VerificationStatus", "NextCheck", "ReleaseDetails", "ReleasePage", "ReviewCommand",
            "OpenCommand", "SnoozeCommand",
        })
        {
            var control = document.Descendants().Single(element => element.Attributes().Any(attribute =>
                (attribute.Name.LocalName is "Text" or "SelectedItem" or "IsChecked" or "Command")
                && attribute.Value.Contains("{Binding " + binding, StringComparison.Ordinal)));
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        foreach (var liveBinding in new[] { "Status", "VerificationStatus" })
        {
            var control = document.Descendants().Single(element =>
                string.Equals(element.Attribute("Text")?.Value, "{Binding " + liveBinding + "}", StringComparison.Ordinal));
            control.Attribute("AutomationProperties.LiveSetting")!.Value.Should().Be("Polite");
        }
        Read("MaintenanceWindow.axaml").Should().Contain("Permit public metadata checks for this run")
            .And.Contain("Permission and snooze are not saved across restart")
            .And.Contain("No unsolicited window, focus, audio, microphone, model call or install approval");
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
