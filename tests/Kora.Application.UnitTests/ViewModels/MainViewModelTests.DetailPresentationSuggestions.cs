using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("Offer")]
    [InlineData("LinkOnly")]
    [InlineData("OpenAutomatically")]
    [InlineData(null)]
    public async Task GenuineRetainedDetailsAdviceCompletesNativeReviewSeparateConfirmationAndActualAtomicFileReadback(string? value)
    {
        var root = Path.Combine(Path.GetTempPath(), "Kora.DetailSuggestionTests." + Guid.NewGuid().ToString("N"));
        try
        {
            await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
            var paths = new DetailSuggestionPaths(root);
            var preferences = new LocalDetailPresentationPreferences(paths);
            preferences.Save(DetailPresentationMode.LinkOnly);
            var service = new DetailPresentationConfigurationService(preferences, settings.Admission, settings.Audit, TimeProvider.System);
            var f = new Fixture(detailPresentationConfiguration: service);
            await using var main = f.ViewModel;
            await main.InitializeAsync();
            var (store, adapter) = BindSpeechSuggestions(f);
            adapter.Answer = value is null ? "reset responses.detail-presentation" : "set responses.detail-presentation to " + value;
            await SubmitSpeechSuggestion(f, store, voice: false);
            var message = store.RequestMessage!;
            var validations = 0;
            DetailPresentationSuggestionReview review;
            using (DetailPresentationSuggestionReviewTests.Input(message))
            {
                review = await main.ReviewDetailPresentationSuggestionAsync(message, static () => true,
                    _ => { validations++; store.RequestMessage.Should().BeSameAs(message); return Task.CompletedTask; },
                    TestContext.Current.CancellationToken);
                review.Description.Should().Contain("LinkOnly -> " + (value ?? "Offer"));
                new LocalDetailPresentationPreferences(paths).Load().Should().Be(DetailPresentationMode.LinkOnly);
                settings.Audit.Events.Should().BeEmpty();
            }
            using (review)
            using (DetailPresentationSuggestionReviewTests.Input(message))
            { (await main.ConfirmDetailPresentationSuggestionAsync(review, TestContext.Current.CancellationToken)).Should().BeTrue(); }
            var expected = DetailPresentationPreference.Parse(value) ?? DetailPresentationMode.Offer;
            new LocalDetailPresentationPreferences(paths).Load().Should().Be(expected);
            service.Description.Should().Contain(expected + " (saved)");
            validations.Should().Be(3);
            settings.Audit.Events.Select(item => item.Outcome).Should().Equal(
                Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
            Directory.GetFiles(Path.Combine(root, "Preferences")).Should().ContainSingle()
                .Which.Should().EndWith("detail-presentation.txt");
            adapter.Calls.Should().Be(1);
            store.RequestMessage.Should().BeSameAs(message);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpeakCalls.Should().Be(0);
            f.Voice.StartCalls.Should().Be(0);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    private sealed record DetailSuggestionPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }

    [Theory]
    [InlineData("save")]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("name")]
    [InlineData("call")]
    [InlineData("dispose")]
    public async Task RetainedDetailsAdviceUsesNativeOwnerAndRejectsChangedHostBindingsWithoutOpeningOrSpeech(string transition)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        settings.Time.Now = DateTimeOffset.UtcNow;
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        await using var main = f.ViewModel;
        await main.InitializeAsync();
        var (store, adapter) = BindSpeechSuggestions(f);
        adapter.Answer = "set responses.detail-presentation to OpenAutomatically";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        var current = true;
        var starts = f.Voice.StartCalls;
        DetailPresentationSuggestionReview review;
        using (DetailPresentationSuggestionReviewTests.Input(message))
        {
            review = await main.ReviewDetailPresentationSuggestionAsync(message, () => current, _ => Task.CompletedTask,
                TestContext.Current.CancellationToken);
        }
        settings.Preferences.Writes.Should().Be(0);
        using (review)
        {
            switch (transition)
            {
                case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; break;
                case "source": current = false; break;
                case "owner": main.BindCallOwnershipGate(static () => false); break;
                case "name": await main.SetAssistantNameAsync("Nova"); break;
                case "call": f.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
                case "dispose": main.Dispose(); break;
            }
            using var input = DetailPresentationSuggestionReviewTests.Input(message);
            if (transition is "save")
            {
                (await main.ConfirmDetailPresentationSuggestionAsync(review, TestContext.Current.CancellationToken)).Should().BeTrue();
                settings.Preferences.Value.Should().Be(DetailPresentationMode.OpenAutomatically);
                main.DetailPresentationStatus.Should().Contain("OpenAutomatically (saved)");
            }
            else
            {
                await main.Invoking(vm => vm.ConfirmDetailPresentationSuggestionAsync(review, TestContext.Current.CancellationToken))
                    .Should().ThrowAsync<InvalidOperationException>();
                settings.Preferences.Writes.Should().Be(0);
            }
        }
        f.Voice.StartCalls.Should().Be(starts);
        adapter.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailsSuggestionWorkRejectsOverlapAndDisposalJoinsAwaitedSourceCheck(bool confirming)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        settings.Time.Now = DateTimeOffset.UtcNow;
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        var main = f.ViewModel;
        await main.InitializeAsync();
        var (store, adapter) = BindSpeechSuggestions(f);
        adapter.Answer = "set responses.detail-presentation to LinkOnly";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        using var input = DetailPresentationSuggestionReviewTests.Input(message);
        var waiting = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Validate(CancellationToken _)
        {
            if (waiting) { entered.TrySetResult(); await release.Task; }
        }
        using var review = await main.ReviewDetailPresentationSuggestionAsync(message, static () => true, Validate,
            TestContext.Current.CancellationToken);
        using var confirmation = DetailPresentationSuggestionReviewTests.Input(message);
        waiting = true;
        Task work = confirming ? main.ConfirmDetailPresentationSuggestionAsync(review, TestContext.Current.CancellationToken)
            : main.ReviewDetailPresentationSuggestionAsync(message, static () => true, Validate, TestContext.Current.CancellationToken);
        await entered.Task;
        await main.Invoking(vm => vm.ReviewDetailPresentationSuggestionAsync(message, static () => true, Validate,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await main.Invoking(vm => vm.ConfirmDetailPresentationSuggestionAsync(review, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        main.BindDetailPresentationNativeLifetime(static () => true);
        await main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
        main.Transcript.Should().Contain("unavailable");
        var disposal = main.DisposeAsync().AsTask();
        work.IsCompleted.Should().BeFalse();
        disposal.IsCompleted.Should().BeFalse();
        release.SetResult();
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        await disposal;
        settings.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task DetailsSuggestionsRequireAvailableMatchingOwnerWithoutSpeechBinding()
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        await using var foreignSettings = new DetailPresentationConfigurationServiceTests.Fixture();
        settings.Time.Now = DateTimeOffset.UtcNow;
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        var foreign = new Fixture(detailPresentationConfiguration: foreignSettings.Service);
        var unavailable = new Fixture();
        await using var main = f.ViewModel;
        await using var foreignMain = foreign.ViewModel;
        await using var unavailableMain = unavailable.ViewModel;
        await main.InitializeAsync();
        await foreignMain.InitializeAsync();
        await unavailableMain.InitializeAsync();
        var message = DetailPresentationSuggestionReviewTests.Answer("reset responses.detail-presentation");
        using var input = DetailPresentationSuggestionReviewTests.Input(message);
        await unavailableMain.Invoking(vm => vm.ReviewDetailPresentationSuggestionAsync(message, static () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        using var review = await main.ReviewDetailPresentationSuggestionAsync(message, static () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        await foreignMain.Invoking(vm => vm.ConfirmDetailPresentationSuggestionAsync(review, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await unavailableMain.Invoking(vm => vm.ConfirmDetailPresentationSuggestionAsync(review, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => main.ConfirmDetailPresentationSuggestionAsync(null!, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        main.Dispose();
        await main.Invoking(vm => vm.ReviewDetailPresentationSuggestionAsync(message, static () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
    }
}
