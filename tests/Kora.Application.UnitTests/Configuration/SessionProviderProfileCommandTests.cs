using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SessionProviderProfileCommandTests
{
    private const string Session = "d7fd283b-5d96-4300-8c93-67a119d564c4";

    [Theory]
    [InlineData("Kora, ", "get", "profile", AppearanceCommandOperation.Get)]
    [InlineData("", "status", "service", AppearanceCommandOperation.Get)]
    [InlineData("Kora ", "reset", "model", AppearanceCommandOperation.Reset)]
    [InlineData("", "reset", "reasoning", AppearanceCommandOperation.Reset)]
    [InlineData("", "reset", "context", AppearanceCommandOperation.Reset)]
    [InlineData("", "reset", "profile", AppearanceCommandOperation.Reset)]
    [InlineData("", "reset", "service", AppearanceCommandOperation.Reset)]
    public void ExactAddressedFieldAndAllResetSyntaxIsSessionOnly(string prefix, string verb, string field, AppearanceCommandOperation operation)
    {
        var command = SessionProviderProfileCommand.Parse(prefix + verb + " providers.session." + field + " for " + Session, "Kora");
        command!.Operation.Should().Be(operation);
        command.Session.Should().Be(Guid.Parse(Session));
        command.Field.Should().Be(field is "profile" ? null : Enum.Parse<SessionProviderField>(field, ignoreCase: true));
        command.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("Unset", ProviderService.Unset)]
    [InlineData("Local", ProviderService.Local)]
    [InlineData("Copilot", ProviderService.Copilot)]
    [InlineData("Claude", ProviderService.Claude)]
    [InlineData("Codex", ProviderService.Codex)]
    public void ExactDesiredServiceDoesNotImplyAccountOrExecution(string text, ProviderService service)
    {
        var command = SessionProviderProfileCommand.Parse("set providers.session.service for " + Session + " to " + text, "Kora")!;
        command.Service.Should().Be(service);
        command.Model.Should().BeNull();
    }

    [Theory]
    [InlineData("model", SessionProviderField.Model)]
    [InlineData("reasoning", SessionProviderField.Reasoning)]
    [InlineData("context", SessionProviderField.Context)]
    [InlineData("profile", null)]
    public void SparseAndCompleteTupleSyntaxRequiresExactAdvertisedAccountRevision(string text, SessionProviderField? field)
    {
        var command = SessionProviderProfileCommand.Parse("set providers.session." + text + " for " + Session
            + " to exact-model managed-effort managed-window for account at 1", "Kora")!;
        command.Field.Should().Be(field);
        command.Model.Should().Be("exact-model");
        command.Reasoning.Should().Be("managed-effort");
        command.Context.Should().Be("managed-window");
        command.Account.Should().Be("account");
        command.CatalogueRevision.Should().Be(1);
    }

    [Fact]
    public void ConfirmationIsHostIssuedAndBroaderLanguageNeverRoutesToApp()
    {
        SessionProviderProfileCommand.Parse("confirm providers.session.profile " + Session, "Kora")!.Confirmation.Should().Be(Guid.Parse(Session));
        SessionProviderProfileCommand.Parse("Use Claude and summarize this", "Kora").Should().BeNull();
        SessionProviderProfileCommand.Parse("set providers.app.default-service to Claude", "Kora").Should().BeNull();
        SessionProviderProfileCommand.FixedPhrases.Should().Contain("get providers.session.profile");
        SessionProviderProfileCommand.Syntax.Should().Contain("never app fallback").And.Contain("Unavailable".ToLowerInvariant());
    }

    [Theory]
    [InlineData("get providers.session.profile")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4")]
    [InlineData("confirm providers.session.profile bad")]
    [InlineData("get providers.session.profile for bad")]
    [InlineData("get providers.session.profile at d7fd283b-5d96-4300-8c93-67a119d564c4")]
    [InlineData("get providers.session.other for d7fd283b-5d96-4300-8c93-67a119d564c4")]
    [InlineData("set providers.session.service for d7fd283b-5d96-4300-8c93-67a119d564c4 to Other")]
    [InlineData("set providers.session.service for d7fd283b-5d96-4300-8c93-67a119d564c4 to Claude extra")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to m r c for a at 01")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to m r c for a at 0")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to m r c for a at nope")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to m r c FOR a at 1")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to m r c for a AT 1")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to é r c for a at 1")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 into m r c for a at 1")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to m")]
    [InlineData("set providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4 to m r")]
    [InlineData("delete providers.session.profile for d7fd283b-5d96-4300-8c93-67a119d564c4")]
    [InlineData("get providers.session.profile for 00000000-0000-0000-0000-000000000000")]
    [InlineData("get providers.session.profile for D7FD283B-5D96-4300-8C93-67A119D564C4")]
    public void InvalidUnknownPartialOrUnaddressedSyntaxRequiresSelectionNeverAppFallback(string text) =>
        SessionProviderProfileCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Fact]
    public void OversizedAndControlInputsAreExplicitlyUnavailable()
    {
        SessionProviderProfileCommand.Parse("get providers.session.profile " + new string('x', 1024), "Kora")!.Error.Should().NotBeNull();
        SessionProviderProfileCommand.Parse("get providers.session.profile\n", "Kora")!.Error.Should().NotBeNull();
    }
}
