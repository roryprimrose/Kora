using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static ConversationMessage AppearanceSuggestionMessage(bool reader = false, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = HostRequest.Create(origin);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UtcNow, "Suggest a more readable appearance.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, reader ? "set appearance.reader-text-scale to 125" : "set appearance.theme to dark"),
        };
    }

    private static HostActivity AppearanceSuggestionInput(ConversationMessage message) =>
        HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()),
            RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);

    [Theory]
    [InlineData(AppearanceOption.PresenceDisplay, "false")]
    [InlineData(AppearanceOption.PresenceDisplay, "true")]
    [InlineData(AppearanceOption.PresenceTimeout, "1")]
    [InlineData(AppearanceOption.PresenceTimeout, "60")]
    [InlineData(AppearanceOption.PresenceSize, "240")]
    [InlineData(AppearanceOption.PresenceSize, "600")]
    [InlineData(AppearanceOption.DotSize, "50")]
    [InlineData(AppearanceOption.DotSize, "200")]
    [InlineData(AppearanceOption.DotDensity, "25")]
    [InlineData(AppearanceOption.DotDensity, "200")]
    [InlineData(AppearanceOption.MovementSpeed, "25")]
    [InlineData(AppearanceOption.MovementSpeed, "200")]
    [InlineData(AppearanceOption.SpeechScaling, "false")]
    [InlineData(AppearanceOption.SpeechScaling, "true")]
    [InlineData(AppearanceOption.SpeechScaleAmount, "0")]
    [InlineData(AppearanceOption.SpeechScaleAmount, "200")]
    public async Task PresenceSuggestionConfirmationUpdatesOnlyExactSavedOptionAndExistingPresenceConsumer(
        AppearanceOption option, string value)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            var descriptor = AppearanceOptionRegistry.Get(option);
            var message = AppearanceSuggestionMessage();
            message = message with { AssistantResponse = message.AssistantResponse! with
                { Body = $"set {descriptor.Id} to {value}" } };
            var restored = new AppearanceConfigurationService(f.AppearancePreferences, f.Audit,
                NullLogger<AppearanceConfigurationService>.Instance);
            var before = restored.Get(option);
            var changesValue = !string.Equals(AppearanceCommand.Format(before.Value), value, StringComparison.Ordinal);
            var auditCount = f.Audit.Events.Count;
            AppearanceSuggestionReview review;
            using (AppearanceSuggestionInput(message))
            {
                review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true,
                    _ => Task.CompletedTask, TestContext.Current.CancellationToken);
                review.Description.Should().Contain($"current {AppearanceCommand.Format(before.Value)} (default)")
                    .And.Contain($"proposed {value} {descriptor.Units}").And.Contain("shared device-local");
                restored.Get(option).Should().Be(before);
                f.Audit.Events.Should().HaveCount(auditCount);
            }
            using (review)
            using (AppearanceSuggestionInput(message))
            {
                (await f.ViewModel.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken))
                    .Outcome.Should().Be(changesValue ? AppearanceApplyOutcome.Applied : AppearanceApplyOutcome.Unchanged);
                await f.ViewModel.Invoking(model => model.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            AppearanceValue consumer = option switch
            {
                AppearanceOption.PresenceDisplay => new AppearanceValue.Toggle(f.ViewModel.IsPresenceDisplayEnabled),
                AppearanceOption.PresenceTimeout => new AppearanceValue.Number(f.ViewModel.PresenceTimeoutSeconds),
                AppearanceOption.PresenceSize => new AppearanceValue.Number(f.ViewModel.PresenceSizePixels),
                AppearanceOption.DotSize => new AppearanceValue.Number(f.ViewModel.PresenceDotSizePercent),
                AppearanceOption.DotDensity => new AppearanceValue.Number(f.ViewModel.PresenceDotDensityPercent),
                AppearanceOption.MovementSpeed => new AppearanceValue.Number(f.ViewModel.PresenceMovementSpeedPercent),
                AppearanceOption.SpeechScaling => new AppearanceValue.Toggle(f.ViewModel.IsPresenceSpeechScalingEnabled),
                AppearanceOption.SpeechScaleAmount => new AppearanceValue.Number(f.ViewModel.PresenceSpeechScaleAmountPercent),
                _ => throw new ArgumentOutOfRangeException(nameof(option)),
            };
            AppearanceCommand.Format(consumer).Should().Be(value);
            restored.Reload();
            foreach (var item in AppearanceOptionRegistry.Options)
            {
                var state = restored.Get(item.Option);
                state.IsSaved.Should().Be(item.Option == option && changesValue);
                state.Value.Should().Be(item.Option == option ? consumer : item.Default);
            }
            if (changesValue)
            {
                f.Audit.Events.Skip(auditCount).Select(item => item.Outcome).Should()
                    .Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
            }
            else { f.Audit.Events.Should().HaveCount(auditCount, "confirming the current default preserves the writer's no-op behavior"); }
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Theory]
    [InlineData(AppearanceOption.PresenceDisplay, "false")]
    [InlineData(AppearanceOption.PresenceTimeout, "60")]
    [InlineData(AppearanceOption.PresenceSize, "600")]
    [InlineData(AppearanceOption.DotSize, "50")]
    [InlineData(AppearanceOption.DotDensity, "25")]
    [InlineData(AppearanceOption.MovementSpeed, "50")]
    [InlineData(AppearanceOption.SpeechScaling, "false")]
    [InlineData(AppearanceOption.SpeechScaleAmount, "200")]
    public async Task PresenceSuggestionRequiresOriginalLocalSourceAndSurfacesWriteFailureWithoutRetry(
        AppearanceOption option, string value)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            var descriptor = AppearanceOptionRegistry.Get(option);
            var beforeAudit = f.Audit.Events.Count;
            foreach (var origin in new[] { RequestOrigin.ActivatedVoice, RequestOrigin.HostSystem })
            {
                var denied = AppearanceSuggestionMessage(origin: origin);
                denied = denied with { AssistantResponse = denied.AssistantResponse! with { Body = $"set {descriptor.Id} to {value}" } };
                using (AppearanceSuggestionInput(denied))
                {
                    await f.ViewModel.Invoking(model => model.ReviewAppearanceSuggestionAsync(denied, () => true,
                        _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<Exception>();
                }
            }
            f.Audit.Events.Should().HaveCount(beforeAudit);
            var message = AppearanceSuggestionMessage();
            message = message with { AssistantResponse = message.AssistantResponse! with { Body = $"set {descriptor.Id} to {value}" } };
            AppearanceSuggestionReview review;
            using (AppearanceSuggestionInput(message))
            {
                review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true,
                    _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            }
            f.AppearancePreferences.SaveException = new IOException("Synthetic preference write unavailable.");
            using (review)
            using (AppearanceSuggestionInput(message))
            {
                var result = await f.ViewModel.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken);
                result.Outcome.Should().Be(AppearanceApplyOutcome.Failed);
                result.Error.Should().Contain("Synthetic preference write unavailable");
                result.State.Value.Should().Be(descriptor.Default);
                review.Available.Should().BeFalse();
                await f.ViewModel.Invoking(model => model.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            var restored = new AppearanceConfigurationService(f.AppearancePreferences, f.Audit,
                NullLogger<AppearanceConfigurationService>.Instance);
            foreach (var item in AppearanceOptionRegistry.Options)
            {
                restored.Get(item.Option).IsSaved.Should().BeFalse();
                restored.Get(item.Option).Value.Should().Be(item.Default);
            }
            f.Audit.Events.Skip(beforeAudit).Select(item => item.Outcome).Should()
                .Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Failed);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppearanceSuggestionConfirmationPublishesSameSharedNativeEffectsWithoutModelCalls(bool reader)
    {
        var f = await Fixture.CreateInitializedAsync();
        var message = AppearanceSuggestionMessage(reader);
        AppearanceSuggestionReview review;
        using (AppearanceSuggestionInput(message))
        {
            review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            f.AppearancePreferences.SavedMode.Should().BeNull();
            f.AppearancePreferences.ReaderTextScalePercent.Should().BeNull();
        }
        using (review)
        using (AppearanceSuggestionInput(message))
        {
            var result = await f.ViewModel.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(AppearanceApplyOutcome.Applied);
            if (reader)
            {
                f.AppearancePreferences.ReaderTextScalePercent.Should().Be(125);
                f.ViewModel.ReaderTextScalePercent.Should().Be(125);
            }
            else
            {
                f.AppearancePreferences.SavedMode.Should().Be(ApplicationThemeMode.Dark);
                f.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
            }
            f.ViewModel.IsReaderTypographyUpdatePending.Should().BeFalse();
            f.Reasoner.Requests.Should().BeEmpty();
        }
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("revision")]
    [InlineData("binding")]
    [InlineData("locked")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("disposed")]
    public async Task AppearanceSuggestionConfirmationKeepsCapturedHostAndCallGates(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        long revision = 1;
        f.ViewModel.BindCallOwnershipGate(() => true, () => revision);
        var message = AppearanceSuggestionMessage();
        message = message with { AssistantResponse = message.AssistantResponse! with { Body = "set appearance.movement-speed to 50" } };
        AppearanceSuggestionReview review;
        using (AppearanceSuggestionInput(message))
        {
            review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        switch (loss)
        {
            case "owner": f.ViewModel.BindCallOwnershipGate(() => false); break;
            case "revision": revision++; break;
            case "binding": f.ViewModel.BindCallOwnershipGate(() => true, () => revision); break;
            case "locked": f.Session.IsUnlocked = false; break;
            case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
            case "call":
                await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision,
                    TestContext.Current.CancellationToken);
                break;
            case "disposed": await f.ViewModel.DisposeAsync(); break;
        }
        using (review)
        using (AppearanceSuggestionInput(message))
        {
            review.Available.Should().BeFalse();
            await f.ViewModel.Invoking(model => model.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            f.AppearancePreferences.SavedPresenceMovementSpeedPercent.Should().BeNull();
            review.Description.Should().BeEmpty();
        }
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task AppearanceSuggestionDisposalJoinsPendingConfirmationAndRetiresLateWrite()
    {
        var f = await Fixture.CreateInitializedAsync();
        var message = AppearanceSuggestionMessage(reader: true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirming = false;
        async Task Validate(CancellationToken token)
        {
            if (!confirming) { return; }
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        }
        AppearanceSuggestionReview review;
        using (AppearanceSuggestionInput(message))
        {
            review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken);
        }
        using (review)
        using (AppearanceSuggestionInput(message))
        {
            confirming = true;
            var confirmation = f.ViewModel.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken);
            await entered.Task;
            f.ViewModel.IsReaderTypographyUpdatePending.Should().BeTrue();
            var openingReader = f.ViewModel.EnsureReaderTypographyAvailable;
            openingReader.Should().Throw<InvalidOperationException>();
            var disposal = f.ViewModel.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            release.TrySetResult();
            await confirmation.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>();
            await disposal.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>();
            f.AppearancePreferences.ReaderTextScalePercent.Should().BeNull();
        }
    }
}
