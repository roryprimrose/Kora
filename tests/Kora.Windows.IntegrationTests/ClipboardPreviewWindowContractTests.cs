using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ClipboardPreviewWindowContractTests
{
    [Fact]
    public void ImmutablePreviewExposesScreenReaderNamesAndRevokesWithoutHistoryOrEgress()
    {
        var source = Read("ClipboardPreviewWindow.cs");
        source.Should().Contain("using Avalonia.Automation;")
            .And.Contain("AutomationProperties.SetName(source, \"Clipboard snapshot plain text content\")")
            .And.Contain("AutomationProperties.SetName(identity, \"Clipboard snapshot identity and capture details\")")
            .And.Contain("AutomationProperties.SetName(disclosure, \"Clipboard snapshot privacy and scope disclosure\")")
            .And.Contain("AutomationProperties.SetName(reuseButton, \"Reuse this exact clipboard snapshot ID\")")
            .And.Contain("AutomationProperties.SetName(revokeButton, \"Revoke and clear this clipboard snapshot\")")
            .And.Contain("source.Text = null").And.Contain("Content = null").And.Contain("Close()")
            .And.Contain("No inference, egress approval, copy/write or history")
            .And.Contain("Clipboard changes do not update this snapshot. Close revokes it.")
            .And.NotContain("Clipboard.").And.NotContain("HttpClient");
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
