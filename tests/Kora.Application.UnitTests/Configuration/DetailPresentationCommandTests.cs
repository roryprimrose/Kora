using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration;

public sealed class DetailPresentationCommandTests
{
    [Theory]
    [InlineData("get responses.detail-presentation", DetailPresentationOperation.Inspect, null)]
    [InlineData("status responses.detail-presentation", DetailPresentationOperation.Inspect, null)]
    [InlineData("set responses.detail-presentation to Offer", DetailPresentationOperation.Review, DetailPresentationMode.Offer)]
    [InlineData("Nova, SET RESPONSES.DETAIL-PRESENTATION TO LINKONLY", DetailPresentationOperation.Review, DetailPresentationMode.LinkOnly)]
    [InlineData("stop asking about details", DetailPresentationOperation.Review, DetailPresentationMode.LinkOnly)]
    [InlineData("offer detailed results", DetailPresentationOperation.Review, DetailPresentationMode.Offer)]
    [InlineData("reset responses.detail-presentation", DetailPresentationOperation.Reset, null)]
    [InlineData("confirm detail presentation change", DetailPresentationOperation.Confirm, null)]
    [InlineData("cancel detail presentation change", DetailPresentationOperation.Cancel, null)]
    public void ExactCommandsSelectOnlyBoundedPreferenceOperations(string text, DetailPresentationOperation operation, DetailPresentationMode? value)
    {
        DetailPresentationCommand.Parse(text, "Nova").Should().Be(new DetailPresentationCommand(operation, value));
    }

    [Theory]
    [InlineData("always open detailed results")]
    [InlineData("set responses.detail-presentation to OpenAutomatically")]
    [InlineData("set responses.detail-presentation to 0")]
    [InlineData("confirm detail presentation change and yes")]
    [InlineData("stop asking about details please")]
    [InlineData("stop asking about details\n")]
    [InlineData("offer detailed results\0")]
    public void ReservedInvalidInputCannotFallThroughToQuestionsOrModel(string text)
    {
        DetailPresentationCommand.Parse(text, "Kora")!.Operation.Should().Be(DetailPresentationOperation.Invalid);
    }

    [Theory]
    [InlineData("show detailed results")]
    [InlineData("open current response details")]
    [InlineData("not now")]
    [InlineData("yes")]
    [InlineData("explain the deployment")]
    public void ExistingNavigationAndOfferAnswersAreNotPreferenceWrites(string text)
    {
        DetailPresentationCommand.Parse(text, "Kora").Should().BeNull();
    }

    [Fact]
    public void OversizeReservedInputIsRejected()
    {
        DetailPresentationCommand.Parse("stop asking about details" + new string(' ', 1024), "Kora")!.Operation
            .Should().Be(DetailPresentationOperation.Invalid);
        DetailPresentationCommand.FixedPhrases.Should().OnlyContain(text =>
            DetailPresentationCommand.Parse(text, "Kora")!.Operation != DetailPresentationOperation.Invalid);
    }

    [Theory]
    [InlineData("Offer")]
    [InlineData("Offer detailed")]
    [InlineData("Offer detailed results")]
    public void NaturalPhraseIsNotMistakenForAnAssistantNamePrefix(string name)
    {
        DetailPresentationCommand.Parse("offer detailed results", name)!.Value.Should().Be(DetailPresentationMode.Offer);
    }
}
