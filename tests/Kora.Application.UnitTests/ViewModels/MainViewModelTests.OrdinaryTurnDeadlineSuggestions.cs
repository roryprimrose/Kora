using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Communication;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryTurnDeadlineSuggestionNativeHostConfirmsOnlyFuturePreference(bool reset)
    {
        await using var deadline = new OrdinaryDeadlineConfigurationFixture();
        deadline.Preferences.Save(new(7));
        var f = new Fixture(ordinaryDeadlineConfiguration: deadline.Service);
        var message = OrdinaryTurnDeadlineSuggestionReviewTests.Answer(reset ? "reset work.active-deadline-minutes"
            : "set work.active-deadline-minutes to 10");
        OrdinaryTurnDeadlineSuggestionReview review;
        using (OrdinaryTurnDeadlineSuggestionReviewTests.Input(message))
        {
            review = await f.ViewModel.ReviewOrdinaryTurnDeadlineSuggestionAsync(message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            review.Description.Should().Contain("Current saved: 7 minutes");
            deadline.Service.Capture().Minutes.Should().Be(7);
            deadline.Audit.Events.Should().BeEmpty();
        }
        using (review)
        using (OrdinaryTurnDeadlineSuggestionReviewTests.Input(message))
        { (await f.ViewModel.ConfirmOrdinaryTurnDeadlineSuggestionAsync(review, TestContext.Current.CancellationToken)).Should().BeTrue(); }
        deadline.Service.Capture().Should().Be(reset ? OrdinaryTurnDeadline.Default : new OrdinaryTurnDeadline(10));
        f.Voice.StartedMicrophone.Should().BeNull();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task OrdinaryTurnDeadlineSuggestionWithoutServiceIsExplicitlyUnavailable()
    {
        var f = new Fixture();
        await f.ViewModel.Invoking(view => view.ReviewOrdinaryTurnDeadlineSuggestionAsync(
            OrdinaryTurnDeadlineSuggestionReviewTests.Answer(), () => true, _ => Task.CompletedTask,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("binding")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("consent")]
    [InlineData("source")]
    [InlineData("native-binding")]
    [InlineData("direct")]
    [InlineData("foreign-owner")]
    [InlineData("closed")]
    public async Task OrdinaryTurnDeadlineSuggestionRequiresOriginalCurrentHostUntilCommit(string loss)
    {
        await using var deadline = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: deadline.Service);
        var message = OrdinaryTurnDeadlineSuggestionReviewTests.Answer();
        long owner = 1;
        var live = true;
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        OrdinaryTurnDeadlineSuggestionReview review;
        using (OrdinaryTurnDeadlineSuggestionReviewTests.Input(message))
        { review = await f.ViewModel.ReviewOrdinaryTurnDeadlineSuggestionAsync(message, () => live,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken); }
        if (loss is "ownership") { owner++; }
        if (loss is "binding") { f.ViewModel.BindCallOwnershipGate(() => true, () => owner); }
        if (loss is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (loss is "call") { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
        if (loss is "consent") { await f.ViewModel.SetVoiceConsentAsync(true); }
        if (loss is "source") { live = false; }
        if (loss is "native-binding") { f.ViewModel.BindOrdinaryDeadlineNativeLifetime(() => true); }
        if (loss is "direct") { await f.RunAsync("get work.active-deadline-minutes"); }
        if (loss is "closed") { f.ViewModel.Dispose(); }
        await using var otherDeadline = new OrdinaryDeadlineConfigurationFixture();
        var target = loss is "foreign-owner" ? new Fixture(ordinaryDeadlineConfiguration: otherDeadline.Service).ViewModel : f.ViewModel;
        using (review)
        using (OrdinaryTurnDeadlineSuggestionReviewTests.Input(message))
        { await target.Invoking(view => view.ConfirmOrdinaryTurnDeadlineSuggestionAsync(review, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>(); }
        deadline.Preferences.Load().Should().BeNull();
        deadline.Audit.Events.Should().BeEmpty();
        if (!ReferenceEquals(target, f.ViewModel)) { await target.DisposeAsync(); }
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryTurnDeadlineSuggestionClosureJoinsAwaitingSourceAndSuppressesLateCompletion(bool confirming)
    {
        await using var deadline = new OrdinaryDeadlineConfigurationFixture();
        var f = new Fixture(ordinaryDeadlineConfiguration: deadline.Service);
        f.ViewModel.BindOrdinaryDeadlineNativeLifetime(() => true);
        var message = OrdinaryTurnDeadlineSuggestionReviewTests.Answer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = !confirming;
        async Task Validate(CancellationToken token)
        {
            if (!hold) { return; }
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        }
        OrdinaryTurnDeadlineSuggestionReview? review = null;
        if (confirming)
        {
            using (OrdinaryTurnDeadlineSuggestionReviewTests.Input(message))
            { review = await f.ViewModel.ReviewOrdinaryTurnDeadlineSuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken); }
            hold = true;
        }
        using (OrdinaryTurnDeadlineSuggestionReviewTests.Input(message))
        {
            Task work;
            if (confirming)
            {
                work = f.ViewModel.ConfirmOrdinaryTurnDeadlineSuggestionAsync(review!, TestContext.Current.CancellationToken);
            }
            else
            { work = f.ViewModel.ReviewOrdinaryTurnDeadlineSuggestionAsync(message, () => true, Validate, TestContext.Current.CancellationToken); }
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            f.ViewModel.CanChangeOrdinaryDeadlineNative.Should().BeFalse();
            await f.ViewModel.Invoking(view => view.ReviewOrdinaryTurnDeadlineSuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            var disposal = f.ViewModel.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse("the exact owned source validation must join");
            release.TrySetResult();
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
            review?.Dispose();
            deadline.Preferences.Load().Should().BeNull();
            deadline.Audit.Events.Should().BeEmpty();
        }
    }
}
