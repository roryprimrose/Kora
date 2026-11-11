using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class RegisteredSettingSuggestionCollectionTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public RegisteredSettingSuggestionCollectionTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void ModelPolicyRequiresWholeCollectionValidationAndIndependentMemberConfirmation()
    {
        RegisteredSettingSuggestions.ModelPolicy.Should().Contain("2 through 8")
            .And.Contain("unique exact IDs").And.Contain("one member")
            .And.Contain("separate confirmation").And.Contain("No approval for all");
    }

    [Theory]
    [InlineData("set appearance.theme to dark\nset speech.summary-words to 40")]
    [InlineData("reset appearance.theme\nreset speech.summary-words")]
    [InlineData("set speech.playback-volume to 0\nset responses.default-mode to VisualOnly")]
    [InlineData("set speech.windows-rate to -1\nset speech.voice to windows-sapi / exact installed ID")]
    [InlineData("set voice.trailing-silence to 0.5\nset notices.quiet-hours to Off")]
    [InlineData("set responses.detail-presentation to LinkOnly\nset work.active-deadline-minutes to 10")]
    [InlineData("set appearance.theme to dark\nset appearance.reader-text-scale to 125\nset speech.summary-words to 40\nset speech.summary-sentences to 2\nset speech.playback-volume to 0\nset responses.default-mode to VisualOnly\nset appearance.presence-timeout to 10\nreset appearance.reduced-motion")]
    public void CompleteCanonicalAnswerPreservesOrderAndRequiresAnExplicitMember(string answer)
    {
        var collection = RegisteredSettingSuggestionCollection.Parse(answer);
        collection.Members.Should().HaveCount(answer.Split('\n').Length);
        collection.Count.Should().Be(collection.Members.Count);
        collection.ToArray().Should().Equal(collection.Members);
        ((System.Collections.IEnumerable)collection).Cast<RegisteredSettingProposal>().Should().Equal(collection.Members);
        var untyped = ((System.Collections.IEnumerable)collection).GetEnumerator();
        untyped.MoveNext().Should().BeTrue();
        untyped.Current.Should().Be(collection.Members[0]);
        for (var index = 0; index < collection.Members.Count; index++)
        {
            var proposal = collection.Members[index];
            proposal.Should().Be(RegisteredSettingSuggestions.Parse(answer.Split('\n')[index]));
            RegisteredSettingSuggestionCollection.ParseSelected(answer, proposal.Setting).Should().Be(proposal);
        }
        var rejection = FluentActions.Invoking(() => RegisteredSettingSuggestionCollection.ParseSelected(answer, null))
            .Should().Throw<Exception>("legacy single-command routes must not select a collection member")
            .Which;
        (rejection is InvalidDataException or ArgumentException).Should().BeTrue();
        FluentActions.Invoking(() => ((IList<RegisteredSettingProposal>)collection.Members).Clear())
            .Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("set appearance.theme to dark\n")]
    [InlineData("\nset appearance.theme to dark")]
    [InlineData("set appearance.theme to dark\n\nreset speech.playback-volume")]
    [InlineData("set appearance.theme to dark\nset unknown.setting to true")]
    [InlineData("set appearance.theme to dark\nset appearance.theme to light")]
    [InlineData("set appearance.theme to dark\nreset appearance.theme")]
    [InlineData("set appearance.theme to dark\nconfirm appearance change")]
    [InlineData("set appearance.theme to dark\nSome commentary.")]
    [InlineData("Here are settings:\nset appearance.theme to dark")]
    [InlineData("```text\nset appearance.theme to dark\n```")]
    [InlineData("1. set appearance.theme to dark\n2. set speech.playback-volume to 0")]
    [InlineData("set appearance.theme to dark\r\nset speech.playback-volume to 0")]
    [InlineData("Set appearance.theme to dark\nset speech.playback-volume to 0")]
    [InlineData("set appearance.theme to Dark\nset speech.playback-volume to 0")]
    [InlineData("set speech.summary-words to 40 \nset speech.playback-volume to 0")]
    [InlineData("Set speech.summary-words to 40\nset speech.playback-volume to 0")]
    [InlineData("set speech.playback-volume to 0\nset speech.voice to invented / voice")]
    [InlineData("set speech.playback-volume to 0\nreset speech.voice")]
    public void AnyMalformedDuplicateUnknownOrNoncanonicalMemberRejectsTheWholeAnswer(string answer) =>
        FluentActions.Invoking(() => RegisteredSettingSuggestionCollection.Parse(answer)).Should().Throw<InvalidDataException>();

    [Fact]
    public void NullOversizeCountAndForeignSelectionFailWithoutPartialResults()
    {
        FluentActions.Invoking(() => RegisteredSettingSuggestionCollection.Parse(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => RegisteredSettingSuggestionCollection.Parse(new string('x', 3073))).Should().Throw<InvalidDataException>();
        var nine = string.Join('\n', Enumerable.Repeat("reset appearance.theme", 9));
        FluentActions.Invoking(() => RegisteredSettingSuggestionCollection.Parse(nine)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => RegisteredSettingSuggestionCollection.ParseSelected("reset appearance.theme", RegisteredSetting.PlaybackVolume))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => RegisteredSettingSuggestionCollection.ParseSelected("reset appearance.theme", (RegisteredSetting)int.MaxValue))
            .Should().Throw<InvalidDataException>();
        RegisteredSettingSuggestionCollection.MaximumMembers.Should().Be(8);
        RegisteredSettingSuggestionCollection.MaximumAnswerLength.Should().Be(3072);
    }

    [Theory]
    [InlineData("set appearance.theme to dark")]
    [InlineData("reset speech.playback-volume")]
    [InlineData("Set speech.summary-words to 40")]
    public void ExistingSingleCommandCompatibilityIsUnchanged(string answer)
    {
        var expected = RegisteredSettingSuggestions.Parse(answer);
        RegisteredSettingSuggestionCollection.Parse(answer).Members.Should().Equal(expected);
        RegisteredSettingSuggestionCollection.ParseSelected(answer, null).Should().Be(expected);
    }

    [Fact]
    public async Task OriginalCollectionNeedsIndependentReviewAndFreshConfirmationForEachExistingWriter()
    {
        await using var deadline = new OrdinaryDeadlineConfigurationFixture();
        await using var details = new DetailPresentationConfigurationServiceTests.Fixture();
        var message = QuietHoursSuggestionReviewTests.Answer(
            "set work.active-deadline-minutes to 10\nset responses.detail-presentation to LinkOnly\nset speech.playback-volume to 0");
        var token = TestContext.Current.CancellationToken;
        var validations = 0;
        Task Validate(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            message.Validate();
            validations++;
            return Task.CompletedTask;
        }
        OrdinaryTurnDeadlineSuggestionReview first;
        using (QuietHoursSuggestionReviewTests.Input(message))
        {
            first = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(deadline.Service, deadline.CallPolicy, TimeProvider.System,
                message, () => true, Validate, token, RegisteredSetting.OrdinaryTurnDeadline);
            first.Description.Should().Contain("Current saved: default five minutes").And.Contain("Proposed: 10 minutes")
                .And.Contain("Default: five minutes").And.Contain("shared device-local");
            deadline.Preferences.Load().Should().BeNull();
            details.Preferences.Writes.Should().Be(0);
            await FluentActions.Awaiting(() => first.ConfirmAsync(token)).Should().ThrowAsync<InvalidOperationException>();
        }
        using (first)
        using (QuietHoursSuggestionReviewTests.Input(message))
        { (await first.ConfirmAsync(token)).Should().BeTrue(); }
        deadline.Preferences.Load()!.Value.Minutes.Should().Be(10);
        details.Preferences.Writes.Should().Be(0, "one confirmation must not authorize any other member or writer");
        details.Audit.Events.Should().BeEmpty();
        DetailPresentationSuggestionReview second;
        using (QuietHoursSuggestionReviewTests.Input(message))
        {
            second = await DetailPresentationSuggestionReview.ReviewAsync(details.Service, details.Policy, details.Time,
                message, () => true, Validate, token, RegisteredSetting.DetailPresentation);
            second.Description.Should().Contain("Offer (unsaved default) -> LinkOnly").And.Contain("Nothing has changed");
            details.Preferences.Writes.Should().Be(0);
            await FluentActions.Awaiting(() => second.ConfirmAsync(token)).Should().ThrowAsync<InvalidOperationException>();
        }
        using (second)
        using (QuietHoursSuggestionReviewTests.Input(message))
        { (await second.ConfirmAsync(token)).Should().BeTrue(); }
        details.Preferences.Value.Should().Be(DetailPresentationMode.LinkOnly);
        details.Preferences.Pending.Should().BeFalse();
        deadline.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        details.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        validations.Should().Be(6);
        first.Available.Should().BeFalse();
        second.Available.Should().BeFalse();
        message.AssistantResponse!.Body.Should().Contain("set speech.playback-volume to 0", "the original complete source was not rewritten to the chosen command");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("commentary")]
    [InlineData("oversize")]
    [InlineData("foreign")]
    public async Task InvalidWholeCollectionOrForeignMemberCannotReachTheExistingWriter(string invalid)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var answer = "set work.active-deadline-minutes to 10\n" + (invalid switch
        {
            "unknown" => "reset unknown.setting",
            "duplicate" => "reset work.active-deadline-minutes",
            "commentary" => "Confirm all now.",
            "oversize" => new string('x', 3073),
            _ => "reset appearance.theme",
        });
        var message = QuietHoursSuggestionReviewTests.Answer(answer);
        using (QuietHoursSuggestionReviewTests.Input(message))
        {
            await FluentActions.Awaiting(() => OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, TimeProvider.System,
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken,
                invalid is "foreign" ? RegisteredSetting.PlaybackVolume : RegisteredSetting.OrdinaryTurnDeadline))
                .Should().ThrowAsync<InvalidDataException>();
        }
        f.Store.Values.Should().BeEmpty();
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("clock-back")]
    [InlineData("source")]
    [InlineData("source-return")]
    [InlineData("source-await")]
    [InlineData("review-await")]
    [InlineData("voice-source")]
    [InlineData("voice-confirmation")]
    public async Task SelectedMemberRetainsOriginalSourceChannelExpiryAndDelayedValidation(string transition)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var original = QuietHoursSuggestionReviewTests.Answer(
            "set work.active-deadline-minutes to 10\nreset appearance.theme");
        var message = original;
        if (transition is "voice-source")
        {
            var request = new HostRequest(original.Request.RequestId, original.Request.SessionId, original.Request.TaskId, RequestOrigin.ActivatedVoice);
            var execution = new ModelTurnProvenance(original.Execution!.Turn, request, original.Execution.Selection);
            message = original with { Request = request, Execution = execution, AssistantResponse = new(execution, original.AssistantResponse!.Body) };
        }
        var token = TestContext.Current.CancellationToken;
        var live = true;
        var confirming = false;
        var time = new CollectionTime();
        Task Validate(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (transition is "review-await" || confirming && transition is "source-await") { live = false; }
            return Task.CompletedTask;
        }
        OrdinaryTurnDeadlineSuggestionReview review;
        using (QuietHoursSuggestionReviewTests.Input(message))
        {
            if (transition is "voice-source" or "review-await")
            {
                await FluentActions.Awaiting(() => OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, time,
                    message, () => live, Validate, token, RegisteredSetting.OrdinaryTurnDeadline)).Should().ThrowAsync<InvalidOperationException>();
                f.Store.Values.Should().BeEmpty();
                f.Audit.Events.Should().BeEmpty();
                return;
            }
            review = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, time,
                message, () => live, Validate, token, RegisteredSetting.OrdinaryTurnDeadline);
        }
        using (review)
        {
            if (transition is "expiry") { time.Now = time.Now.AddSeconds(60); }
            if (transition is "clock-back") { time.Now = time.Now.AddSeconds(-1); }
            if (transition is "source" or "source-return") { live = false; }
            if (transition is "source-return") { review.Available.Should().BeFalse(); live = true; }
            confirming = true;
            using var input = HostActivity.BeginRoot(
                new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()),
                    transition is "voice-confirmation" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            await FluentActions.Awaiting(() => review.ConfirmAsync(token)).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Store.Values.Should().BeEmpty();
        f.Audit.Events.Should().BeEmpty();
        review.Available.Should().BeFalse();
    }

    private sealed class CollectionTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
