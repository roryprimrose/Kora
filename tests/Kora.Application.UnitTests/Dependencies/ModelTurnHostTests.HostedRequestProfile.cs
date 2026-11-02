using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(ProviderService.Copilot, RequestOrigin.LocalUi, true)]
    [InlineData(ProviderService.Copilot, RequestOrigin.ActivatedVoice, true)]
    [InlineData(ProviderService.Claude, RequestOrigin.LocalUi, true)]
    [InlineData(ProviderService.Claude, RequestOrigin.ActivatedVoice, true)]
    [InlineData(ProviderService.Copilot, RequestOrigin.LocalUi, false)]
    [InlineData(ProviderService.Claude, RequestOrigin.ActivatedVoice, false)]
    public async Task HostedRequestProfileUsesOnlyReviewedTupleAndNextRequestInheritsWithoutWrites(
        ProviderService service, RequestOrigin origin, bool approve)
    {
        using var f = new Fixture(origin);
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var copilot = new HostedRuntime();
        var claude = new ClaudeRuntime();
        var host = service == ProviderService.Copilot ? f.CopilotProfiles(copilot, reviews) : f.ClaudeProfiles(claude, reviews);
        f.ProfileCatalogue.FieldCatalogue = true;
        var app = f.ProfilePreferences.Value!.Serialize();
        var policy = f.SessionOverrides.Serialize();
        var chosen = Profile(service) with { Reasoning = new("exact-effort"), Context = new("exact-window") };
        var expected = chosen;
        var expectedOrigin = origin;
        var offers = 0;
        reviews.Offered += review =>
        {
            review.Transmission.Profile.Should().Be(expected);
            review.Transmission.Provenance.Request.Origin.Should().Be(expectedOrigin);
            copilot.Transmissions.Should().Be(approve ? offers : 0);
            offers++;
            using var presentation = review.Presentation();
            reviews.Confirm(review, approve, RequestOrigin.LocalUi).Should().BeTrue();
        };
        reviews.ClaudeOffered += review =>
        {
            review.Profile.Should().Be(expected);
            offers++;
            using var presentation = review.Presentation();
            reviews.Confirm(review, true, RequestOrigin.ActivatedVoice).Should().BeFalse("Claude egress remains native-only");
            reviews.Confirm(review, approve, RequestOrigin.LocalUi).Should().BeTrue();
        };
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        const string body = "  Original e\u0301🙂\r\nend  ";
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, body, chosen),
            origin, static () => true, Token)).RequestReview!;
        f.RetainCalls.Should().Be(0);
        offers.Should().Be(0);
        var receipt = (await commands.ExecuteAsync(LocalProfileConfirm(review),
            RequestOrigin.LocalUi, static () => true, Token)).RequestSubmission!;
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Execution!.Selection.Provider.Should().Be(service == ProviderService.Copilot
            ? ModelProviderIdentity.Copilot : ModelProviderIdentity.Claude);
        receipt.Message.Outcome.Should().Be(approve ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse?.Body.Should().Be(!approve ? null : service == ProviderService.Copilot
            ? "actual-boundary-answer" : "genuine Claude answer");
        offers.Should().Be(1);
        await commands.Invoking(value => value.ExecuteAsync(LocalProfileConfirm(review),
            origin, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        expected = Profile(service);
        expectedOrigin = RequestOrigin.LocalUi;
        if (service == ProviderService.Copilot)
        { await conversation.SubmitCopilotAsync(new(f.Session, f.Metadata), "next inherited request", static () => true, Token); }
        else
        { await conversation.SubmitClaudeAsync(new(f.Session, f.Metadata), "next inherited request", static () => true, Token); }
        offers.Should().Be(2);
        f.ProfilePreferences.Value.Serialize().Should().Be(app);
        f.ProfilePreferences.Writes.Should().Be(0);
        f.SessionOverrides.Serialize().Should().Be(policy);
        f.SessionProfileRevision.Should().Be(0);
        f.Messages.Should().NotContain(text => text.Contains(body, StringComparison.Ordinal)
            || text.Contains("genuine Claude answer", StringComparison.Ordinal)
            || text.Contains("actual-boundary-answer", StringComparison.Ordinal));
        host.IsQuiescent.Should().BeTrue();
    }

    public static TheoryData<ProviderService, string, bool> HostedRequestProfileDriftCases
    {
        get
        {
            var data = new TheoryData<ProviderService, string, bool>();
            foreach (var service in new[] { ProviderService.Copilot, ProviderService.Claude })
            {
                foreach (var fault in new[] { "account", "catalogue", "deleted-model", "options", "unavailable", "preferences", "policy", "held" })
                {
                    data.Add(service, fault, false);
                    data.Add(service, fault, true);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(HostedRequestProfileDriftCases))]
    public async Task HostedRequestProfileDriftBeforeConfirmationOrPhysicalSendNeverSubstitutesOrSends(
        ProviderService service, string fault, bool duringDisclosure)
    {
        using var f = new Fixture();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var copilot = new HostedRuntime();
        var claude = new ClaudeRuntime();
        var host = service == ProviderService.Copilot ? f.CopilotProfiles(copilot, reviews) : f.ClaudeProfiles(claude, reviews);
        f.ProfileCatalogue.FieldCatalogue = true;
        var chosen = Profile(service) with { Reasoning = new("exact-effort"), Context = new("exact-window") };
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", chosen),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        void Change()
        {
            switch (fault)
            {
                case "account": f.ProfileCatalogue.Account = "changed"; break;
                case "catalogue": f.ProfileCatalogue.Revision++; break;
                case "deleted-model": f.ProfileCatalogue.Model = "removed"; break;
                case "options": f.ProfileCatalogue.FieldCatalogue = false; break;
                case "unavailable": f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; break;
                case "preferences": f.ProfilePreferences.Value = new(ProviderService.Local, []); break;
                case "policy": f.SessionProfileRevision++; break;
                case "held": f.SessionProfileHeld = true; break;
            }
        }
        if (duringDisclosure)
        {
            reviews.Offered += offered =>
            {
                Change();
                using var presentation = offered.Presentation();
                reviews.Confirm(offered, true, RequestOrigin.LocalUi).Should().BeTrue();
            };
            reviews.ClaudeOffered += offered =>
            {
                Change();
                using var presentation = offered.Presentation();
                reviews.Confirm(offered, true, RequestOrigin.LocalUi).Should().BeTrue();
            };
            var receipt = (await commands.ExecuteAsync(LocalProfileConfirm(review),
                RequestOrigin.LocalUi, static () => true, Token)).RequestSubmission!;
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        else
        {
            Change();
            await commands.Invoking(value => value.ExecuteAsync(LocalProfileConfirm(review),
                RequestOrigin.LocalUi, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            f.RetainCalls.Should().Be(0);
            copilot.Calls.Should().Be(0);
            claude.Calls.Should().Be(0);
        }
        copilot.Transmissions.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalFirst)]
    public async Task HostedRequestProfileCannotReplaceProviderModeOrHandoffAuthority(ModelProviderMode mode)
    {
        using var f = new Fixture();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, reviews);
        f.ProviderPreferences.Mode = mode;
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile(ProviderService.Copilot)),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        var receipt = (await commands.ExecuteAsync(LocalProfileConfirm(review),
            RequestOrigin.LocalUi, static () => true, Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        receipt.Message.Reason.Should().Be(mode == ModelProviderMode.LocalOnly
            ? ModelTurnReason.InvalidPolicy : ModelTurnReason.HandoffReviewRequired);
        receipt.Message.AssistantResponse.Should().BeNull();
        runtime.Calls.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedRequestProfileWithoutReviewCompositionOrWithLocalToolsCannotDispatch(bool tools)
    {
        using var f = new Fixture();
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, new(f, f.Time), composeReviews: tools);
        using var root = f.Root();
        var context = f.Context(toolMode: tools ? ModelToolMode.ReadOnly : ModelToolMode.None);
        var review = await host.ReviewLocalRequestProfileAsync(new(f.Session, f.Metadata), context.UserRequest,
            f.Request.Origin, Profile(ProviderService.Copilot), static () => true, Token);
        review.Bind(context.Request);
        var admission = await host.AdmitLocalRequestProfileAsync(review, context, Token);
        admission.Result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        admission.Turn.Should().BeNull();
        runtime.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    [InlineData(ModelTurnOutcome.QuotaExceeded)]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task HostedRequestProfileRetainsRealFailureWithoutFallbackOrPreferenceMutation(ModelTurnOutcome outcome)
    {
        using var f = new Fixture();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime { Reply = new(outcome) };
        var host = f.CopilotProfiles(runtime, reviews);
        var before = f.ProfilePreferences.Value!.Serialize();
        reviews.Offered += offered =>
        {
            using var presentation = offered.Presentation();
            reviews.Confirm(offered, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile(ProviderService.Copilot)),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        var receipt = (await commands.ExecuteAsync(LocalProfileConfirm(review),
            RequestOrigin.LocalUi, static () => true, Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.AssistantResponse.Should().BeNull();
        runtime.Calls.Should().Be(1);
        runtime.Transmissions.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
        f.ProfilePreferences.Value.Serialize().Should().Be(before);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task HostedRequestProfileDefaultDriftAfterTransmissionCannotRetainLateSuccessfulAnswer()
    {
        using var f = new Fixture();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime
        { OnTransmission = () => f.ProfilePreferences.Value = new(ProviderService.Local, []) };
        var host = f.CopilotProfiles(runtime, reviews);
        reviews.Offered += offered =>
        {
            using var presentation = offered.Presentation();
            reviews.Confirm(offered, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile(ProviderService.Copilot)),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        var receipt = (await commands.ExecuteAsync(LocalProfileConfirm(review),
            RequestOrigin.LocalUi, static () => true, Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        receipt.Message.Reason.Should().Be(ModelTurnReason.PolicyChanged);
        receipt.Message.AssistantResponse.Should().BeNull();
        runtime.Transmissions.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
    }
}
