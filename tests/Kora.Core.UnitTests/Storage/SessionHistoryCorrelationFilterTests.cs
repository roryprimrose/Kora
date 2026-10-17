using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionHistoryCorrelationFilterTests
{
    private static readonly Guid Request = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Task = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Source = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Theory]
    [InlineData("bad")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}")]
    [InlineData("(aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa)")]
    [InlineData(" aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa")]
    public void EveryInputRejectsRepairAndAlternateFormats(string invalid)
    {
        foreach (var inputs in new[] { new[] { invalid, "", "" }, ["", invalid, ""], ["", "", invalid] })
        {
            var parse = () => SessionHistoryCorrelationFilter.Parse(inputs[0], inputs[1], inputs[2]);
            parse.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void NullInputAndEmptyTypedValuesFailClosed()
    {
        foreach (var inputs in new[] { new[] { null!, "", "" }, ["", null!, ""], ["", "", null!] })
        {
            var parse = () => SessionHistoryCorrelationFilter.Parse(inputs[0], inputs[1], inputs[2]);
            parse.Should().Throw<ArgumentNullException>();
        }
        foreach (var filter in new[] { new SessionHistoryCorrelationFilter(Guid.Empty),
            new(TaskId: Guid.Empty), new(SourceId: Guid.Empty) })
        {
            var search = () => new SessionHistorySearch("needle", correlation: filter);
            search.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void EveryCombinationMatchesOnlyRetainedPresentFieldsAndBindsCompleteIdentity()
    {
        var record = Record();
        var token = TestContext.Current.CancellationToken;
        var metadata = new SessionHistoryMetadataFilter(Kind: SessionHistoryKind.Question);
        var range = new SessionHistoryRangeFilter(1, 1, 1, 1, 0, 0);
        var digests = new List<string>();
        SessionHistoryCorrelationFilter.Parse("", "", "").Should().Be(default(SessionHistoryCorrelationFilter));
        SessionHistoryCorrelationFilter.Parse(Request.ToString("D"), Task.ToString("D"), Source.ToString("D"))
            .Should().Be(new SessionHistoryCorrelationFilter(Request, Task, Source));
        for (var mask = 0; mask < 8; mask++)
        {
            var filter = new SessionHistoryCorrelationFilter((mask & 1) != 0 ? Request : null,
                (mask & 2) != 0 ? Task : null, (mask & 4) != 0 ? Source : null);
            filter.Validate();
            var search = new SessionHistorySearch("absent needle", metadata, range, filter);
            digests.Add(search.Digest);
            search.Matches(record, token).Should().BeTrue();
            search.Digest.Should().Be(new SessionHistorySearch("NEEDLE absent needle", metadata, range, filter).Digest);
            search.Matches(record with { RequestId = null }, token).Should().Be(filter.RequestId is null);
            search.Matches(record with { TaskId = null }, token).Should().Be(filter.TaskId is null);
            search.Matches(record with { SourceId = null }, token).Should().Be(filter.SourceId is null);
            search.Matches(record with { RequestId = Source }, token).Should().Be(filter.RequestId is null);
            search.Matches(record with { TaskId = Request }, token).Should().Be(filter.TaskId is null);
            search.Matches(record with { SourceId = Task }, token).Should().Be(filter.SourceId is null);
            search.Matches(record with { Sequence = 2 }, token).Should().BeFalse();
            search.Matches(record with { Kind = SessionHistoryKind.Answer }, token).Should().BeFalse();
            search.Matches(record with { Question = "nonmatching" }, token).Should().BeFalse();
            search.Digest.Should().NotBe(new SessionHistorySearch("other", metadata, range, filter).Digest);
            search.Digest.Should().NotBe(new SessionHistorySearch("needle absent", default, range, filter).Digest);
            search.Digest.Should().NotBe(new SessionHistorySearch("needle absent", metadata, default, filter).Digest);
        }
        digests.Should().OnlyHaveUniqueItems();
        new SessionHistorySearch("needle").Digest.Should().Be(new SessionHistorySearch("NEEDLE", correlation: default).Digest);
        var command = new SessionCommand(SessionCommandOperation.HistorySearch, record.SessionId.Value)
        { HistoryQuery = "needle", HistoryMetadata = metadata, HistoryRange = range, HistoryCorrelation = new(Request, Task, Source) };
        JsonSerializer.Deserialize<SessionCommand>(JsonSerializer.Serialize(command)).Should().Be(command);
    }

    [Fact]
    public void InvalidExcludedReceiptsAndCancellationAreNotHiddenByCorrelation()
    {
        var search = new SessionHistorySearch("absent", correlation: new(Source, Source, Request));
        foreach (var record in new[] { Record() with { RequestId = Guid.Empty },
            Record() with { TaskId = Guid.Empty }, Record() with { SourceId = Guid.Empty },
            Record() with { Id = Guid.Empty, Availability = SessionHistoryAvailability.Unavailable } })
        {
            var match = () => search.Matches(record, TestContext.Current.CancellationToken);
            match.Should().Throw<InvalidDataException>();
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancel = () => search.Matches(Record(), cancelled.Token);
        cancel.Should().Throw<OperationCanceledException>();
    }

    private static SessionHistoryEvent Record() => new(Guid.NewGuid(), new(Guid.NewGuid()), 1, new(1),
        SessionHistoryKind.Question, SessionHistoryAvailability.Available, Request, Task, Source, 0, new('a', 64),
        null, false, "needle", [], null, [], null, null, null, null);
}
