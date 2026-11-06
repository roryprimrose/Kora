using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionStoppedWorkReviewTests
{
    [Fact]
    public void PendingTerminalSlotRequiresExplicitNewReviewAndNeverBroadensAnOlderReview()
    {
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(request, new(1), 1, 2, DateTimeOffset.UnixEpoch, "original", null, null);
        var original = new SessionStoppedWorkReview(new(Guid.NewGuid(), request, new(1), new(2),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1)), message);
        original.PendingModelUsage.Should().BeFalse();
        original.ModelUsage.Should().BeNull();
        var pending = original with { PendingModelUsage = true };
        pending.PendingModelUsage.Should().BeTrue();
        pending.Should().NotBe(original);
        var usage = new SessionModelUsage(provenance, "Selected model", null, TimeSpan.Zero, DateTimeOffset.UnixEpoch);
        var captured = original with { ModelUsage = usage };
        captured.ModelUsage.Should().Be(usage);
        captured.PendingModelUsage.Should().BeFalse();
        captured.Target.Should().Be(original.Target);
        captured.Message.Should().Be(message);
        captured.Should().NotBe(pending);
        original.ModelUsage.Should().BeNull();
        original.PendingModelUsage.Should().BeFalse();
    }
}
