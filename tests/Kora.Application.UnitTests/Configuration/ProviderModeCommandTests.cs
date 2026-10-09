using System.Text;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ProviderModeCommandTests
{
    [Theory]
    [InlineData("list provider settings", AppearanceCommandOperation.List, null)]
    [InlineData("get providers.default-mode", AppearanceCommandOperation.Get, null)]
    [InlineData("status providers.default-mode", AppearanceCommandOperation.Get, null)]
    [InlineData("reset providers.default-mode", AppearanceCommandOperation.Reset, null)]
    [InlineData("set providers.default-mode to LocalOnly", AppearanceCommandOperation.Set, ModelProviderMode.LocalOnly)]
    [InlineData("set providers.default-mode to localfirst", AppearanceCommandOperation.Set, ModelProviderMode.LocalFirst)]
    [InlineData("set providers.default-mode to HostedPreferred", AppearanceCommandOperation.Set, ModelProviderMode.HostedPreferred)]
    public void ExactCommandsShareTypedAndCurrentNameVoiceGrammar(string text, AppearanceCommandOperation operation, ModelProviderMode? mode)
    {
        foreach (var prefix in new[] { "", "Nova, ", "Nova " })
        {
            ProviderModeCommand.Parse(prefix + text, "Nova").Should().Be(new ProviderModeCommand(operation, mode));
        }
    }

    [Theory]
    [InlineData("set providers.default-mode to 1")]
    [InlineData("set providers.default-mode to Unknown")]
    [InlineData("set providers.default-mode to LocalOnly,LocalFirst")]
    [InlineData("set providers.default-mode to local only")]
    [InlineData("set providers.session-mode to LocalOnly")]
    [InlineData("get providers.default-mode extra")]
    [InlineData("reset providers.default-mode all")]
    [InlineData("set providers.default-mode to LocalOnly\n")]
    public void MalformedSettingsAreClarifiedNotInferred(string text) =>
        ProviderModeCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Theory]
    [InlineData("old get providers.default-mode")]
    [InlineData("Nova Scotia list provider settings")]
    [InlineData("ordinary request")]
    public void UnrelatedInputIsNotASetting(string text) => ProviderModeCommand.Parse(text, "Nova").Should().BeNull();

    [Fact]
    public void ExactInputAndCompleteResultByteLimitsAreEnforced()
    {
        var prefix = "set providers.default-mode to ";
        var exact = prefix + new string('a', SessionCommand.MaximumInputBytes - Encoding.UTF8.GetByteCount(prefix));
        ProviderModeCommand.Parse(exact, "Kora")!.Error.Should().Be(ProviderModeCommand.Syntax);
        ProviderModeCommand.Parse(exact + "a", "Kora")!.Error.Should().Contain("UTF-8");
        var result = new ProviderModeCommandResult("observed", null, 1, 2, null, ModelProviderMode.LocalOnly, "default", true);
        var json = ProviderModeCommandResult.Serialize(result);
        json.Should().Contain("\"default\":\"LocalOnly\"").And.Contain("\"scope\":\"device-local\"")
            .And.Contain("\"choices\":[\"LocalOnly\",\"LocalFirst\",\"HostedPreferred\"]");
        var bounded = result with { Recovery = new string('a', SessionCommand.MaximumResultBytes - Encoding.UTF8.GetByteCount(json) + 2) };
        Encoding.UTF8.GetByteCount(ProviderModeCommandResult.Serialize(bounded)).Should().Be(SessionCommand.MaximumResultBytes);
        FluentActions.Invoking(() => ProviderModeCommandResult.Serialize(bounded with { Recovery = bounded.Recovery + "a" })).Should().Throw<InvalidDataException>();
        foreach (var phrase in ProviderModeCommand.FixedPhrases)
        {
            ProviderModeCommand.Parse(phrase, "Kora")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
        }
    }
}
