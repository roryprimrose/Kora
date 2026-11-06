using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Theory]
    [InlineData("unknown", 1)]
    [InlineData("ask_user", 1)]
    [InlineData("settings.get", 0)]
    public void Version_two_constraints_reject_unknown_question_or_uncallable_selections(string id, int calls)
    {
        var create = () => new ModelTurnConstraints(2, [id], calls, 1, 16000, 2000, 1000);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(ModelToolMode.LocalWebPage)]
    [InlineData(ModelToolMode.McpReadOnly)]
    [InlineData(ModelToolMode.HostedClarification)]
    [InlineData(ModelToolMode.ReadOnly)]
    public void Version_two_constraints_cannot_advertise_settings_in_another_mode(ModelToolMode mode)
    {
        var constraints = new ModelTurnConstraints(2, ["settings.get"], 1, 2, 16000, 2000, 1000);
        var substitute = () => constraints.GetTools(mode);
        substitute.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Explicit_empty_version_two_selection_preserves_the_tool_free_zero_call_contract()
    {
        var empty = new ModelTurnConstraints(2, [], 0, 1, 16000, 2000, 1000);
        empty.GetTools(ModelToolMode.None).Should().BeEmpty();
        empty.GetTools(ModelToolMode.ReadOnly).Should().BeEmpty();
        var calling = new ModelTurnConstraints(2, [], 1, 2, 16000, 2000, 1000);
        var widening = () => calling.GetTools(ModelToolMode.None);
        widening.Should().Throw<InvalidDataException>();
        var scoped = new ModelTurnConstraints(2, [SettingsDiscoveryTools.Get], 1, 2, 16000, 2000, 1000);
        scoped.GetTools(ModelToolMode.SettingsDiscovery).Single().Id.Should().Be(SettingsDiscoveryTools.Get);
    }
}
