using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("configuration")]
    [InlineData("sessions")]
    [InlineData("request")]
    [InlineData("context")]
    [InlineData("private")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("held")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("privacy")]
    [InlineData("unset")]
    [InlineData("local")]
    [InlineData("catalogue")]
    [InlineData("changed-observation")]
    [InlineData("missing-registration")]
    [InlineData("wrong-adapter")]
    [InlineData("wrong-selection")]
    [InlineData("expired-registration")]
    [InlineData("registration-profile-drift")]
    [InlineData("registration-privacy")]
    public async Task ClaudeHistoryPreparationRejectsUnavailableOrChangedAuthorityWithoutProviderExecution(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var runtime = new ClaudeRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var source = new RegistrationSource
        {
            Resolve = (selection, _, _) =>
            {
                if (fault is "registration-profile-drift") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
                if (fault is "registration-privacy") { f.CanControl = false; }
                return Task.FromResult<ModelProviderRegistration?>(fault is "missing-registration" ? null
                    : new(fault is "wrong-selection" ? ModelProviderSelection.CopilotCandidate : selection,
                        Fixture.Gates(selection),
                        fault is "expired-registration" ? f.Time.Now : f.Time.Now.AddMinutes(5), fault is "wrong-adapter" ? f.Adapter
                            : new ClaudeModelTurnAdapter(runtime, reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance)));
            },
        };
        var host = f.ClaudeProfiles(runtime, reviews, fault is not "configuration", fault is not "sessions",
            registrationSource: source);
        var context = new HostedConversationHistoryContext(
            fault is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request, "new question",
            f.Time.Now, f.Time.Now.AddMinutes(1)).Context;
        if (fault is "context") { context = f.Context(); }
        if (fault is "private") { f.CanControl = false; }
        if (fault is "stopped") { root.Activity!.Stop(); System.Diagnostics.Activity.Current = root.Activity; }
        if (fault is "completed") { root.Complete(HostOperationOutcome.Completed); }
        if (fault is "held") { f.SessionProfileHeld = true; }
        if (fault is "inactive") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "session") { f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; }
        if (fault is "privacy") { f.AfterProfileRead = () => f.CanControl = false; }
        if (fault is "unset") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
        if (fault is "local") { f.ProfilePreferences.Value = new(ProviderService.Local, [Profile(ProviderService.Local)]); }
        if (fault is "catalogue") { f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
        if (fault is "changed-observation") { f.ProfileCatalogue.AfterObservation = () => f.CanControl = false; }
        await host.Invoking(value => value.PrepareClaudeHistoryInputBudgetAsync(context, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        runtime.Calls.Should().Be(0);
        runtime.Sends.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid-bytes")]
    [InlineData("private-before")]
    [InlineData("profile-before")]
    [InlineData("session-before")]
    [InlineData("foreign-request")]
    [InlineData("private-after")]
    [InlineData("profile-after")]
    [InlineData("session-after")]
    public async Task ClaudeHistoryMeasurementRejectsInvalidOrStalePreparationWithoutProviderExecution(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var runtime = new ClaudeRuntime();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        var context = new HostedConversationHistoryContext(f.Request, "new question", f.Time.Now, f.Time.Now.AddMinutes(1)).Context;
        var budget = await host.PrepareClaudeHistoryInputBudgetAsync(context, Token);
        if (fault is "private-before") { f.CanControl = false; }
        if (fault is "profile-before") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
        if (fault is "session-before") { f.SessionProfileRevision++; }
        if (fault is "foreign-request")
        { context = new HostedConversationHistoryContext(HostRequest.Create(RequestOrigin.LocalUi), "foreign question",
            f.Time.Now, f.Time.Now.AddMinutes(1)).Context; }
        runtime.HistoryInputBytes = candidate =>
        {
            if (fault is "private-after") { f.CanControl = false; }
            if (fault is "profile-after") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
            if (fault is "session-after") { f.SessionProfileRevision++; }
            return fault is "invalid-bytes" ? 0 : candidate.Serialize().Length;
        };
        await budget.Invoking(value => value.Measure(context, Token)).Should().ThrowAsync<InvalidOperationException>();
        runtime.Calls.Should().Be(0);
        runtime.Sends.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaudeHistoryCannotUseARuntimeWithoutLocalExactPreparation()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var runtime = new ClaudeRuntime();
        var adapter = new ClaudeModelTurnAdapter(new ClaudeRuntimeWithoutHistoryPreparation(runtime),
            new(f, f.Time), f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var context = new HostedConversationHistoryContext(f.Request, "new question", f.Time.Now, f.Time.Now.AddMinutes(1)).Context;
        await adapter.Invoking(value => value.MeasureHistoryInputAsync(ModelProviderSelection.CopilotCandidate,
            Profile(ProviderService.Claude), context, Token)).Should().ThrowAsync<InvalidOperationException>();
        runtime.Calls.Should().Be(0);
    }

    private sealed class ClaudeRuntimeWithoutHistoryPreparation(ClaudeRuntime inner) : IClaudeTurnRuntime
    {
        public bool IsQuiescent => inner.IsQuiescent;
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token) =>
            inner.RunAsync(provenance, profile, context, admit, current, token);
    }

    [Fact]
    public async Task ClaudeHistoryOmitsOlderWholeMessagesThatExceedTheExactPhysicalInputBudget()
    {
        using var f = new Fixture();
        var messages = Enumerable.Range(1, 6).Select(sequence => Earlier(f, sequence) with
        { Body = new string((char)('a' + sequence), 4000) }).ToArray();
        foreach (var message in messages) { f.HistoryMessages[message.Request.RequestId] = message; }
        f.AfterRetain = () => f.Retained = f.Retained! with { Sequence = 7 };
        f.HostedHistoryPage = (_, cursor, limit) => new(f.Session, 19, 7,
            [.. messages.Append(f.Retained!).Where(item => item.Sequence > (cursor?.After ?? 0)).Take(limit)], null);
        var runtime = new ClaudeRuntime();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        await using var service = f.ConversationService(host, execution: host);
        var receipt = await service.SubmitAsync(new(f.Session, null), "new question", () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var context = runtime.Context!;
        runtime.HistoryInputBytes(context).Should().BeLessThanOrEqualTo(ModelContextEnvelope.MaximumInputUtf8Bytes);
        context.Evidence.Length.Should().BeInRange(1, 5);
        var sequences = context.Evidence.Select(item =>
        {
            using var content = JsonDocument.Parse(item.Content);
            content.RootElement.GetProperty("Body").GetString()!.Length.Should().Be(4000);
            return content.RootElement.GetProperty("Sequence").GetInt64();
        }).ToArray();
        sequences.Should().Equal(Enumerable.Range(7 - sequences.Length, sequences.Length).Select(value => (long)value));
    }

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalFirst)]
    public async Task LocalRoutesDoNotReadOrIncludeEarlierConversationHistory(ModelProviderMode mode)
    {
        using var f = new Fixture();
        Earlier(f);
        f.HostedHistoryPage = (_, _, _) => throw new InvalidOperationException("Local turns must not read hosted history.");
        var host = QualifiedHistoryHost(f);
        f.ProviderPreferences.Mode = mode;
        await using var service = f.ConversationService(host, execution: host);
        var receipt = await service.SubmitAsync(new(f.Session, null), "original local request", () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
        f.HistoryReads.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignHistorySnapshotOrRecordCannotReachTheHostedProvider(bool snapshot)
    {
        using var f = new Fixture();
        var source = Earlier(f);
        var request = new HostRequest(source.Request.RequestId, new(Guid.NewGuid()), source.Request.TaskId, source.Request.Origin);
        var provenance = source.Execution! with { Request = request };
        var foreign = source with { Request = request, Execution = provenance, AssistantResponse = new(provenance, "foreign private answer") };
        f.AfterRetain = () => f.Retained = f.Retained! with { Sequence = 2 };
        f.HostedHistoryPage = (_, cursor, limit) => new(snapshot ? f.Session with { SessionId = request.SessionId } : f.Session,
            4, 2, [.. new[] { foreign, f.Retained! }.Where(item => item.Sequence > (cursor?.After ?? 0)).Take(limit)], null);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, new(f, f.Time));
        await using var service = f.ConversationService(host, execution: host);
        var receipt = await service.SubmitAsync(new(f.Session, null), "new question", () => true, Token);
        receipt.Message.Outcome.Should().Be(snapshot ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unknown);
        receipt.Message.Reason.Should().Be(snapshot ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.InvalidProviderResponse);
        receipt.Message.AssistantResponse.Should().BeNull();
        runtime.Calls.Should().Be(0);
        runtime.Transmissions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsHostedSubmitIncludesWholeSameSessionHistoryAndExcludesHostAndUnknownLocalAnswers(bool claude)
    {
        using var f = new Fixture();
        var first = Earlier(f);
        var second = Earlier(f, 2);
        var hosted = second.Execution! with
        { Selection = second.Execution.Selection with { Provider = ModelProviderIdentity.Copilot } };
        second = second with { Execution = hosted, AssistantResponse = new(hosted, "eligible hosted answer") };
        f.HistoryMessages[second.Request.RequestId] = second;
        var failed = new ConversationMessage(new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            new(1), 3, 2, DateTimeOffset.UnixEpoch, "failed user must not be folded", ModelTurnOutcome.Denied,
            ModelTurnReason.HostAdmissionClosed);
        f.HistoryMessages.Add(failed.Request.RequestId, failed);
        f.AfterRetain = () => f.Retained = f.Retained! with { Sequence = 4 };
        f.HostedHistoryPage = (session, cursor, limit) =>
        {
            session.Should().Be(f.Session.SessionId);
            var messages = f.HistoryMessages.Values.Append(f.Retained!).OrderBy(item => item.Sequence).ToArray();
            return new(f.Session, messages.Sum(item => item.Revision), 4,
                [.. messages.Where(item => item.Sequence > (cursor?.After ?? 0)).Take(limit)], null);
        };
        var copilot = new HostedRuntime();
        var anthropic = new ClaudeRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = claude ? f.ClaudeProfiles(anthropic, reviews) : f.CopilotProfiles(copilot, reviews);
        await using var service = f.ConversationService(host, execution: host);
        var receipt = await service.SubmitAsync(new(f.Session, null), "unchanged new question", () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var bytes = claude ? anthropic.Context!.Serialize() : copilot.ContextBytes!;
        using var wire = JsonDocument.Parse(bytes);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("unchanged new question");
        var bodies = wire.RootElement.GetProperty("Evidence").EnumerateArray().Select(item =>
        {
            using var content = JsonDocument.Parse(item.GetProperty("Content").GetString()!);
            return (content.RootElement.GetProperty("Role").GetString(), content.RootElement.GetProperty("Body").GetString());
        }).ToArray();
        bodies.Should().Equal(("USER", first.Body), ("USER", second.Body), ("ASSISTANT", "eligible hosted answer"));
        System.Text.Encoding.UTF8.GetString(bytes).Should().NotContain(failed.Body).And.NotContain(first.AssistantResponse!.Body);
        f.HistoryReads.Should().BeGreaterThan(0);
        f.Messages.Should().NotContain(message => message.Contains("eligible hosted answer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedHistorySourceRevocationBeforePhysicalSendProducesNoAnswerOrTransmission(bool claude)
    {
        using var f = new Fixture();
        var source = Earlier(f);
        f.AfterRetain = () => f.Retained = f.Retained! with { Sequence = 2 };
        f.HostedHistoryPage = (_, cursor, limit) => new(f.Session, 4, 2,
            [.. new[] { source, f.Retained! }.Where(item => item.Sequence > (cursor?.After ?? 0)).Take(limit)], null);
        var copilot = new HostedRuntime { BeforeTransmission = () => f.HistoryMessages.Clear() };
        var anthropic = new ClaudeRuntime { BeforeAdmit = () => f.HistoryMessages.Clear() };
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = claude ? f.ClaudeProfiles(anthropic, reviews) : f.CopilotProfiles(copilot, reviews);
        await using var service = f.ConversationService(host, execution: host);
        await service.Invoking(value => value.SubmitAsync(new(f.Session, null), "new question", () => true, Token))
            .Should().ThrowAsync<Exception>();
        copilot.Transmissions.Should().Be(0);
        anthropic.Sends.Should().BeEmpty();
        f.Retained!.AssistantResponse.Should().BeNull();
    }
}
