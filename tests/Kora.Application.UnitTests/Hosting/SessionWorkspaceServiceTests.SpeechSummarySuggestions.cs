using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task SpeechSuggestionSourceIsExactPassiveAndOriginalChannelWithoutBroadeningNativeDetails(RequestOrigin origin)
    {
        using var f = new ConversationFixture { CanControl = true };
        var message = Message(f, 1, "original");
        f.Messages = [message];
        var request = new HostRequest(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), origin);
        using var input = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        (await f.Service.ReadSpeechSummarySuggestionAsync(new(f.Session, null), message, () => true, f.Token))
            .Should().Be(message);
        f.ExactReads.Should().Be(2);
        f.TaskWrites.Should().BeEmpty();
        if (origin == RequestOrigin.ActivatedVoice)
        {
            await f.Service.Invoking(service => service.ReadConversationDetailAsync(new(f.Session, null), message, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Theory]
    [InlineData("session")]
    [InlineData("invocation")]
    [InlineData("system")]
    [InlineData("done")]
    [InlineData("message-session")]
    [InlineData("message-generation")]
    [InlineData("selection")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("before-target")]
    [InlineData("after-target")]
    [InlineData("body")]
    [InlineData("final-body")]
    [InlineData("final-control")]
    [InlineData("final-selection")]
    [InlineData("final-cancel")]
    public async Task SpeechSuggestionSourceRejectsForeignStaleAndLateAuthority(string failure)
    {
        using var f = new ConversationFixture { CanControl = true };
        var message = Message(f, 1, "original");
        f.Messages = [message];
        var target = new SessionWorkspaceEntry(f.Session, null);
        var live = failure is not "selection";
        var request = new HostRequest(new(Guid.NewGuid()), failure is "session" ? new(Guid.NewGuid()) : f.Session.SessionId,
            new(Guid.NewGuid()), failure is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            failure is "invocation" ? new(Guid.NewGuid()) : null);
        if (failure is "done") { target = target with { Authority = target.Authority with { IsActive = false } }; }
        if (failure is "message-session")
        { message = message with { Request = HostRequest.Create(RequestOrigin.LocalUi) }; }
        if (failure is "message-generation") { message = message with { Generation = new(2) }; }
        if (failure is "control") { f.CanControl = false; }
        if (failure is "private") { f.CanInspect = false; }
        if (failure is "before-target") { f.Session = f.Session with { Generation = new(2) }; }
        if (failure is "body") { f.Messages = [message with { Body = "different body" }]; }
        f.BeforeExactRead = read =>
        {
            if (read == 1 && failure is "after-target") { f.Session = f.Session with { Generation = new(2) }; }
            if (read != 2) { return; }
            if (failure is "final-body") { f.Messages = [message with { Body = "late body" }]; }
            if (failure is "final-control") { f.AdvanceRevision(); }
            if (failure is "final-selection") { live = false; }
            if (failure is "final-cancel") { throw new OperationCanceledException(); }
        };
        using var input = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadSpeechSummarySuggestionAsync(target, message, () => live, f.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }
}
