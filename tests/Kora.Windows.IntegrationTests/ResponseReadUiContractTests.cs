using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ResponseReadUiContractTests
{
    [Fact]
    public void NativeReadProposalAndStopUseSharedCommandsAndRetireClosedSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var response = XDocument.Load(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml"));
        response.Descendants().Select(node => node.Attribute("Command")?.Value)
            .Should().Contain("{Binding StopSpeechCommand}", "owned playback must retain a native stop route");
        var proposal = response.Descendants().Single(node =>
            string.Equals(node.Attribute("IsVisible")?.Value, "{Binding HasResponseReadProposal}", StringComparison.Ordinal));
        proposal.Descendants().Select(node => node.Attribute("Command")?.Value).Should().Contain(
            ["{Binding PlanCurrentResponseReadCommand}", "{Binding ConfirmCurrentResponseReadCommand}", "{Binding CancelCurrentResponseReadCommand}"]);
        var code = File.ReadAllText(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml.cs"));
        code.Should().Contain("viewModel.RetireCurrentResponseSpeech();");
        var questions = File.ReadAllText(Path.Combine(root, "src", "Kora", "QuestionWindowController.cs"));
        questions.Should().Contain("main.BindResponseReadDecisionGate(() => !disposed && !IsOwnerInUse)");
    }
}
