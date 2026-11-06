using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelTokenUsageTests
{
    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(0L, 0L, 0L, 1L)]
    [InlineData(100L, 50L, 100L, 100L)]
    [InlineData(null, null, 10L, null)]
    [InlineData(null, null, null, 10L)]
    [InlineData(long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue)]
    public void ValidateAcceptsUnknownZeroAndExactProviderCounts(long? input, long? output, long? context, long? capacity)
    {
        new ModelTokenUsage(input, output, context, capacity).Validate();
    }

    [Theory]
    [InlineData(-1L, null, null, null)]
    [InlineData(null, -1L, null, null)]
    [InlineData(null, null, -1L, null)]
    [InlineData(null, null, null, 0L)]
    [InlineData(null, null, null, -1L)]
    [InlineData(null, null, 11L, 10L)]
    public void ValidateRejectsInvalidCountsAndCapacity(long? input, long? output, long? context, long? capacity)
    {
        Assert.Throws<InvalidDataException>(() => new ModelTokenUsage(input, output, context, capacity).Validate());
    }

    [Fact]
    public void LegacyReplyConstructionLeavesTokenMetadataUnknown()
    {
        new CopilotTurnReply(ModelTurnOutcome.Unknown).TokenUsage.Should().BeNull();
        new ClaudeTurnReply(ModelTurnOutcome.Unknown).TokenUsage.Should().BeNull();
        new ModelTokenUsage().Should().Be(new ModelTokenUsage(null, null, null, null));
    }
}
