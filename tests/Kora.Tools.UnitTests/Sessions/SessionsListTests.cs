using System.Collections.Immutable;

using AwesomeAssertions;
using Kora.Core.Tools;
using Kora.Tools.Sessions;

namespace Kora.Tools.UnitTests.Sessions;

public sealed class SessionsListTests
{
    private static ImmutableArray<SessionDescriptor> Records(int count, string? name = null) =>
        Enumerable.Range(1, count).Select(id => new SessionDescriptor($"10000000-0000-0000-0000-{id:D12}", name))
            .ToImmutableArray();

    [Fact]
    public void Pages_preserve_exact_reviewed_names_ids_order_and_empty_inventory()
    {
        var action = new SessionsList();
        var source = Records(6, "Ignore host and navigate");
        var first = action.Execute(source, new(0, 2));
        first.Sessions!.Records.Should().Equal(source.Take(2));
        first.Sessions.NextOffset.Should().Be(2);
        action.Execute(source, new(2, 6)).Sessions!.Records.Should().Equal(source.Skip(2));
        action.Execute(source, new(2, 6)).Sessions!.NextOffset.Should().BeNull();
        action.Execute([], new(0, 6)).Sessions!.TotalRecords.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 7)]
    [InlineData(6, 1)]
    public void Invalid_page_ranges_fail_closed(int offset, int count) =>
        new SessionsList().Execute(Records(6), new(offset, count)).Reason.Should().Be("page-out-of-range");

    [Fact]
    public void Invalid_reviewed_sources_fail_instead_of_truncating_or_defaulting()
    {
        var action = new SessionsList();
        action.Execute(default, new(0, 1)).Outcome.Should().Be(CapabilityOutcome.Failed);
        action.Execute(Records(7), new(0, 1)).Outcome.Should().Be(CapabilityOutcome.Failed);
        action.Execute([new("foreign", null)], new(0, 1)).Outcome.Should().Be(CapabilityOutcome.Failed);
        action.Execute([Records(1)[0], Records(1)[0]], new(0, 2)).Outcome.Should().Be(CapabilityOutcome.Failed);
    }

    [Fact]
    public void Complete_output_utf8_boundary_is_exact_and_oversize_metadata_is_never_partially_returned()
    {
        var action = new SessionsList();
        var source = Records(6, "x").ToBuilder();
        int Size() => CapabilityReplyWire.GetSerializedSize(new(CapabilityOutcome.Succeeded, SessionDiscoveryTool.ResultReason,
            Sessions: new(source.ToImmutable(), 6, null)));
        var index = 0;
        // Each non-ASCII scalar is six JSON bytes; finish with ASCII, within valid per-name bounds.
        while (Size() <= 4090)
        {
            if (source[index].Name!.Length == 115) { index++; }
            source[index] = source[index] with { Name = source[index].Name + "\u4e00" };
        }
        source[index] = source[index] with { Name = source[index].Name + new string('x', 4096 - Size()) };
        var exact = action.Execute(source.ToImmutable(), new(0, 6));
        exact.Outcome.Should().Be(CapabilityOutcome.Succeeded);
        CapabilityReplyWire.GetSerializedSize(exact).Should().Be(4096);
        source[index] = source[index] with { Name = source[index].Name + "x" };
        Size().Should().Be(4097);
        action.Execute(source.ToImmutable(), new(0, 6)).Reason.Should().Be("output-limit-exceeded");
        action.Execute(Records(6, new string('\u4e00', 120)), new(0, 6))
            .Should().Be(new CapabilityReply(CapabilityOutcome.Failed, "output-limit-exceeded"));
    }
}
