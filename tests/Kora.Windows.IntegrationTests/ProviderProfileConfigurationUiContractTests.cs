using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ProviderProfileConfigurationUiContractTests
{
    [Fact]
    public void NativeAppProviderControlsRequireExplicitExactServiceAndCompleteTupleAndDisableSignIn()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        foreach (var (selected, source) in new[]
        {
            ("SelectedProviderServiceChoice", "ProviderServiceChoices"),
            ("SelectedProviderProfileModel", "ProviderProfileModels"),
            ("SelectedProviderReasoning", "ProviderReasoningChoices"),
            ("SelectedProviderContext", "ProviderContextChoices"),
        })
        {
            var selector = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding " + selected + "}", StringComparison.Ordinal));
            selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding " + source + "}");
            selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeProviderProfile}");
        }
        foreach (var (command, enabled) in new[]
        {
            ("RefreshProviderProfileCommand", "CanChangeProviderProfile"),
            ("SaveProviderServiceCommand", "CanSaveProviderService"),
            ("SaveProviderProfileCommand", "CanSaveProviderProfile"),
            ("ResetProviderServiceCommand", "CanChangeProviderProfile"),
            ("ResetProviderProfileCommand", "CanChangeProviderProfile"),
            ("ProviderSignInCommand", "CanSignInProvider"),
        })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding " + enabled + "}");
        }
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value, "{Binding ProviderProfileConfigurationStatus}", StringComparison.Ordinal)).Should().BeTrue();
        File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs")).Should().Contain("ProviderProfileComposition.Configure(services);");
    }
}
