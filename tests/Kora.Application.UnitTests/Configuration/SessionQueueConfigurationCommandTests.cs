using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SessionQueueConfigurationCommandTests
{
    [Theory]
    [InlineData("show fixed read queue settings", AppearanceCommandOperation.List, null, null)]
    [InlineData(" Nova, SHOW FIXED READ QUEUE SETTINGS ", AppearanceCommandOperation.List, null, null)]
    [InlineData("show fixed read pending capacity", AppearanceCommandOperation.Get, SessionQueueOption.PendingPerSession, null)]
    [InlineData("show fixed read execution slots", AppearanceCommandOperation.Get, SessionQueueOption.ExecutionSlots, null)]
    [InlineData("show fixed read pending lifetime", AppearanceCommandOperation.Get, SessionQueueOption.PendingLifetimeMinutes, null)]
    [InlineData("show fixed read active budget", AppearanceCommandOperation.Get, SessionQueueOption.ActiveBudgetMinutes, null)]
    [InlineData("reset fixed read pending capacity", AppearanceCommandOperation.Reset, SessionQueueOption.PendingPerSession, null)]
    [InlineData("reset fixed read execution slots", AppearanceCommandOperation.Reset, SessionQueueOption.ExecutionSlots, null)]
    [InlineData("reset fixed read pending lifetime", AppearanceCommandOperation.Reset, SessionQueueOption.PendingLifetimeMinutes, null)]
    [InlineData("reset fixed read active budget", AppearanceCommandOperation.Reset, SessionQueueOption.ActiveBudgetMinutes, null)]
    [InlineData("Nova set fixed read pending capacity to 1", AppearanceCommandOperation.Set, SessionQueueOption.PendingPerSession, "1")]
    [InlineData("set fixed read pending capacity to 10", AppearanceCommandOperation.Set, SessionQueueOption.PendingPerSession, "10")]
    [InlineData("set fixed read execution slots to 1", AppearanceCommandOperation.Set, SessionQueueOption.ExecutionSlots, "1")]
    [InlineData("set fixed read execution slots to 2", AppearanceCommandOperation.Set, SessionQueueOption.ExecutionSlots, "2")]
    [InlineData("set fixed read pending lifetime to 1 minutes", AppearanceCommandOperation.Set, SessionQueueOption.PendingLifetimeMinutes, "1")]
    [InlineData("set fixed read pending lifetime to 120 minutes", AppearanceCommandOperation.Set, SessionQueueOption.PendingLifetimeMinutes, "120")]
    [InlineData("set fixed read active budget to 1 minutes", AppearanceCommandOperation.Set, SessionQueueOption.ActiveBudgetMinutes, "1")]
    [InlineData("set fixed read active budget to 60 MINUTES", AppearanceCommandOperation.Set, SessionQueueOption.ActiveBudgetMinutes, "60")]
    public void NaturalWholeUtterancesMapToTheExistingAction(string text, AppearanceCommandOperation operation,
        SessionQueueOption? option, string? value) =>
        SessionQueueConfigurationCommand.Parse(text, "Nova").Should().Be(new SessionQueueConfigurationCommand(operation, option, value));

    public static TheoryData<string> InvalidNaturalValues()
    {
        var data = new TheoryData<string>();
        foreach (var (phrase, unit, aboveMaximum) in new[] {
            ("pending capacity", "", "11"), ("execution slots", "", "3"),
            ("pending lifetime", " minutes", "121"), ("active budget", " minutes", "61") })
        {
            foreach (var value in new[] { "0", "-1", "+1", "01", "1.0", "1,0", "1e0", "one", "2147483648",
                "99999999999999999999999999", "\uff11", "\u0661", " 1", "1 2", "", aboveMaximum })
            {
                data.Add("set fixed read " + phrase + " to " + value + unit);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidNaturalValues))]
    public void NaturalValuesUseCanonicalDomainValidationWithoutClamping(string text)
    {
        var command = SessionQueueConfigurationCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        command.Error.Should().Be(SessionQueueConfigurationCommand.Syntax);
        command.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("show fixed read")]
    [InlineData("set fixed read")]
    [InlineData("reset fixed read")]
    [InlineData("show fixed read queue settings extra")]
    [InlineData("show fixed read execution slot")]
    [InlineData("set fixed read execution slots to")]
    [InlineData("set fixed read execution slots to 2 and dispatch")]
    [InlineData("set fixed read pending lifetime to minutes")]
    [InlineData("set fixed read pending lifetime to 1 minute")]
    [InlineData("set fixed read pending lifetime to 1")]
    [InlineData("set fixed read pending lifetime to 1 seconds")]
    [InlineData("set fixed read active budget to 1 minute")]
    [InlineData("set fixed read active budget to 60 minutes apply-now")]
    [InlineData("set fixed read active budget to 60 minutes.")]
    [InlineData("reset fixed read queue settings")]
    [InlineData("reset fixed read active budget and pending lifetime")]
    [InlineData("set fixed read\tpending capacity to 2")]
    [InlineData("show fixed read queue settings\n")]
    [InlineData("Nova,\tshow fixed read queue settings")]
    public void ReservedMalformedNaturalInputClarifiesBeforeAnyOtherRoute(string text) =>
        SessionQueueConfigurationCommand.Parse(text, "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Theory]
    [InlineData("Kora, show fixed read queue settings")]
    [InlineData("NovaX show fixed read queue settings")]
    [InlineData("please show fixed read queue settings")]
    [InlineData("show fixed reader queue settings")]
    [InlineData("run two tasks")]
    [InlineData("pause all")]
    [InlineData("set queue capacity to 2")]
    [InlineData("extend current task budget to 60 minutes")]
    [InlineData("show fixed readqueue settings")]
    [InlineData("set fixed reading slots to 2")]
    [InlineData("reset fixed reader settings")]
    public void UnrelatedGenericAndFuzzyPhrasesDoNotAcquireQueueAuthority(string text) =>
        SessionQueueConfigurationCommand.Parse(text, "Nova").Should().BeNull();

    [Fact]
    public void NaturalDiscoveryRoundTripsEveryAdmittedValueAndOriginalByteLimit()
    {
        var phrases = SessionQueueConfigurationCommand.FixedPhrases;
        phrases.Should().OnlyHaveUniqueItems();
        phrases.Should().Contain("show fixed read queue settings");
        foreach (var phrase in phrases)
        {
            var command = SessionQueueConfigurationCommand.Parse("Nova, " + phrase, "Nova")!;
            command.Operation.Should().NotBe(AppearanceCommandOperation.Clarify, phrase);
            if (command.Operation == AppearanceCommandOperation.Set)
            {
                new SessionQueuePreferences().With(command.Option!.Value, command.Value).IsDefault.Should().BeFalse();
            }
        }
        var input = "show fixed read queue settings";
        SessionQueueConfigurationCommand.Parse(input.PadRight(1024), "Nova")!.Operation.Should().Be(AppearanceCommandOperation.List);
        SessionQueueConfigurationCommand.Parse(input.PadRight(1025), "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        SessionQueueConfigurationCommand.Parse("show fixed read " + new string('\u00e9', 512), "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        SessionQueueConfigurationCommand.Syntax.Should().Contain("manual dispatch").And.Contain("existing pending metadata and admitted deadlines never change");
    }

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
    [InlineData("get queue.pending-lifetime-minutes", AppearanceCommandOperation.Get, SessionQueueOption.PendingLifetimeMinutes, null)]
    [InlineData("status queue.pending-lifetime-minutes", AppearanceCommandOperation.Get, SessionQueueOption.PendingLifetimeMinutes, null)]
    [InlineData("Nova, set queue.pending-lifetime-minutes to 1", AppearanceCommandOperation.Set, SessionQueueOption.PendingLifetimeMinutes, "1")]
    [InlineData("set queue.pending-lifetime-minutes to 30", AppearanceCommandOperation.Set, SessionQueueOption.PendingLifetimeMinutes, "30")]
    [InlineData("set queue.pending-lifetime-minutes to 120", AppearanceCommandOperation.Set, SessionQueueOption.PendingLifetimeMinutes, "120")]
    [InlineData("reset queue.pending-lifetime-minutes", AppearanceCommandOperation.Reset, SessionQueueOption.PendingLifetimeMinutes, null)]
    [InlineData("get queue.active-budget-minutes", AppearanceCommandOperation.Get, SessionQueueOption.ActiveBudgetMinutes, null)]
    [InlineData("status queue.active-budget-minutes", AppearanceCommandOperation.Get, SessionQueueOption.ActiveBudgetMinutes, null)]
    [InlineData("Nova, set queue.active-budget-minutes to 1", AppearanceCommandOperation.Set, SessionQueueOption.ActiveBudgetMinutes, "1")]
    [InlineData("set queue.active-budget-minutes to 5", AppearanceCommandOperation.Set, SessionQueueOption.ActiveBudgetMinutes, "5")]
    [InlineData("set queue.active-budget-minutes to 60", AppearanceCommandOperation.Set, SessionQueueOption.ActiveBudgetMinutes, "60")]
    [InlineData("reset queue.active-budget-minutes", AppearanceCommandOperation.Reset, SessionQueueOption.ActiveBudgetMinutes, null)]
    public void Exact_current_name_commands_remain_original_bounded_values(string text, AppearanceCommandOperation operation,
        SessionQueueOption? option, string? value)
    {
        SessionQueueConfigurationCommand.Parse(text, "Nova").Should().Be(new SessionQueueConfigurationCommand(operation, option, value));
        SessionQueueConfigurationCommand.FixedPhrases.Should().Contain("list queue settings");
    }

    [Theory]
    [InlineData("set queue.execution-slots to ")]
    [InlineData("list queue settings extra")]
    [InlineData("set queue.active-deadline-minutes to 120")]
    [InlineData("set queue.active-budget-minutes to ")]
    [InlineData("reset queue.active-budget-minutes apply-now")]
    [InlineData("set queue.pending-lifetime-minutes to ")]
    [InlineData("set queue.pending-lifetime-minutes to \t30")]
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
