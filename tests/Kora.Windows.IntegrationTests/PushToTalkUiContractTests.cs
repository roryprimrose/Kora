using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class PushToTalkUiContractTests
{
    [Fact]
    public void Response_window_exposes_hold_to_talk_only_for_disabled_listening_mode()
    {
        var root = SourceRoot();
        var response = XDocument.Load(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml"));
        var button = response.Descendants().Single(element =>
            string.Equals(element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value,
                "PushToTalkButton", StringComparison.Ordinal));

        button.Attribute("IsVisible")!.Value.Should().Be("{Binding IsPushToTalkVisible}");
        button.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanPushToTalk}");
        button.Attribute("AutomationProperties.Name")!.Value
            .Should().Be("Push to talk; hold while speaking");

        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml.cs"));
        codeBehind.Should().Contain("OnPushToTalkPressed")
            .And.Contain("OnPushToTalkReleased")
            .And.Contain("OnPushToTalkCaptureLost")
            .And.Contain("EndPushToTalkCommand.Execute(null)");
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
    }
}
