using AwesomeAssertions;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mcp_original_typed_and_activated_voice_commands_preserve_channel_and_require_native_selected_target(bool voice)
    {
        var f = new Fixture();
        var calls = 0;
        f.ViewModel.BindMcpCommands((command, origin, token) =>
        {
            calls++;
            command.Body.Should().Be("find the reviewed reference");
            command.Stop.Should().BeFalse();
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            return Task.FromResult("Actual MCP task result.");
        });
        try
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Kora use selected mcp tool find the reviewed reference", 0.95f); }
            else { await f.RunAsync("use selected mcp tool find the reviewed reference"); }
            calls.Should().Be(1);
            f.ViewModel.ResponseBody.Should().Be("Actual MCP task result.");
        }
        finally { f.ViewModel.Dispose(); }
    }

    [Fact]
    public async Task Mcp_stop_is_routed_to_its_owner_and_missing_composition_and_invalid_tasks_are_explicit()
    {
        var f = new Fixture();
        var stops = 0;
        f.ViewModel.BindMcpCommands((command, _, _) =>
        {
            command.Stop.Should().BeTrue();
            stops++;
            return Task.FromResult("Cancellation requested; owned resources remain held.");
        });
        try
        {
            await f.RunAsync("stop mcp");
            stops.Should().Be(1);
            f.ViewModel.ResponseBody.Should().Contain("owned resources");
            await f.RunAsync("use selected mcp tool");
            stops.Should().Be(1);
            f.ViewModel.ResponseBody.Should().Contain("<task>");
            f.ViewModel.BindMcpCommands(null);
            await f.RunAsync("stop mcp");
            stops.Should().Be(1);
            f.ViewModel.ResponseBody.Should().Contain("foreground session");
        }
        finally { f.ViewModel.Dispose(); }
    }

    [Fact]
    public async Task Mcp_result_is_suppressed_when_private_host_eligibility_changes_while_awaiting_its_owner()
    {
        var f = new Fixture();
        f.ViewModel.BindMcpCommands((_, _, _) =>
        {
            f.Session.IsUnlocked = false;
            return Task.FromResult("private result must not be displayed");
        });
        try
        {
            await f.RunAsync("use selected mcp tool find reference");
            f.ViewModel.ResponseBody.Should().NotContain("private result must not be displayed");
        }
        finally { f.ViewModel.Dispose(); }
    }

    [Fact]
    public async Task Mcp_does_not_recover_a_disabled_assistant_prefix_by_running_the_command()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.BindMcpCommands((_, _, _) => { calls++; return Task.FromResult("should not run"); });
        try
        {
            await f.RunAsync("Kora, stop mcp");
            calls.Should().Be(0);
            f.ViewModel.ResponseBody.Should().Contain("unavailable");
        }
        finally { f.ViewModel.Dispose(); }
    }
}
