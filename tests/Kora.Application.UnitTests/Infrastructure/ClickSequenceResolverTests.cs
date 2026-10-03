using AwesomeAssertions;

using Kora.Application.Infrastructure;

namespace Kora.Application.UnitTests.Infrastructure;

public sealed class ClickSequenceResolverTests
{
    [Fact]
    public void First_click_waits_for_the_double_click_interval()
    {
        var resolver = new ClickSequenceResolver();

        var outcome = resolver.RegisterClick();

        outcome.Should().Be(ClickSequenceOutcome.Pending);
    }

    [Fact]
    public void Pending_click_resolves_to_a_single_click()
    {
        var resolver = new ClickSequenceResolver();
        resolver.RegisterClick();

        var outcome = resolver.ResolvePendingClick();

        outcome.Should().Be(ClickSequenceOutcome.SingleClick);
        resolver.ResolvePendingClick().Should().Be(ClickSequenceOutcome.None);
    }

    [Fact]
    public void Second_click_resolves_to_a_double_click_and_cancels_the_single_click()
    {
        var resolver = new ClickSequenceResolver();
        resolver.RegisterClick();

        var outcome = resolver.RegisterClick();

        outcome.Should().Be(ClickSequenceOutcome.DoubleClick);
        resolver.ResolvePendingClick().Should().Be(ClickSequenceOutcome.None);
    }

    [Fact]
    public void Cancel_discards_a_pending_click()
    {
        var resolver = new ClickSequenceResolver();
        resolver.RegisterClick();

        resolver.Cancel();

        resolver.ResolvePendingClick().Should().Be(ClickSequenceOutcome.None);
    }
}
