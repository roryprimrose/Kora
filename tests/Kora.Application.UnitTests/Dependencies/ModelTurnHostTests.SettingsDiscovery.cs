using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static SettingInspectionReview SettingsReview(Fixture f, ModelToolMode mode = ModelToolMode.SettingsDiscovery)
    {
        var review = new SettingInspectionReview(
            [
                new(RegisteredSetting.Theme, 0, false, 1),
                new(RegisteredSetting.ReaderTextScale, 100, false, 1),
                new(RegisteredSetting.SummaryWords, 80, false, 1),
                new(RegisteredSetting.SummarySentences, 3, false, 1),
                new(RegisteredSetting.PlaybackVolume, 100, false, 1),
                new(RegisteredSetting.ResponseMode, 0, false, 1),
                new(RegisteredSetting.PresenceDisplay, 1, false, 1),
                new(RegisteredSetting.PresenceTimeout, 10, false, 1),
                new(RegisteredSetting.PresenceSize, 360, false, 1),
                new(RegisteredSetting.DotSize, 100, false, 1),
                new(RegisteredSetting.DotDensity, 100, false, 1),
                new(RegisteredSetting.MovementSpeed, 100, false, 1),
                new(RegisteredSetting.SpeechScaling, 1, false, 1),
                new(RegisteredSetting.SpeechScaleAmount, 100, false, 1),
                new(RegisteredSetting.ReducedMotion, 0, false, 1),
            ], "What controls summaries?", f.Request, f.Time.Now, mode);
        f.Request = review.Destination;
        f.Session = new(f.Request.SessionId, new(1), true);
        f.Task = new(new(f.Request, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
        return review;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsModeRequiresHostBindingBeforeAnyTransportOrToolDispatch(bool bind)
    {
        using var f = new Fixture();
        var review = SettingsReview(f);
        using var root = f.Root();
        var transport = new ToolTransport(
            """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"speech.summary-words"}}]}""",
            FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.LoopHost(loop);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            review.Bind(f.Request, new(1), f.Time.Now), Token)).Turn!;
        if (bind) { host.BindSettingsDiscovery(turn, review); }
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().Be(bind ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied);
        transport.Requests.Should().HaveCount(bind ? 2 : 0);
        if (bind)
        {
            ((ModelToolResultMessage)transport.Requests[1].Messages[1]).Result.Should().Equal(
                CapabilityReplyWire.Serialize(SettingsDiscoveryTools.Read(review.Observations, SettingsDiscoveryTools.Get,
                    """{"id":"speech.summary-words"}""")));
        }
        else { result.Reason.Should().Be(ModelTurnReason.ToolResultRejected); }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("mode")]
    [InlineData("review-mode")]
    [InlineData("provider")]
    [InlineData("session")]
    [InlineData("origin")]
    [InlineData("question")]
    [InlineData("policy")]
    [InlineData("evidence")]
    [InlineData("source")]
    [InlineData("disclosure")]
    [InlineData("expiry")]
    [InlineData("duplicate")]
    public async Task SettingsBindingCannotBorrowForeignModeSourceChannelProviderOrReview(string defect)
    {
        using var f = new Fixture();
        var review = SettingsReview(f, defect is "review-mode" ? ModelToolMode.None : ModelToolMode.SettingsDiscovery);
        using var root = f.Root();
        var envelope = review.Bind(f.Request, new(1), f.Time.Now);
        if (defect is "mode" or "review-mode" or "question" or "policy" or "evidence" or "source" or "disclosure")
        {
            envelope = new(f.Request, defect is "policy" ? "Other policy" : review.ModelPolicy,
                defect is "question" ? "Other question" : review.Question,
                defect is "evidence" ? [] : [new(new(Guid.NewGuid()), f.Request, new(1), defect is "source" ? "Other source" : review.Source,
                    defect is "disclosure" ? ModelEvidenceDisclosure.HostedEligible : ModelEvidenceDisclosure.LocalOnly)],
                f.Time.Now, f.Time.Now.AddSeconds(15), defect is "mode" ? ModelToolMode.None : ModelToolMode.SettingsDiscovery);
        }
        var selection = ModelProviderSelection.OllamaCandidate;
        var host = f.Qualified(selection);
        var turn = (await host.AdmitAsync(selection, envelope, Token)).Turn!;
        if (defect is "provider" or "origin")
        {
            var provenance = defect is "provider" ? turn.Provenance with { Selection = ModelProviderSelection.CopilotCandidate }
                : turn.Provenance with { Request = new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.ActivatedVoice) };
            turn = new(turn.Owner, turn.Issuer, provenance, turn.Context, turn.Wire, turn.SessionGeneration,
                turn.TaskRevision, turn.ControlRevision, turn.Registration);
        }
        if (defect is "session")
        {
            review = new(review.Observations, review.Question, HostRequest.Create(RequestOrigin.LocalUi),
                f.Time.Now, ModelToolMode.SettingsDiscovery);
        }
        if (defect is "expiry") { f.Time.Now = review.ExpiresAt; }
        if (defect is "duplicate") { host.BindSettingsDiscovery(turn, review); }
        var binder = defect is "owner" ? f.Host : host;
        FluentActions.Invoking(() => binder.BindSettingsDiscovery(turn, review)).Should().Throw<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
    }

        [Theory]
        [InlineData("success")]
        [InlineData("missing-host")]
        [InlineData("wrong-mode")]
        [InlineData("wrong-target")]
        public async Task CanonicalSettingsConsumerRequiresExactReviewedModeTargetAndComposedHost(string mode)
        {
            using var f = new Fixture();
            var review = SettingsReview(f, mode is "wrong-mode" ? ModelToolMode.None : ModelToolMode.SettingsDiscovery);
            f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
            f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
            var transport = new ToolTransport(
                """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"speech.summary-words"}}]}""",
                FinalAnswer);
            await using var loop = CreateLoop(f, transport);
            var host = f.ConfiguredProfiles(adapter: loop);
            await using var service = f.ConversationService(host, execution: mode is "missing-host" ? null : host);
            using var root = f.Root();
            var target = new SessionWorkspaceEntry(mode is "wrong-target"
                ? f.Session with { SessionId = new(Guid.NewGuid()) } : f.Session, null);
            var validations = 0;
            Task Validate(CancellationToken _)
            {
                validations++;
                return Task.CompletedTask;
            }
            Task<Kora.Core.Conversation.ConversationSubmissionReceipt> Submit() => service.SubmitSettingsDiscoveryAsync(
                target, review, (request, now) => review.Bind(request, target.Authority.Generation, now),
                () => true, Validate, [], result => result, Token);
            if (mode is not "success")
            {
                await FluentActions.Awaiting(Submit).Should().ThrowAsync<InvalidOperationException>();
                f.RetainCalls.Should().Be(0);
                transport.Requests.Should().BeEmpty();
                return;
            }
            var receipt = await Submit();
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            receipt.Message.Body.Should().Be(review.Question);
            receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
            receipt.Message.Observations.Should().BeNull();
            validations.Should().BeGreaterThan(0);
            transport.Requests.Should().HaveCount(2);
        }

        [Fact]
        public async Task RegisteredMetadataWithoutAConfirmedReaderIsTruthfullyUnavailableNotAProviderFailure()
        {
            using var f = new Fixture();
            var review = SettingsReview(f);
            using var root = f.Root();
            var transport = new ToolTransport(
                """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"appearance.response-timeout"}}]}""");
            await using var loop = CreateLoop(f, transport);
            var host = f.LoopHost(loop);
            var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
                review.Bind(f.Request, new(1), f.Time.Now), Token)).Turn!;
            host.BindSettingsDiscovery(turn, review);
            var result = await host.RunAsync(turn, Token);
            result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            result.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
            result.Response.Should().BeNull();
            transport.Requests.Should().ContainSingle();
        }

        [Theory]
        [InlineData("input")]
        [InlineData("output")]
        [InlineData("calls")]
        public async Task SettingsDiscoveryCannotWidenExistingCumulativeByteOrCallCeilings(string budget)
        {
            using var f = new Fixture();
            var review = SettingsReview(f);
            using var root = f.Root();
            var calls = string.Join(',', Enumerable.Range(1, ModelToolProtocol.MaximumCalls).Select(index =>
                $$$"""{"callId":"list_{{{index}}}","capabilityId":"settings.list","input":{"count":1}}"""));
            var transport = new ToolTransport(
                """{"kind":"toolCalls","calls":[""" + calls + "]}",
                """{"kind":"toolCalls","calls":[{"callId":"extra_7","capabilityId":"settings.list","input":{"count":1}}]}""");
            if (budget is not "calls")
            {
                transport.ChangeReply = (request, reply) => reply with
                {
                    Usage = budget is "input"
                        ? reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes }
                        : reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes },
                };
            }
            await using var loop = CreateLoop(f, transport);
            var host = f.LoopHost(loop);
            var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
                review.Bind(f.Request, new(1), f.Time.Now), Token)).Turn!;
            host.BindSettingsDiscovery(turn, review);
            var result = await host.RunAsync(turn, Token);
            result.Reason.Should().Be(ModelTurnReason.ToolLoopBudgetExceeded);
            result.Response.Should().BeNull();
            transport.Requests.Should().HaveCount(budget is "calls" ? 2 : 1);
            ModelToolProtocol.MaximumCalls.Should().Be(6);
            ModelToolProtocol.MaximumInputUtf8Bytes.Should().Be(32768);
            ModelToolProtocol.MaximumOutputUtf8Bytes.Should().Be(4096);
        }
    [Theory]
    [InlineData("expiry")]
    [InlineData("permission")]
    [InlineData("unknown-session")]
    [InlineData("stale-session")]
    public async Task BoundSettingsDiscoveryRevalidatesAuthorityAfterTransportAwait(string change)
    {
        using var f = new Fixture();
        var review = SettingsReview(f);
        using var root = f.Root();
        var transport = new ToolTransport(
            """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"speech.summary-words"}}]}""",
            FinalAnswer)
        {
            AfterRequest = _ =>
            {
                switch (change)
                {
                    case "expiry": f.Time.Now = review.ExpiresAt; break;
                    case "permission": f.CanControl = false; break;
                    case "unknown-session": f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; break;
                    case "stale-session": f.Session = f.Session with { Generation = new(2) }; break;
                }
            },
        };
        await using var loop = CreateLoop(f, transport);
        var host = f.LoopHost(loop);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            review.Bind(f.Request, new(1), f.Time.Now), Token)).Turn!;
        host.BindSettingsDiscovery(turn, review);
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Response.Should().BeNull();
        transport.Requests.Should().ContainSingle();
    }
}
