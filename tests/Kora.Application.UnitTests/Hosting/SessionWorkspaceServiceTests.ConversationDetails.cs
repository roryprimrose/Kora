using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullAssistantDetailsUseExactRetainedSourceAndCannotInventAMissingAnswer(bool missing)
    {
        using var f = new ConversationFixture();
        var original = Message(f, 1, "original");
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), original.Request, ModelProviderSelection.OllamaCandidate);
        var expected = missing ? original : original with
        {
            Version = 2, Revision = 3, Execution = provenance, ExecutionMetadataRevision = 0,
            Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome,
            AssistantResponse = new(provenance, "# complete assistant answer"),
        };
        f.Messages = [expected];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => f.Service.ReadConversationDetailAsync(new(f.Session, null), expected, () => true, f.Token, assistant: true);
        if (missing) { await read.Should().ThrowAsync<InvalidOperationException>(); }
        else
        {
            var content = await read();
            content.Source.Should().Be(expected.AssistantResponse!.Body);
            content.Origin.Should().Be(DetailContentOrigin.RetainedAssistant);
            content.Kind.Should().Be(DetailContentKind.Markdown);
            content.Reference.ItemId.Value.Should().Be(provenance.Turn.Value);
            f.ExactReads.Should().Be(2);
        }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConversationDetailsResolveOnlyActualExactRecordWithPassiveDoneAndSeparateHost(bool active)
    {
        using var f = new ConversationFixture();
        f.Session = f.Session with { IsActive = active };
        var expected = Message(f, 1, " exact original e\u0301 😀\r\n\t<script>inert</script>  ");
        f.Messages = [expected];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var content = await f.Service.ReadConversationDetailAsync(new(f.Session, null), expected, () => true, f.Token);
        content.Origin.Should().Be(DetailContentOrigin.RetainedConversation);
        content.Kind.Should().Be(DetailContentKind.PlainText);
        content.Source.Should().Be(expected.Body);
        content.ConversationMessage.Should().Be(expected);
        content.SessionSource.Should().BeNull();
        f.ExactReads.Should().Be(2, "the final source is independently revalidated");
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
        f.Session.IsActive.Should().Be(active);
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("foreign-session")]
    [InlineData("foreign-record")]
    [InlineData("target")]
    [InlineData("body")]
    [InlineData("revision")]
    [InlineData("outcome")]
    [InlineData("final-target")]
    [InlineData("final-body")]
    [InlineData("final-selection")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("cancel")]
    [InlineData("invalid")]
    public async Task ConversationDetailsRejectStaleAndLatePublicationWithoutFollowingNewBodies(string fault)
    {
        using var f = new ConversationFixture();
        var expected = Message(f, 1, "exact original body");
        f.Messages = [expected];
        var target = new SessionWorkspaceEntry(f.Session, null);
        var admitted = fault is not "selection";
        if (fault is "foreign-session") { target = target with { Authority = target.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "foreign-record")
        {
            expected = expected with { Request = new(expected.Request.RequestId, new(Guid.NewGuid()),
                expected.Request.TaskId, RequestOrigin.LocalUi) };
        }
        if (fault is "target") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "invalid") { expected = expected with { Sequence = 0 }; }
        if (fault is "body") { f.Messages = [expected with { Body = "new body" }]; }
        if (fault is "revision") { f.Messages = [expected with { Revision = 1, Outcome = null, Reason = null }]; }
        if (fault is "outcome") { f.Messages = [expected with { Reason = ModelTurnReason.GenerationNotEnabled }]; }
        f.BeforeExactRead = read =>
        {
            if (read != 2) { return; }
            if (fault is "final-target") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "final-body") { f.Messages = [expected with { Body = "replacement" }]; }
            if (fault is "final-selection") { admitted = false; }
            if (fault is "private") { f.CanInspect = false; }
            if (fault is "owner") { f.AdvanceRevision(); }
            if (fault is "cancel") { throw new OperationCanceledException(); }
        };
        if (fault is "final-target")
        {
            f.BeforeExactRead = read => { if (read == 1) { f.Session = f.Session with { Generation = new(2) }; } };
        }
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadConversationDetailAsync(target, expected, () => admitted, f.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("voice")]
    [InlineData("invocation")]
    [InlineData("session")]
    public async Task ConversationDetailsRejectNonNativeAndForeignHostContextBeforeReadingOriginal(string fault)
    {
        using var f = new ConversationFixture();
        var expected = Message(f, 1, "exact body");
        f.Messages = [expected];
        var request = new HostRequest(f.Request.RequestId,
            fault is "session" ? new(Guid.NewGuid()) : f.Session.SessionId, f.Request.TaskId,
            fault is "system" ? RequestOrigin.HostSystem : fault is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            fault is "invocation" ? new(Guid.NewGuid()) : null);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadConversationDetailAsync(new(f.Session, null), expected, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.ExactReads.Should().Be(0);
    }
}
