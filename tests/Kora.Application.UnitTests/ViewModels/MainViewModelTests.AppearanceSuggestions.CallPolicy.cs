using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(CallState.Active, AppearanceOption.Theme, "dark")]
    [InlineData(CallState.Unknown, AppearanceOption.Theme, "dark")]
    [InlineData(CallState.Active, AppearanceOption.ReaderTextScale, "125")]
    [InlineData(CallState.Unknown, AppearanceOption.ReaderTextScale, "125")]
    [InlineData(CallState.Active, AppearanceOption.ReducedMotion, "true")]
    [InlineData(CallState.Unknown, AppearanceOption.ReducedMotion, "true")]
    [InlineData(CallState.Active, AppearanceOption.ResponseTimeout, "15")]
    [InlineData(CallState.Unknown, AppearanceOption.ResponseTimeout, "15")]
    public async Task NativeAppearanceSuggestionDuringProtectedCallCompletesExactAuditedPreference(
        CallState call, AppearanceOption option, string value)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            f.CallState.SetState(call);
            await f.Dispatcher.LastInvocation;
            f.ViewModel.IsProtectedCall.Should().BeTrue();
            var descriptor = AppearanceOptionRegistry.Get(option);
            var message = AppearanceSuggestionMessage();
            message = message with { AssistantResponse = message.AssistantResponse! with
                { Body = $"set {descriptor.Id} to {value}" } };
            var restored = new AppearanceConfigurationService(f.AppearancePreferences, f.Audit,
                NullLogger<AppearanceConfigurationService>.Instance);
            var auditCount = f.Audit.Events.Count;
            var validations = 0;
            Task Validate(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                validations++;
                return Task.CompletedTask;
            }
            AppearanceSuggestionReview review;
            using (AppearanceSuggestionInput(message))
            {
                review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true,
                    Validate, TestContext.Current.CancellationToken);
                review.Available.Should().BeTrue();
                review.Description.Should().Contain($"proposed {value}").And.Contain("shared device-local");
                restored.Get(option).IsSaved.Should().BeFalse();
                f.Audit.Events.Should().HaveCount(auditCount);
            }
            using (review)
            using (AppearanceSuggestionInput(message))
            {
                var result = await f.ViewModel.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken);
                result.Outcome.Should().Be(AppearanceApplyOutcome.Applied);
                AppearanceCommand.Format(result.State.Value).Should().Be(value);
                result.State.IsSaved.Should().BeTrue();
                review.Available.Should().BeFalse();
            }
            restored.Reload();
            foreach (var item in AppearanceOptionRegistry.Options)
            {
                var saved = restored.Get(item.Option);
                saved.IsSaved.Should().Be(item.Option == option);
                AppearanceCommand.Format(saved.Value).Should().Be(
                    item.Option == option ? value : AppearanceCommand.Format(item.Default));
            }
            validations.Should().Be(2);
            f.Audit.Events.Skip(auditCount).Select(item => item.Outcome).Should()
                .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Audit.Events.Skip(auditCount).Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.LocalUser);
            f.ViewModel.IsProtectedCall.Should().BeTrue();
            f.CallState.CurrentState.Should().Be(call);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Theory]
    [InlineData("voice-source")]
    [InlineData("voice-review")]
    [InlineData("voice-timeout-source")]
    [InlineData("voice-timeout-review")]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("private")]
    [InlineData("call-revision")]
    [InlineData("cancel")]
    public async Task NativeAppearanceSuggestionDuringProtectedCallRetainsOriginalAdmission(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            f.CallState.SetState(CallState.Active);
            await f.Dispatcher.LastInvocation;
            var message = AppearanceSuggestionMessage(origin: loss is "voice-source" or "voice-timeout-source"
                ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            if (loss is "voice-timeout-source" or "voice-timeout-review")
            {
                message = message with { AssistantResponse = message.AssistantResponse! with
                    { Body = "set appearance.response-timeout to 15" } };
            }
            var auditCount = f.Audit.Events.Count;
            var current = true;
            AppearanceSuggestionReview? review = null;
            using (HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()),
                loss is "voice-review" or "voice-timeout-review" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Request))
            {
                if (loss is "voice-source" or "voice-review" or "voice-timeout-source" or "voice-timeout-review")
                {
                    await f.ViewModel.Invoking(model => model.ReviewAppearanceSuggestionAsync(message, () => current,
                        _ => Task.CompletedTask, TestContext.Current.CancellationToken))
                        .Should().ThrowAsync<InvalidOperationException>();
                }
                else
                {
                    review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => current,
                        _ => Task.CompletedTask, TestContext.Current.CancellationToken);
                }
            }
            using (review)
            {
                if (review is not null)
                {
                    switch (loss)
                    {
                        case "source": current = false; break;
                        case "owner": f.ViewModel.BindCallOwnershipGate(() => false); break;
                        case "private": f.Session.IsUnlocked = false; break;
                        case "call-revision":
                            f.CallState.SetState(CallState.Clear);
                            await f.Dispatcher.LastInvocation;
                            f.CallState.SetState(CallState.Active);
                            await f.Dispatcher.LastInvocation;
                            break;
                        case "cancel": review.Dispose(); break;
                    }
                    using (AppearanceSuggestionInput(message))
                    {
                        review.Available.Should().BeFalse();
                        await f.ViewModel.Invoking(model => model.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken))
                            .Should().ThrowAsync<InvalidOperationException>();
                    }
                }
            }
            f.AppearancePreferences.SavedMode.Should().BeNull();
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().BeNull();
            f.Audit.Events.Should().HaveCount(auditCount);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task NativeAppearanceSuggestionDuringProtectedCallResetUsesExistingDefaultAndAudit(CallState call)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            await f.RunAsync("set appearance.theme to dark");
            f.CallState.SetState(call);
            await f.Dispatcher.LastInvocation;
            var message = AppearanceSuggestionMessage();
            message = message with { AssistantResponse = message.AssistantResponse! with { Body = "reset appearance.theme" } };
            var auditCount = f.Audit.Events.Count;
            AppearanceSuggestionReview review;
            using (AppearanceSuggestionInput(message))
            {
                review = await f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true,
                    _ => Task.CompletedTask, TestContext.Current.CancellationToken);
                review.Description.Should().Contain("current dark (saved)").And.Contain("proposed system");
                f.AppearancePreferences.SavedMode.Should().Be(ApplicationThemeMode.Dark);
                f.Audit.Events.Should().HaveCount(auditCount);
            }
            using (review)
            using (AppearanceSuggestionInput(message))
            {
                var result = await f.ViewModel.ConfirmAppearanceSuggestionAsync(review, TestContext.Current.CancellationToken);
                result.Outcome.Should().Be(AppearanceApplyOutcome.Applied);
                result.State.IsSaved.Should().BeTrue();
            }
            f.AppearancePreferences.SavedMode.Should().Be(ApplicationThemeMode.System);
            f.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
            f.ViewModel.IsProtectedCall.Should().BeTrue();
            f.Audit.Events.Skip(auditCount).Select(item => item.Outcome).Should()
                .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }
}
