using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewRangesTests
{
    [Theory]
    [InlineData(0, 41, true)]
    [InlineData(1, 37, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 8, true)]
    [InlineData(4, 2, true)]
    [InlineData(5, 1, false)]
    [InlineData(6, 4, false)]
    [InlineData(7, 9, false)]
    [InlineData(8, 7, false)]
    public void EveryEndpointIsInclusiveOrderedAndStrictlyAdmitted(int index, long observed, bool positive)
    {
        var row = Row();
        row.Work!.Validate(row.Session.Authority.SessionId);
        foreach (var minimum in new long?[] { null, observed - 1, observed, observed + 1 })
        {
            foreach (var maximum in new long?[] { null, observed - 1, observed, observed + 1 })
            {
                var ranges = Ranges(index, new(minimum, maximum));
                var validate = () => ranges.Validate();
                if (minimum > maximum || positive && (minimum == 0 || maximum == 0))
                {
                    validate.Should().Throw<InvalidDataException>();
                    continue;
                }
                validate.Should().NotThrow();
                ranges.Matches(row).Should().Be((minimum is null || minimum <= observed)
                    && (maximum is null || maximum >= observed));
            }
        }
        foreach (var invalid in new long[] { -1, long.MinValue, positive ? 0 : -2 })
        {
            var lower = () => Ranges(index, new(invalid, null)).Validate();
            var upper = () => Ranges(index, new(null, invalid)).Validate();
            lower.Should().Throw<InvalidDataException>();
            upper.Should().Throw<InvalidDataException>();
        }
        var floor = positive ? 1 : 0;
        var valid = Ranges(index, new(floor, long.MaxValue));
        valid.Validate();
        valid.Matches(row).Should().BeTrue();
        foreach (var availability in Enum.GetValues<SessionWorkOverviewAvailability>())
        {
            valid.Matches(row with { Availability = availability }).Should()
                .Be(availability == SessionWorkOverviewAvailability.Observed);
            valid.Matches(row with { Availability = availability, Work = null }).Should().BeFalse();
            default(SessionWorkOverviewRanges).Matches(row with { Availability = availability, Work = null }).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0001")]
    [InlineData("9223372036854775807")]
    public void TextBoundsUseTheSharedInvariantDecimalPolicy(string text)
    {
        var range = SessionWorkMetadataRange.Parse(text, text);
        range.Validate(positive: false);
        range.Minimum.Should().Be(OptionalUnsignedDecimal.Parse(text));
        range.Maximum.Should().Be(range.Minimum);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("1,000")]
    [InlineData("1e2")]
    [InlineData("１")]
    [InlineData("9223372036854775808")]
    public void MalformedBoundsAreNeverNormalizedOrReinterpreted(string text)
    {
        var lower = () => SessionWorkMetadataRange.Parse(text, "");
        var upper = () => SessionWorkMetadataRange.Parse("", text);
        lower.Should().Throw<InvalidDataException>();
        upper.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AllNinePredicatesComposeAndOmissionsRemainCountsRatherThanRecordsOrAuthority()
    {
        var row = Row();
        var ranges = new SessionWorkOverviewRanges(new(41, 41), new(37, 37), new(3, 3),
            new(8, 8), new(2, 2), new(1, 1), new(4, 4), new(9, 9), new(7, 7));
        ranges.Matches(row).Should().BeTrue();
        for (var index = 0; index < 9; index++)
        {
            var changed = index switch
            {
                0 => row.Work! with { AuthorityRevision = 42 },
                1 => row.Work! with { QueueRevision = 38 },
                2 => row.Work! with { PendingCount = 4 },
                3 => row.Work! with { PendingCapacity = 9 },
                4 => row.Work! with { ExecutionSlots = 1 },
                5 => row.Work! with { ObservedCurrentFixedReads = 0 },
                6 => row.Work! with { OmittedQueueRecords = 5 },
                7 => row.Work! with { OmittedTasks = 10 },
                _ => row.Work! with { OmittedQuestions = 8 }
            };
            ranges.Matches(row with { Work = changed }).Should().BeFalse();
        }
        var omittedOnly = row with { Work = row.Work! with
        {
            QueueRecords = [], Tasks = [], PendingQuestions = [], ObservedCurrentFixedReads = 0
        } };
        var omissions = new SessionWorkOverviewRanges(OmittedQueueRecords: new(4, 4),
            OmittedTasks: new(9, 9), OmittedQuestions: new(7, 7));
        omissions.Matches(omittedOnly).Should().BeTrue();
        foreach (var content in Enum.GetValues<SessionWorkOverviewContent>().Skip(1))
        {
            SessionWorkOverviewFilter.Matches(null, omittedOnly, content).Should().BeFalse();
        }
        var zero = new SessionWorkOverviewRanges(QueueRevision: new(0, 0), PendingCount: new(0, 0),
            ObservedCurrentFixedReads: new(0, 0), OmittedQueueRecords: new(0, 0), OmittedTasks: new(0, 0),
            OmittedQuestions: new(0, 0));
        zero.Matches(omittedOnly with { Work = omittedOnly.Work! with
        {
            QueueRevision = 0, PendingCount = 0, OmittedQueueRecords = 0, OmittedTasks = 0, OmittedQuestions = 0
        } }).Should().BeTrue("zero is only an observed scalar, not completeness or absence");
    }

    [Fact]
    public void CompleteObservationValidationPrecedesAnyPotentialRangeExclusion()
    {
        var row = Row();
        var work = row.Work!;
        var foreign = HostRequest.Create(RequestOrigin.LocalUi);
        var task = work.Tasks[0];
        var question = work.PendingQuestions[0];
        var queue = work.QueueRecords[0];
        var offset = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1));
        SessionWorkOverviewObservation[] invalid =
        [
            work with { AuthorityRevision = 0 }, work with { QueueRevision = -1 },
            work with { ObservedAt = offset }, work with { PendingCount = -1 },
            work with { PendingCapacity = 0 }, work with { PendingCapacity = 11 },
            work with { ExecutionSlots = 0 }, work with { ExecutionSlots = 3 },
            work with { ObservedCurrentFixedReads = -1 }, work with { ObservedCurrentFixedReads = 0 },
            work with { QueueRecords = default }, work with { Tasks = default }, work with { PendingQuestions = default },
            work with { Tasks = [.. Enumerable.Repeat(task, 51)] },
            work with { PendingQuestions = [.. Enumerable.Repeat(question, 51)] },
            work with { OmittedQueueRecords = -1 }, work with { OmittedTasks = -1 }, work with { OmittedQuestions = -1 },
            work with { QueueRecords = [queue with { Entry = queue.Entry with { Request = foreign } }] },
            work with { QueueRecords = [queue with { Eligibility = (SessionQueueEligibility)99 }] },
            work with { QueueRecords = [queue with { PendingOrder = 0 }] },
            work with { QueueRecords = [queue with { ActiveDeadline = offset }] },
            work with { Tasks = [task with { Request = foreign }] },
            work with { Tasks = [task with { Generation = default }] },
            work with { Tasks = [task with { Revision = default }] },
            work with { Tasks = [task with { State = (HostTaskState)99 }] },
            work with { PendingQuestions = [question with { Key = new(foreign, new(Guid.NewGuid()), new(1)) }] },
            work with { PendingQuestions = [question with { Generation = default }] },
            work with { PendingQuestions = [question with { Status = QuestionStatus.Answered }] },
            work with { PendingQuestions = [question with { Kind = (QuestionKind)99 }] },
            work with { PendingQuestions = [question with { AnswerChannel = (RequestOrigin)99 }] },
            work with { PendingQuestions = [question with { ExpiresAt = offset }] },
            work with { DispatchControl = new(Guid.Empty, new(1), 0, 0, false, false) },
            work with { DispatchControl = new(Guid.NewGuid(), default, 0, 0, false, false) },
            work with { DispatchControl = new(Guid.NewGuid(), new(1), -1, 0, false, false) },
            work with { DispatchControl = new(Guid.NewGuid(), new(1), 0, -1, false, false) }
        ];
        foreach (var candidate in invalid)
        {
            var validate = () => candidate.Validate(row.Session.Authority.SessionId);
            validate.Should().Throw<InvalidDataException>();
        }
        work.Validate(row.Session.Authority.SessionId);
        (work with { DispatchControl = new(Guid.NewGuid(), new(1), 0, 0, false, false) })
            .Validate(row.Session.Authority.SessionId);
        (work with { QueueRecords = [queue with { PendingOrder = 1, ActiveDeadline = null }],
            PendingQuestions = [question with { AnswerChannel = null }] }).Validate(row.Session.Authority.SessionId);
        (work with { Tasks = [], PendingQuestions = [], QueueRecords = [], ObservedCurrentFixedReads = 0 })
            .Validate(row.Session.Authority.SessionId);
    }

    private static SessionWorkOverviewRanges Ranges(int index, SessionWorkMetadataRange range) => index switch
    {
        0 => new(AuthorityRevision: range), 1 => new(QueueRevision: range), 2 => new(PendingCount: range),
        3 => new(PendingCapacity: range), 4 => new(ExecutionSlots: range), 5 => new(ObservedCurrentFixedReads: range),
        6 => new(OmittedQueueRecords: range), 7 => new(OmittedTasks: range), _ => new(OmittedQuestions: range)
    };

    private static SessionWorkOverviewRow Row()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var now = DateTimeOffset.UnixEpoch;
        var queue = new SessionQueueEntry(request, new(1), new(1), 1, SessionQueueState.Pending,
            Guid.NewGuid(), 1, now, now.AddMinutes(30)).Admit(new(2), 2, now, 5);
        var question = new SessionWorkQuestion(new(request, new(Guid.NewGuid()), new(1)), new(1),
            QuestionStatus.Pending, QuestionKind.SingleChoice, RequestOrigin.LocalUi, now.AddMinutes(30));
        return new(new(new(request.SessionId, new(1), true), null), SessionWorkOverviewAvailability.Observed,
            new(41, now, 37, 3, 8, 2, 1, [new(queue, SessionQueueEligibility.Current, queue.ActiveDeadlineAt)], 4,
                [new(request, new(1), new(1), HostTaskState.Unknown, false)], 9, [question], 7));
    }
}
