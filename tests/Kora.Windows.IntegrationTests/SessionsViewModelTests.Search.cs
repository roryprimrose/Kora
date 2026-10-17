using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Theory]
    [InlineData("request")]
    [InlineData("task")]
    [InlineData("source")]
    public async Task CorrelationEditsRetireCompletedRowsCursorSelectionAndDetailWithoutAnyRead(string field)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new HeldStore();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        model.HistorySessionId = f.Request.SessionId.Value.ToString("D");
        model.HistoryQuery = "needle";
        model.HistoryRequestId = f.Request.RequestId.Value.ToString("D");
        model.HistoryTaskId = f.Request.TaskId.Value.ToString("D");
        model.HistorySourceId = f.Request.RequestId.Value.ToString("D");
        held.HistoryReads.Should().Be(0);
        held.HistoryCompletion.SetResult(new(f.Request.SessionId, new(1), false, 26,
            [.. Enumerable.Range(1, 26).Select(i => new SessionHistoryEvent(Guid.NewGuid(), f.Request.SessionId, i, new(1),
                SessionHistoryKind.Question, SessionHistoryAvailability.Available, f.Request.RequestId.Value,
                f.Request.TaskId.Value, f.Request.RequestId.Value, 0, new('a', 64), null, false, "needle",
                [], null, [], null, null, null, null))], null));
        await model.SearchHistoryAsync();
        held.HistoryReads.Should().Be(2, "candidate page and final snapshot resolution");
        model.HistoryRecords.Should().HaveCount(25);
        model.CanNextHistorySearch.Should().BeTrue();
        model.Detail.Should().NotBeEmpty();
        model.SelectHistoryRecord(model.HistoryRecords[0]);
        if (field is "request") { model.HistoryRequestId = " "; }
        if (field is "task") { model.HistoryTaskId = " "; }
        if (field is "source") { model.HistorySourceId = " "; }
        held.HistoryReads.Should().Be(2);
        model.HistoryRecords.Should().BeEmpty();
        model.SelectedHistoryRecord.Should().BeNull();
        model.CanNextHistorySearch.Should().BeFalse();
        model.CanOpenHistoryDetail.Should().BeFalse();
        model.Detail.Should().BeEmpty();
        model.ClearHistorySearchInputs();
        held.HistoryReads.Should().Be(2);
        model.HistoryRequestId.Should().BeEmpty();
        model.HistoryTaskId.Should().BeEmpty();
        model.HistorySourceId.Should().BeEmpty();
        held.ControlCalls.Should().Be(0);
        model.Close();
    }

    [Theory]
    [InlineData("query")]
    [InlineData("subject")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("kind")]
    [InlineData("state")]
    [InlineData("decision")]
    [InlineData("question")]
    [InlineData("channel")]
    [InlineData("source")]
    [InlineData("clear")]
    [InlineData("minimum-sequence")]
    [InlineData("maximum-sequence")]
    [InlineData("minimum-generation")]
    [InlineData("maximum-generation")]
    [InlineData("minimum-source")]
    [InlineData("maximum-source")]
    [InlineData("minimum-audit")]
    [InlineData("maximum-audit")]
    [InlineData("request-id")]
    [InlineData("task-id")]
    [InlineData("source-id")]
    public async Task Native_search_suppresses_cancelled_or_late_content_and_has_explicit_recovery(string mode)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new HeldStore();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        model.HistorySessionId = f.Request.SessionId.Value.ToString("D");
        model.HistoryQuery = "needle";
        model.HistoryKindChoice = new(SessionHistoryKind.Question);
        model.HistoryMinimumSequence = "1";
        model.HistoryMinimumSequence = "1";
        model.HistoryMaximumSequence = "9";
        model.HistoryMinimumGeneration = "1";
        model.HistoryMaximumGeneration = "9";
        model.HistoryMinimumSourceRevision = "0";
        model.HistoryMaximumSourceRevision = "9";
        model.HistoryMinimumAuditSequence = "";
        model.HistoryMaximumAuditSequence = "";
        model.HistoryRequestId = f.Request.RequestId.Value.ToString("D");
        model.HistoryTaskId = f.Request.TaskId.Value.ToString("D");
        model.HistorySourceId = f.Request.RequestId.Value.ToString("D");
        model.HistoryRequestId = model.HistoryRequestId;
        held.HistoryReads.Should().Be(0);
        var search = model.SearchHistoryAsync();
        model.CanCancelHistorySearch.Should().BeTrue();
        model.CanSearchHistory.Should().BeFalse();
        if (mode is "query") { model.HistoryQuery = "another"; }
        if (mode is "subject") { model.HistorySessionId = Guid.NewGuid().ToString("D"); }
        if (mode is "cancel") { model.CancelHistorySearch(); }
        if (mode is "close") { model.Close(); }
        if (mode is "privacy") { access.CanInspect = false; }
        if (mode is "kind") { model.HistoryKindChoice = new(SessionHistoryKind.Answer); }
        if (mode is "state") { model.HistoryTaskStateChoice = new(HostTaskState.Succeeded); }
        if (mode is "decision") { model.HistoryDecisionChoice = new(HostInteractionOutcome.Answered); }
        if (mode is "question") { model.HistoryQuestionStatusChoice = new(QuestionStatus.Answered); }
        if (mode is "channel") { model.HistoryAnswerChannelChoice = new(RequestOrigin.LocalUi); }
        if (mode is "source") { access.ControlRevision++; }
        if (mode is "clear") { model.ClearHistorySearchInputs(); }
        if (mode is "minimum-sequence") { model.HistoryMinimumSequence = "2"; }
        if (mode is "maximum-sequence") { model.HistoryMaximumSequence = "1"; }
        if (mode is "minimum-generation") { model.HistoryMinimumGeneration = "2"; }
        if (mode is "maximum-generation") { model.HistoryMaximumGeneration = "1"; }
        if (mode is "minimum-source") { model.HistoryMinimumSourceRevision = "1"; }
        if (mode is "maximum-source") { model.HistoryMaximumSourceRevision = "0"; }
        if (mode is "minimum-audit") { model.HistoryMinimumAuditSequence = "1"; }
        if (mode is "maximum-audit") { model.HistoryMaximumAuditSequence = "1"; }
        if (mode is "request-id") { model.HistoryRequestId = Guid.NewGuid().ToString("D"); }
        if (mode is "task-id") { model.HistoryTaskId = Guid.NewGuid().ToString("D"); }
        if (mode is "source-id") { model.HistorySourceId = Guid.NewGuid().ToString("D"); }
        held.HistoryReads.Should().Be(1, "edits, clear and cancellation must not read storage");
        var record = new SessionHistoryEvent(Guid.NewGuid(), f.Request.SessionId, 1, new(1),
            SessionHistoryKind.Question, SessionHistoryAvailability.Available, f.Request.RequestId.Value,
            f.Request.TaskId.Value, f.Request.RequestId.Value, 1,
            new('a', 64), null, false, "needle", [], null, [], null, null, null, null);
        held.HistoryCompletion.SetResult(new(f.Request.SessionId, new(1), false, 1, [record], null));
        await search;
        model.HistoryRecords.Should().BeEmpty();
        model.Detail.Should().BeEmpty();
        model.CanCancelHistorySearch.Should().BeFalse();
        model.SelectedHistoryRecord.Should().BeNull();
        held.ControlCalls.Should().Be(0);
        model.Close();
        model.HistoryKindChoice.Value.Should().BeNull();
        model.HistoryTaskStateChoice.Value.Should().BeNull();
        model.HistoryDecisionChoice.Value.Should().BeNull();
        model.HistoryQuestionStatusChoice.Value.Should().BeNull();
        model.HistoryAnswerChannelChoice.Value.Should().BeNull();
        model.HistoryMinimumSequence.Should().BeEmpty();
        model.HistoryMaximumSequence.Should().BeEmpty();
        model.HistoryMinimumGeneration.Should().BeEmpty();
        model.HistoryMaximumGeneration.Should().BeEmpty();
        model.HistoryMinimumSourceRevision.Should().BeEmpty();
        model.HistoryMaximumSourceRevision.Should().BeEmpty();
        model.HistoryMinimumAuditSequence.Should().BeEmpty();
        model.HistoryMaximumAuditSequence.Should().BeEmpty();
        model.HistoryRequestId.Should().BeEmpty();
        model.HistoryTaskId.Should().BeEmpty();
        model.HistorySourceId.Should().BeEmpty();
        model.HistoryRequestId = "closed";
        model.HistoryTaskId = "closed";
        model.HistorySourceId = "closed";
        model.HistoryRequestId.Should().BeEmpty();
        model.HistoryTaskId.Should().BeEmpty();
        model.HistorySourceId.Should().BeEmpty();
    }
}
