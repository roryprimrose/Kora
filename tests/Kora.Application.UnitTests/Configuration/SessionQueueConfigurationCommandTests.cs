using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SessionQueueConfigurationCommandTests
{
    [Theory]
    [InlineData("list queue settings", AppearanceCommandOperation.List, null, null)]
    [InlineData("Nova, list queue settings", AppearanceCommandOperation.List, null, null)]
    [InlineData("Nova get queue.pending-per-session", AppearanceCommandOperation.Get, SessionQueueOption.PendingPerSession, null)]
    [InlineData("status queue.pending-per-session", AppearanceCommandOperation.Get, SessionQueueOption.PendingPerSession, null)]
    [InlineData("get queue.execution-slots", AppearanceCommandOperation.Get, SessionQueueOption.ExecutionSlots, null)]
    [InlineData("status queue.execution-slots", AppearanceCommandOperation.Get, SessionQueueOption.ExecutionSlots, null)]
    [InlineData("set queue.pending-per-session to 10", AppearanceCommandOperation.Set, SessionQueueOption.PendingPerSession, "10")]
    [InlineData("Nova, set queue.execution-slots to 2", AppearanceCommandOperation.Set, SessionQueueOption.ExecutionSlots, "2")]
    [InlineData("reset queue.pending-per-session", AppearanceCommandOperation.Reset, SessionQueueOption.PendingPerSession, null)]
    [InlineData("RESET QUEUE.EXECUTION-SLOTS", AppearanceCommandOperation.Reset, SessionQueueOption.ExecutionSlots, null)]
    public void Exact_current_name_commands_remain_original_bounded_values(string text, AppearanceCommandOperation operation,
        SessionQueueOption? option, string? value)
    {
        SessionQueueConfigurationCommand.Parse(text, "Nova").Should().Be(new SessionQueueConfigurationCommand(operation, option, value));
        SessionQueueConfigurationCommand.FixedPhrases.Should().Contain("list queue settings");
    }

    [Theory]
    [InlineData("set queue.execution-slots to ")]
    [InlineData("list queue settings extra")]
    [InlineData("set queue.pending-lifetime-minutes to 120")]
    [InlineData("reset queue.settings")]
    [InlineData("status queue.unknown")]
    [InlineData("get queue.execution-slots\n")]
    [InlineData("set queue.execution-slots to \t2")]
    public void Ambiguous_future_or_control_bearing_queue_configuration_clarifies(string text)
    {
        SessionQueueConfigurationCommand.Parse(text, "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Theory]
    [InlineData("Kora, list queue settings")]
    [InlineData("queue list 00000000-0000-0000-0000-000000000001")]
    [InlineData("NovaX get queue.execution-slots")]
    [InlineData("Nova")]
    [InlineData("tell me about the queue")]
    public void Non_settings_and_old_names_do_not_select_configuration(string text) =>
        SessionQueueConfigurationCommand.Parse(text, "Nova").Should().BeNull();

    [Fact]
    public void Original_UTF8_boundary_is_not_trimmed_away()
    {
        var prefix = "set queue.pending-per-session to ";
        SessionQueueConfigurationCommand.Parse(prefix + new string('x', 1024 - prefix.Length), "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Set);
        SessionQueueConfigurationCommand.Parse(prefix + new string('x', 1025 - prefix.Length), "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        SessionQueueConfigurationCommand.Parse(prefix + new string('\u00e9', 512), "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }
}
