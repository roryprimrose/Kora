using System.Diagnostics;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class AppearanceConfigurationServiceTests
{
    [Fact]
    public async Task RegisteredAppearanceSuggestionCannotRelabelAnOriginalSystemSource()
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion(origin: RequestOrigin.HostSystem);
        using var input = BeginSuggestionInput(SuggestionInput(message.Request));
        await FluentActions.Awaiting(() => AppearanceSuggestionReview.ReviewAsync(fixture.Service, message,
            () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredAppearanceSuggestionCannotReuseOriginalModelRequestAsConfirmation()
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion();
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSuggestionInput(message.Request))
        {
            await FluentActions.Awaiting(() => review.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue("a rejected input is not a confirmation");
        }
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    private static ActivityListener ListenToSuggestions()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static HostRequest SuggestionInput(HostRequest original, RequestOrigin origin = RequestOrigin.LocalUi) =>
        new(new(Guid.NewGuid()), original.SessionId, new(Guid.NewGuid()), origin);

    private static ConversationMessage Suggestion(string text = "set appearance.theme to dark",
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = HostRequest.Create(origin);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UtcNow, "Suggest a more readable appearance.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, text),
        };
    }

    private static HostActivity BeginSuggestionInput(HostRequest request) =>
        HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);

    [Fact]
    public async Task SuggestionReviewShowsSavedCurrentValueWithoutWriting()
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        fixture.Store.Text["appearance-theme.txt"] = "Light";
        var message = Suggestion();
        using var input = BeginSuggestionInput(SuggestionInput(message.Request));
        using var review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message,
            () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        review.Description.Should().Contain("current light (saved); proposed dark mode");
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuggestionReviewRechecksEligibilityAndExactIssuingRequestAfterSourceRead(bool replaceRequest)
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion();
        var read = false;
        HostActivity? replacement = null;
        ExecutionContext? replacementContext = null;
        bool Eligible()
        {
            if (!read) { return true; }
            if (!replaceRequest) { return false; }
            replacement = BeginSuggestionInput(SuggestionInput(message.Request));
            replacementContext = ExecutionContext.Capture();
            return true;
        }
        using var input = BeginSuggestionInput(SuggestionInput(message.Request));
        try
        {
            await FluentActions.Awaiting(() => AppearanceSuggestionReview.ReviewAsync(fixture.Service, message,
                Eligible, _ => { read = true; return Task.CompletedTask; }, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The original appearance review input changed.");
            fixture.Store.Writes.Should().Be(0);
            fixture.Audit.Events.Should().BeEmpty();
        }
        finally
        {
            // Retire the hostile callback's scope in the exact execution context that created it.
            if (replacementContext is not null)
            {
                ExecutionContext.Run(replacementContext, _ => replacement!.Dispose(), null);
            }
        }
    }

    [Fact]
    public async Task SuggestionReviewRejectsInvocationScopedInputEvenInOriginalSession()
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion();
        using var input = BeginSuggestionInput(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid())));
        await FluentActions.Awaiting(() => AppearanceSuggestionReview.ReviewAsync(fixture.Service, message,
            () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("set appearance.theme to dark", "current system (default); proposed dark mode", "appearance-theme.txt", "Dark")]
    [InlineData("set appearance.reduced-motion to true", "current false (default); proposed true boolean", "appearance-reduced-motion-enabled.txt", "True")]
    [InlineData("set appearance.reader-text-scale to 125", "current 100 (default); proposed 125 percent", "reader-text-scale-percent.txt", "125")]
    public async Task SuggestionReviewIsInertUntilSeparateOriginalUserConfirmation(string text, string description,
        string file, string saved)
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion(text);
        var reads = 0;
        Task Validate(CancellationToken _) { reads++; return Task.CompletedTask; }
        AppearanceSuggestionReview review;
        using (var input = BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message, () => true, Validate,
                TestContext.Current.CancellationToken);
            review.Description.Should().Contain(description).And.Contain("shared device-local");
            fixture.Store.Writes.Should().Be(0);
            fixture.Audit.Events.Should().BeEmpty();
            await review.Invoking(value => value.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
            input.Complete(HostOperationOutcome.Completed);
        }
        using (var input = BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            var result = await review.ConfirmAsync(TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(AppearanceApplyOutcome.Applied);
            fixture.Store.Text.Should().ContainKey(file).WhoseValue.Should().Be(saved);
            fixture.Audit.Events.Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.LocalUser);
            fixture.Audit.Events.Select(item => item.Outcome).Should()
                .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            review.Description.Should().BeEmpty();
            review.Available.Should().BeFalse();
            await review.Invoking(value => value.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            input.Complete(HostOperationOutcome.Completed);
        }
        reads.Should().Be(2);
    }

    [Theory]
    [InlineData("set appearance.theme to ultraviolet")]
    [InlineData("set appearance.reader-text-scale to 999")]
    [InlineData("set appearance.reader-text-scale to 1.25")]
    [InlineData("set appearance.response-timeout to 10")]
    [InlineData("set speech.summary-words to 40")]
    [InlineData("reset appearance.theme")]
    [InlineData("set appearance.theme to dark\nand do something else")]
    [InlineData("I changed your theme to dark.")]
    public async Task SuggestionReviewRejectsInvalidOrOutOfScopeAnswerWithoutAnyWrite(string text)
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion(text);
        using var input = BeginSuggestionInput(SuggestionInput(message.Request));
        await FluentActions.Awaiting(() => AppearanceSuggestionReview.ReviewAsync(fixture.Service, message,
            () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("voice")]
    [InlineData("missing")]
    [InlineData("closed")]
    [InlineData("stopped-current")]
    [InlineData("cancelled")]
    [InlineData("unavailable")]
    [InlineData("source")]
    [InlineData("original-request")]
    public async Task SuggestionReviewFailsClosedWithoutExactAdmittedOriginalSource(string fault)
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion(origin: fault is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        if (fault is "unavailable")
        {
            message = message with { Outcome = ModelTurnOutcome.Unavailable, AssistantResponse = null };
        }
        using var input = fault is "missing" ? null : BeginSuggestionInput(fault switch
        {
            "foreign" => HostRequest.Create(RequestOrigin.LocalUi),
            "original-request" => message.Request,
            _ => SuggestionInput(message.Request),
        });
        if (fault is "closed") { input!.Complete(HostOperationOutcome.Completed); }
        if (fault is "stopped-current")
        {
            await Task.Run(() => input!.Activity!.Stop(), TestContext.Current.CancellationToken);
        }
        var token = fault is "cancelled" ? new CancellationToken(true) : TestContext.Current.CancellationToken;
        await FluentActions.Awaiting(() => AppearanceSuggestionReview.ReviewAsync(fixture.Service, message,
            () => fault is not "source", _ => Task.CompletedTask, token)).Should().ThrowAsync<Exception>();
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel", "set appearance.theme to dark")]
    [InlineData("privacy", "set appearance.theme to dark")]
    [InlineData("foreign", "set appearance.theme to dark")]
    [InlineData("voice", "set appearance.theme to dark")]
    [InlineData("source", "set appearance.theme to dark")]
    [InlineData("cancelled", "set appearance.theme to dark")]
    [InlineData("during-validation", "set appearance.theme to dark")]
    [InlineData("cancel", "set appearance.reduced-motion to true")]
    [InlineData("privacy", "set appearance.reduced-motion to true")]
    [InlineData("foreign", "set appearance.reduced-motion to true")]
    [InlineData("voice", "set appearance.reduced-motion to true")]
    [InlineData("source", "set appearance.reduced-motion to true")]
    [InlineData("cancelled", "set appearance.reduced-motion to true")]
    [InlineData("during-validation", "set appearance.reduced-motion to true")]
    public async Task SuggestionConfirmationRevokesOnLifecycleOrAdmissionChange(string fault, string text)
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion(text);
        var eligible = true;
        var failRead = false;
        AppearanceSuggestionReview? review = null;
        Task Validate(CancellationToken _)
        {
            if (failRead)
            {
                if (fault is "during-validation") { review!.Dispose(); }
                else { throw new InvalidOperationException("Exact source changed."); }
            }
            return Task.CompletedTask;
        }
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message, () => eligible,
                Validate, TestContext.Current.CancellationToken);
        }
        using (review)
        {
            if (fault is "cancel") { review.Dispose(); }
            if (fault is "privacy") { eligible = false; }
            failRead = fault is "source" or "during-validation";
            using var input = BeginSuggestionInput(fault is "foreign" ? HostRequest.Create(RequestOrigin.LocalUi)
                : SuggestionInput(message.Request, fault is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));
            var token = fault is "cancelled" ? new CancellationToken(true) : TestContext.Current.CancellationToken;
            await review.Invoking(value => value.ConfirmAsync(token)).Should().ThrowAsync<Exception>();
            fixture.Store.Writes.Should().Be(0);
            fixture.Audit.Events.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SuggestionConfirmationKeepsReviewedRevisionAndDoesNotOverwriteNewerSettings()
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion();
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        fixture.Store.Text["appearance-theme.txt"] = "Light";
        fixture.Service.Reload();
        review.Available.Should().BeFalse("the exact reviewed revision is no longer current");
        using (review)
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            var result = await review.ConfirmAsync(TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(AppearanceApplyOutcome.Stale);
            fixture.Store.Text["appearance-theme.txt"].Should().Be("Light");
            fixture.Store.Writes.Should().Be(0);
            review.Description.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuggestionConfirmationDoesNotClaimSuccessOnPersistenceOrAuditFailure(bool audit)
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion();
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        if (audit) { fixture.Audit.BeforeRequested = () => throw new IOException("Audit unavailable."); }
        else { fixture.Store.Failure = new IOException("Write unavailable."); }
        using (review)
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            if (audit)
            {
                await review.Invoking(value => value.ConfirmAsync(TestContext.Current.CancellationToken))
                    .Should().ThrowAsync<IOException>();
            }
            else
            {
                (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Outcome.Should().Be(AppearanceApplyOutcome.Failed);
            }
            fixture.Store.Writes.Should().Be(0);
            review.Available.Should().BeFalse();
            fixture.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuggestionConfirmationSurfacesTerminalAuditFailureWithoutFabricatingRollback(bool reader)
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion(reader ? "set appearance.reader-text-scale to 125" : "set appearance.theme to dark");
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        fixture.Audit.BeforeSuccess = () => throw new IOException("Terminal audit unavailable.");
        using (review)
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            await review.Invoking(value => value.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<IOException>();
            fixture.Store.Text.Should().ContainKey(reader ? "reader-text-scale-percent.txt" : "appearance-theme.txt");
            review.Available.Should().BeFalse();
            review.Description.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SuggestionConfirmationPreservesFreshHostTraceAndRechecksAfterRequestedAudit()
    {
        using var listener = ListenToSuggestions();
        var fixture = new Fixture();
        var message = Suggestion();
        var eligible = true;
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(fixture.Service, message, () => eligible,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        Activity? policy = null;
        Activity? storage = null;
        fixture.Audit.BeforeRequested = () =>
        {
            storage = Activity.Current;
            eligible = false;
        };
        using (review)
        using (var input = BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            using var captured = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (activity.ParentSpanId == input.Activity!.SpanId) { policy = activity; }
                },
            };
            ActivitySource.AddActivityListener(captured);
            await review.Invoking(value => value.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            policy.Should().NotBeNull();
            storage.Should().NotBeNull();
            policy!.TraceId.Should().Be(input.Activity!.TraceId);
            storage!.TraceId.Should().Be(input.Activity.TraceId);
            storage.ParentSpanId.Should().Be(policy.SpanId);
            policy.Status.Should().Be(ActivityStatusCode.Error);
            storage.Status.Should().Be(ActivityStatusCode.Error);
            storage.TagObjects.Should().NotContain(tag => Equals(tag.Value, message.Body)
                || Equals(tag.Value, message.AssistantResponse!.Body));
            fixture.Store.Writes.Should().Be(0);
            fixture.Audit.Events.Select(item => item.Outcome).Should()
                .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
        }
    }
}
