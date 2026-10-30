using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("missing")]
    [InlineData("combined")]
    [InlineData("replaced")]
    [InlineData("late-private")]
    [InlineData("late-owner")]
    [InlineData("late-selection")]
    public async Task ExactObservationResolverCannotSelectAnotherItemOrPublishAfterRetirement(string fault)
    {
        using var f = new ConversationFixture();
        var original = Message(f, 1, "original");
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), original.Request, ModelProviderSelection.OllamaCandidate);
        var item = RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.Version, "{}",
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "original deployment")), DateTimeOffset.UnixEpoch);
        var expected = original with
        {
            Version = 2, Revision = 3, Execution = provenance, ExecutionMetadataRevision = 0,
            Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.None,
            AssistantResponse = new(provenance, "answer"), Observations = new(1, [item]),
        };
        var live = true;
        f.Messages = [expected];
        f.BeforeExactRead = read =>
        {
            if (read != 2) { return; }
            if (fault is "late-private") { f.CanInspect = false; }
            if (fault is "late-owner") { f.AdvanceRevision(); }
            if (fault is "late-selection") { live = false; }
            if (fault is "replaced") { f.Messages = [expected with { Observations = null }]; }
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var resolve = () => f.Service.ReadConversationDetailAsync(new(f.Session, null), expected, () => live, f.Token,
            assistant: fault is "combined", observationId: fault is "missing" ? Guid.NewGuid() : item.Id);
        if (fault is "success")
        {
            var content = await resolve();
            content.Source.Should().Be(item.ResultJson);
            content.Reference.ItemId.Value.Should().Be(item.Id);
        }
        else { await resolve.Should().ThrowAsync<Exception>(); }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }
}
