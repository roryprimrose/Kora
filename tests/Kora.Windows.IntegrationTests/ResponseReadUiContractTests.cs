using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ResponseReadUiContractTests
{
    [Fact]
    public void NativeCurrentResponseAndStopUseSharedCommandsAndRetireClosedSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var response = XDocument.Load(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml"));
        var read = response.Descendants().Single(node =>
            string.Equals(node.Attribute("Command")?.Value, "{Binding ReadCurrentResponseCommand}", StringComparison.Ordinal));
        read.Attribute("AutomationProperties.Name")!.Value.Should().Be("Read this current ordinary response aloud once");
        response.Descendants().Select(node => node.Attribute("Command")?.Value)
            .Should().Contain("{Binding StopSpeechCommand}", "owned playback must retain a native stop route");
        var code = File.ReadAllText(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml.cs"));
        code.Should().Contain("viewModel.RetireCurrentResponseSpeech();");
        var questions = File.ReadAllText(Path.Combine(root, "src", "Kora", "QuestionWindowController.cs"));
        questions.Should().Contain("main.BindResponseReadDecisionGate(() => !disposed && !IsOwnerInUse)");
    }
}
