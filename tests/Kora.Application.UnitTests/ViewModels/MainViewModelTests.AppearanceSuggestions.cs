using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static ConversationMessage AppearanceSuggestionMessage(bool reader = false)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
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
            f.AppearancePreferences.SavedMode.Should().BeNull();
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
