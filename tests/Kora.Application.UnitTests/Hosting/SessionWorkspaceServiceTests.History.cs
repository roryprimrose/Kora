using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Exact_history_and_get_are_read_only_freshly_admitted_and_do_not_put_content_in_activity(RequestOrigin origin)
    {
        using var fixture = new Fixture();
        const string content = "Only the native user may read this stored content";
        fixture.HistoryEvent = new(Guid.NewGuid(), fixture.Request.SessionId, 1, new(1),
            SessionHistoryKind.Question, SessionHistoryAvailability.Available,
            fixture.Request.RequestId.Value, fixture.Request.TaskId.Value, Guid.NewGuid(), 1,
            new string('a', 64), 1, false, content, [], null, [], null, null, null, null);
        var tags = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => tags.AddRange(activity.TagObjects.Select(pair => pair.Key + "=" + pair.Value)),
        };
        ActivitySource.AddActivityListener(listener);
        var page = await fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.History, fixture.Request.SessionId.Value),
            origin, () => true, fixture.Token);
        page.History!.SessionId.Should().Be(fixture.Request.SessionId);
        var result = await fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.HistoryGet, fixture.Request.SessionId.Value)
        {
            HistoryEventId = fixture.HistoryEvent.Id,
        }, origin, () => true, fixture.Token);
        result.HistoryEvent.Should().Be(fixture.HistoryEvent);
        fixture.TaskWrites.Should().BeEmpty();
        fixture.ControlCalls.Should().Be(0);
        tags.Should().NotContain(value => value.Contains(content, StringComparison.Ordinal));
        fixture.Logger.Messages.Should().BeEmpty();
        fixture.HistoryEvent = null;
        (await fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.HistoryGet, fixture.Request.SessionId.Value)
        {
            HistoryEventId = Guid.NewGuid(),
        }, origin, () => true, fixture.Token)).Outcome.Should().Be("unknown");
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("id")]
    [InlineData("admission")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("late-admission")]
    [InlineData("late-privacy")]
    [InlineData("event-id")]
    [InlineData("cancel")]
    [InlineData("storage")]
    public async Task Invalid_missing_stale_cancelled_history_is_never_success_or_new_work(string mode)
    {
        using var fixture = new Fixture();
        var allowed = true;
        if (mode is "privacy") { fixture.CanInspect = false; }
        if (mode is "revision") { fixture.AfterHistoryRead = fixture.AdvanceRevision; }
        if (mode is "late-admission") { fixture.AfterHistoryRead = () => allowed = false; }
        if (mode is "late-privacy") { fixture.AfterHistoryRead = () => fixture.CanInspect = false; }
        if (mode is "cancel") { fixture.HistoryFailure = new OperationCanceledException(); }
        if (mode is "storage") { fixture.HistoryFailure = new IOException("Storage unavailable"); }
        if (mode is "admission") { allowed = false; }
        var command = new SessionCommand(mode is "event-id" ? SessionCommandOperation.HistoryGet : SessionCommandOperation.History,
            mode is "id" ? null : fixture.Request.SessionId.Value);
        var act = () => fixture.Service.ExecuteCommandAsync(command,
            mode is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => allowed, fixture.Token);
        await act.Should().ThrowAsync<Exception>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Native_passive_exact_event_and_missing_history_adapter_are_fail_closed()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        (await fixture.Service.ReadHistoryEventAsync(fixture.Request.SessionId, Guid.NewGuid(), fixture.Token)).Should().BeNull();
        var store = new Configuration.AudioControlTestStore();
        var service = new Kora.Application.Hosting.SessionWorkspaceService(store, new(store), fixture, fixture.Logger);
        var act = () => service.ReadHistoryAsync(fixture.Request.SessionId, null, 1, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not provide durable history*");
    }
}
