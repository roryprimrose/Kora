using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ProviderProfileCommandTests
{
    [Theory]
    [InlineData(ProviderProfileField.Model)]
    [InlineData(ProviderProfileField.Reasoning)]
    [InlineData(ProviderProfileField.Context)]
    public void ExactIndividualFieldInputBindsIdsWithoutInferredScope(ProviderProfileField field)
    {
        var option = "providers.app." + field.ToString().ToLowerInvariant();
        var text = "set " + option + " to Exact/V2 for Account at 42";
        var command = ProviderProfileCommand.Parse("Mira, " + text, "Mira")!;
        command.Field.Should().Be(field);
        command.FieldValue.Should().Be(new ProviderChoiceId("Exact/V2"));
        command.Account.Should().Be("Account");
        command.CatalogueRevision.Should().Be(42);
        command.Model.Should().BeNull();
        ProviderProfileCommand.Parse("get " + option, "Mira")!.Field.Should().Be(field);
        ProviderProfileCommand.Parse("status " + option, "Mira")!.Field.Should().Be(field);
        ProviderProfileCommand.Parse("Kora, " + text, "Mira")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Theory]
    [InlineData("confirm", false)]
    [InlineData("cancel", true)]
    public void ConfirmationAndCancellationAcceptOnlyCanonicalHostIds(string verb, bool cancel)
    {
        var id = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
        var command = ProviderProfileCommand.Parse($"{verb} providers.app.profile {id:D}", "Kora")!;
        command.Confirmation.Should().Be(id);
        command.Cancel.Should().Be(cancel);
        foreach (var value in new[] { id.ToString("D").ToUpperInvariant(), id.ToString("N"), Guid.Empty.ToString("D"), "foreign", id + " extra" })
        { ProviderProfileCommand.Parse($"{verb} providers.app.profile {value}", "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify); }
    }

    [Theory]
    [InlineData("reset providers.app.model")]
    [InlineData("set providers.app.model to model for account at 01")]
    [InlineData("set providers.app.model to model FOR account at 1")]
    [InlineData("set providers.app.model to model for account AT 1")]
    [InlineData("set providers.app.reasoning to é for account at 1")]
    [InlineData("set providers.app.context to exact for é at 1")]
    [InlineData("set providers.app.context to exact for account at -1")]
    [InlineData("set providers.app.context to exact for account at 9223372036854775808")]
    [InlineData("set providers.app.context to exact for account at 1 and summarize")]
    public void PartialUnknownAndMultiActionFieldSyntaxNeverWrites(string text)
    {
        ProviderProfileCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Theory]
    [InlineData("list app provider settings", AppearanceCommandOperation.List)]
    [InlineData("get providers.app.profile", AppearanceCommandOperation.Get)]
    [InlineData("status providers.app.profile", AppearanceCommandOperation.Get)]
    [InlineData("get providers.app.default-service", AppearanceCommandOperation.Get)]
    [InlineData("reset providers.app.default-service", AppearanceCommandOperation.Reset)]
    [InlineData("reset providers.app.profile", AppearanceCommandOperation.Reset)]
    [InlineData("sign in providers.app.service", AppearanceCommandOperation.Get)]
    public void ExplicitAppDiscoveryAndResetParseWithoutInferredSessionScope(string input, AppearanceCommandOperation operation)
    {
        var command = ProviderProfileCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(operation);
        ProviderProfileCommand.Parse("Kora, " + input, "Kora")!.Operation.Should().Be(operation);
        ProviderProfileCommand.Parse("Kora " + input, "Kora")!.Operation.Should().Be(operation);
        command.TupleOnly.Should().Be(input.StartsWith("reset providers.app.profile", StringComparison.Ordinal));
        command.SignIn.Should().Be(input.StartsWith("sign in", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProviderService.Unset)]
    [InlineData(ProviderService.Local)]
    [InlineData(ProviderService.Copilot)]
    [InlineData(ProviderService.Claude)]
    [InlineData(ProviderService.Codex)]
    public void ExplicitServiceChoicesAreTypedAndDoNotSelectModels(ProviderService service)
    {
        var command = ProviderProfileCommand.Parse("set providers.app.default-service to " + service, "Kora")!;
        command.Service.Should().Be(service);
        command.Model.Should().BeNull();
        command.Operation.Should().Be(AppearanceCommandOperation.Set);
    }

    [Fact]
    public void CompleteTuplePreservesExactIdsAndAccountCatalogueBinding()
    {
        var command = ProviderProfileCommand.Parse("set providers.app.profile to Model/V2 effort-id window-id for account-id at 42", "Kora")!;
        command.Model.Should().Be("Model/V2");
        command.Reasoning.Should().Be("effort-id");
        command.Context.Should().Be("window-id");
        command.Account.Should().Be("account-id");
        command.CatalogueRevision.Should().Be(42);
    }

    [Theory]
    [InlineData("Use Claude")]
    [InlineData("for this request only use Codex")]
    [InlineData("for this session use Copilot")]
    [InlineData("set providers.default-mode to LocalOnly")]
    [InlineData("hello")]
    [InlineData("Kora")]
    [InlineData("Korax get providers.default-mode")]
    public void UnscopedOrOtherDomainInputNeverBecomesAnAppSetting(string input)
    {
        ProviderProfileCommand.Parse(input, "Kora").Should().BeNull();
    }

    [Theory]
    [InlineData("set providers.app.default-service to 1")]
    [InlineData("set providers.app.default-service to Other")]
    [InlineData("set providers.app.profile to model effort window")]
    [InlineData("set providers.app.profile to model effort window FOR account at 1")]
    [InlineData("set providers.app.profile to model effort window for account AT 1")]
    [InlineData("set providers.app.profile to model effort window for account at nope")]
    [InlineData("set providers.app.profile to model effort window for account at 0")]
    [InlineData("set providers.app.profile to model effort window for account at 01")]
    [InlineData("set providers.app.profile to é effort window for account at 1")]
    [InlineData("get providers.app.other")]
    [InlineData("get providers.app.profile\n")]
    public void UnknownNoncanonicalOrIncompleteInputRequiresExplicitClarification(string input)
    {
        ProviderProfileCommand.Parse(input, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Fact]
    public void CompleteInputBoundIncludesActivationPrefixAndIds()
    {
        var command = ProviderProfileCommand.Parse("get providers.app.profile " + new string('x', 1024), "Kora")!;
        command.Error.Should().Contain("1024");
        ProviderProfileCommand.FixedPhrases.Should().Contain("set providers.app.default-service to Claude");
    }
}
