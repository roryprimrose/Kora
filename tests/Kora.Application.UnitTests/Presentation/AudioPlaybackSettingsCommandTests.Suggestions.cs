using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Presentation;

public sealed partial class AudioPlaybackSettingsCommandTests
{
    private static ConversationMessage VolumeSuggestionMessage()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Suggest quieter Kora speech.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "set speech.playback-volume to 30"),
        };
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disposed")]
    [InlineData("busy")]
    [InlineData("foreign")]
    public async Task VolumeSuggestionPlaybackOwnerRejectsMissingDisposedBusyAndForeignReviewAuthority(string failure)
    {
        await using var f = new AudioPlaybackSettingsFixture();
        using var settings = f.CreateSettings();
        using var missing = new AudioPlaybackSettings(f.Dispatcher, NullLogger<AudioPlaybackSettings>.Instance);
        var message = VolumeSuggestionMessage();
        using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var confirming = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Validate(CancellationToken _)
        {
            if (!confirming) { return; }
            entered.TrySetResult();
            await release.Task;
        }
        using var review = await settings.ReviewVolumeSuggestionAsync(f.Policy, TimeProvider.System, message,
            () => true, Validate, TestContext.Current.CancellationToken);
        var target = failure is "missing" ? missing : settings;
        if (failure is "disposed") { target.Dispose(); }
        if (failure is "foreign")
        {
            await using var other = new AudioPlaybackSettingsFixture();
            using var foreign = other.CreateSettings();
            await foreign.Invoking(owner => owner.ConfirmVolumeSuggestionAsync(review, SecurityAuditInitiator.LocalUser,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            f.VolumePreferences.Load().Should().BeNull();
            return;
        }
        using var confirmInput = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        Task<bool>? work = null;
        if (failure is "busy")
        {
            confirming = true;
            work = target.ConfirmVolumeSuggestionAsync(review, SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
            await entered.Task;
            target.IsVolumeControlActive.Should().BeTrue();
        }
        var exception = failure is "disposed" ? typeof(ObjectDisposedException) : typeof(InvalidOperationException);
        var read = FluentActions.Awaiting(() => target.ReviewVolumeSuggestionAsync(f.Policy, TimeProvider.System, message,
            () => true, Validate, TestContext.Current.CancellationToken));
        var write = FluentActions.Awaiting(() => target.ConfirmVolumeSuggestionAsync(review, SecurityAuditInitiator.LocalUser,
            TestContext.Current.CancellationToken));
        if (exception == typeof(ObjectDisposedException))
        {
            await read.Should().ThrowAsync<ObjectDisposedException>();
            await write.Should().ThrowAsync<ObjectDisposedException>();
        }
        else
        {
            await read.Should().ThrowAsync<InvalidOperationException>();
            await write.Should().ThrowAsync<InvalidOperationException>();
        }
        if (work is not null)
        {
            target.Dispose();
            work.IsCompleted.Should().BeFalse();
            release.SetResult();
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        }
        f.VolumePreferences.Load().Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
    }
}
