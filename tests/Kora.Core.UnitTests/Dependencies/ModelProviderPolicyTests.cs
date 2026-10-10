using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelProviderPolicyTests
{
    private static ModelProviderPolicy Policy(ModelProviderMode mode) => new(new(Guid.NewGuid()), new(1), mode,
        ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly, ModelTurnChoice.Default, ModelProviderIdentity.Ollama)]
    [InlineData(ModelProviderMode.LocalFirst, ModelTurnChoice.Default, ModelProviderIdentity.Ollama)]
    [InlineData(ModelProviderMode.HostedPreferred, ModelTurnChoice.Default, ModelProviderIdentity.Copilot)]
    [InlineData(ModelProviderMode.HostedPreferred, ModelTurnChoice.Local, ModelProviderIdentity.Ollama)]
    [InlineData(ModelProviderMode.LocalFirst, ModelTurnChoice.Hosted, ModelProviderIdentity.Copilot)]
    public void Valid_modes_resolve_one_exact_provider(ModelProviderMode mode, ModelTurnChoice choice, ModelProviderIdentity provider)
    {
        var policy = Policy(mode);
        policy.IsValid.Should().BeTrue();
        policy.Select(choice)!.Provider.Should().Be(provider);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("revision")]
    [InlineData("mode")]
    [InlineData("unknown")]
    [InlineData("local-null")]
    [InlineData("local-provider")]
    [InlineData("local-model")]
    [InlineData("local-revision")]
    [InlineData("hosted-null")]
    [InlineData("hosted-provider")]
    [InlineData("hosted-model")]
    [InlineData("hosted-revision")]
    public void Malformed_policy_never_selects_a_provider(string failure)
    {
        var policy = Policy(ModelProviderMode.LocalFirst);
        policy = failure switch
        {
            "session" => policy with { Session = default },
            "revision" => policy with { Revision = default },
            "mode" => policy with { Mode = (ModelProviderMode)99 },
            "unknown" => policy with { Mode = ModelProviderMode.Unknown },
            "local-null" => policy with { Local = null! },
            "local-provider" => policy with { Local = policy.Local with { Provider = ModelProviderIdentity.Copilot } },
            "local-model" => policy with { Local = policy.Local with { Model = default } },
            "local-revision" => policy with { Local = policy.Local with { Revision = default } },
            "hosted-null" => policy with { Hosted = null! },
            "hosted-provider" => policy with { Hosted = policy.Hosted with { Provider = (ModelProviderIdentity)99 } },
            "hosted-model" => policy with { Hosted = policy.Hosted with { Model = default } },
            _ => policy with { Hosted = policy.Hosted with { Revision = default } },
        };
        policy.IsValid.Should().BeFalse();
        policy.Select(ModelTurnChoice.Default).Should().BeNull();
    }

    [Fact]
    public void Local_only_and_unknown_turn_choices_fail_closed()
    {
        Policy(ModelProviderMode.LocalOnly).Select(ModelTurnChoice.Hosted).Should().BeNull();
        Policy(ModelProviderMode.LocalFirst).Select((ModelTurnChoice)99).Should().BeNull();
    }
}
