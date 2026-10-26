using AwesomeAssertions;

using Kora.Core.Customizations;
using Kora.Core.Tools;
using Kora.Tools.Customizations;

namespace Kora.Tools.UnitTests.Customizations;

public sealed class CustomizationDiscoveryActionTests
{
    private static CustomizationMetadata Record(int index = 1) => new($"1000000000000000000000000000000{index}",
        CustomizationKind.Skill, "Name", "Description", CustomizationScope.User, true, false, CapabilityAvailability.NotObserved);

    [Fact]
    public void ListAndGetReturnOnlyFrozenSelectedMetadataAndRejectForeignReferences()
    {
        var records = new[] { Record(), Record(2) };
        var page = new CustomizationsList().Execute(records, new(0, 1));
        page.Customizations!.Records.Should().Equal(records[0]);
        page.Customizations.TotalRecords.Should().Be(2);
        page.Customizations.NextOffset.Should().Be(1);
        var last = new CustomizationsList().Execute(records, new(1, 6));
        last.Customizations!.Records.Should().Equal(records[1]);
        last.Customizations.NextOffset.Should().BeNull();
        new CustomizationsGet().Execute(records, new(records[1].Ref)).Customizations!.Records.Should().Equal(records[1]);
        new CustomizationsGet().Execute(records, new("foreign")).Outcome.Should().Be(CapabilityOutcome.Denied);
        var large = Enumerable.Range(1, 6).Select(i => Record(i) with { Description = new string('x', 4096) }).ToArray();
        new CustomizationsList().Execute(large, new(0, 6)).Reason.Should().Be("output-limit-exceeded");
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(7, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 0, 7)]
    [InlineData(1, 1, 1)]
    public void InvalidPagesNeverReturnSuccessfulEmptyOrWidenedResults(int total, int offset, int count) =>
        new CustomizationsList().Execute(Enumerable.Range(1, total).Select(i => Record(i)).ToArray(),
            new(offset, count)).Outcome.Should().Be(CapabilityOutcome.Denied);
}
