using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Presentation;

[Collection("Host tracing")]
public sealed class ExactAnswerExportReviewTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public ExactAnswerExportReviewTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public async Task ReviewRequiresSeparateSingleUseConfirmationAndPreservesExactSource(string extension)
    {
        var source = Source();
        using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Application, HostOperation.Request);
        var writer = new Destination { Path = Path.GetFullPath("answer" + extension) };
        var audit = new Audit();
        await using var review = new ExactAnswerExportReview(source, writer, () => true, _ => Task.FromResult(source),
            audit, NullLogger<ExactAnswerExportReview>.Instance);
        await review.ValidateAsync(TestContext.Current.CancellationToken);
        review.Source.Should().BeSameAs(source);
        review.Destination.Should().Be(writer.Path);
        review.Format.Should().Contain(extension is ".txt" ? "plain text" : "Markdown").And.Contain("no BOM");
        ExactAnswerExportReview.RetentionDisclosure.Should().Contain("does not delete").And.Contain("sync");
        writer.Calls.Should().Be(0);
        audit.Events.Should().BeEmpty();
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(ExactAnswerExportOutcome.Saved);
        writer.Source.Should().BeSameAs(source);
        writer.Calls.Should().Be(1);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item => item.ActionId == "result.exact-answer.export"
            && item.TargetId == source.Reference.ItemId.Value.ToString("D"));
        var replay = () => review.ConfirmAsync(TestContext.Current.CancellationToken);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        writer.Calls.Should().Be(1);
        await review.DisposeAsync();
        writer.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("message")]
    [InlineData("voice")]
    [InlineData("model")]
    [InlineData("session")]
    public void ReviewCannotBorrowPassiveReadOrForeignChannelAuthority(string fault)
    {
        var source = Source();
        var request = source.ConversationMessage!.Request;
        if (fault is "origin")
        {
            var message = source.ConversationMessage;
            source = new(new(new(message.Request.RequestId.Value), message.Revision), DetailContentKind.PlainText,
                DetailContentOrigin.RetainedConversation, DetailSensitivity.DisclosureConfirmationRequired,
                "USER", "Private original", message.Body, conversationMessage: message);
        }
        if (fault is "message")
        {
            source = new(new(new(Guid.NewGuid()), 1), DetailContentKind.PlainText, DetailContentOrigin.EmbeddedDocument,
                DetailSensitivity.Public, "Document", "Embedded", "not a retained answer");
        }
        request = fault switch
        {
            "voice" => new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.ActivatedVoice),
            "model" => new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())),
            "session" => HostRequest.Create(RequestOrigin.LocalUi),
            _ => request,
        };
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var create = () => new ExactAnswerExportReview(source, new Destination(), () => true, _ => Task.FromResult(source),
            new Audit(), NullLogger<ExactAnswerExportReview>.Instance);
        create.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("source")]
    [InlineData("cancel-before")]
    [InlineData("cancel-after")]
    public async Task ValidationDeniesRetiredChangedAndCancelledSourcesWithoutWriting(string fault)
    {
        var source = Source();
        using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = new CancellationTokenSource();
        var live = fault is not "before";
        if (fault is "cancel-before") { cancellation.Cancel(); }
        var writer = new Destination();
        await using var review = new ExactAnswerExportReview(source, writer, () => live, _ =>
        {
            if (fault is "after") { live = false; }
            if (fault is "cancel-after") { cancellation.Cancel(); }
            return Task.FromResult(fault is "source" ? Source() : source);
        }, new Audit(), NullLogger<ExactAnswerExportReview>.Instance);
        (await review.ConfirmAsync(cancellation.Token)).Should().Be(fault.StartsWith("cancel", StringComparison.Ordinal)
            ? ExactAnswerExportOutcome.Cancelled : ExactAnswerExportOutcome.NotWritten);
        writer.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ExactAnswerExportOutcome.Saved, false)]
    [InlineData(ExactAnswerExportOutcome.NotWritten, false)]
    [InlineData(ExactAnswerExportOutcome.Cancelled, false)]
    [InlineData(ExactAnswerExportOutcome.Uncertain, false)]
    [InlineData(ExactAnswerExportOutcome.Saved, true)]
    [InlineData(ExactAnswerExportOutcome.NotWritten, true)]
    [InlineData(ExactAnswerExportOutcome.Cancelled, true)]
    [InlineData(ExactAnswerExportOutcome.Uncertain, true)]
    public async Task TerminalAuditFailureCannotCertifyASavedFile(ExactAnswerExportOutcome outcome, bool failAudit)
    {
        var source = Source();
        using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Application, HostOperation.Request);
        var writer = new Destination { Outcome = outcome };
        var audit = new Audit { FailTerminal = failAudit };
        await using var review = new ExactAnswerExportReview(source, writer, () => true, _ => Task.FromResult(source),
            audit, NullLogger<ExactAnswerExportReview>.Instance);
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(
            failAudit && outcome == ExactAnswerExportOutcome.Saved ? ExactAnswerExportOutcome.Uncertain : outcome);
        writer.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("requested-audit")]
    [InlineData("after-audit")]
    [InlineData("writer")]
    [InlineData("writer-cancel")]
    [InlineData("request")]
    public async Task FailuresNeverRenewAuthorityOrRetryAnUncertainWrite(string fault)
    {
        var source = Source();
        using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Application, HostOperation.Request);
        var live = true;
        var writer = new Destination
        {
            Error = fault is "writer" ? new IOException("synthetic write uncertainty")
                : fault is "writer-cancel" ? new OperationCanceledException() : null,
        };
        var audit = new Audit
        {
            FailRequested = fault is "requested-audit",
            AfterRequested = () => { if (fault is "after-audit") { live = false; } },
        };
        await using var review = new ExactAnswerExportReview(source, writer, () => live, _ => Task.FromResult(source),
            audit, NullLogger<ExactAnswerExportReview>.Instance);
        using var changed = fault is "request"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request) : null;
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(
            fault.StartsWith("writer", StringComparison.Ordinal) ? ExactAnswerExportOutcome.Uncertain : ExactAnswerExportOutcome.NotWritten);
        writer.Calls.Should().Be(fault.StartsWith("writer", StringComparison.Ordinal) ? 1 : 0);
    }

    [Fact]
    public async Task ContentAndDestinationBearingExceptionsNeverEnterDiagnosticOrAuditPayloads()
    {
        var source = Source();
        using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Application, HostOperation.Request);
        var logger = new Network.EnabledLogger<ExactAnswerExportReview>();
        var writer = new Destination { Error = new IOException(source.Source + Path.GetFullPath("private-export-location.md")) };
        var audit = new Audit();
        await using var review = new ExactAnswerExportReview(source, writer, () => true, _ => Task.FromResult(source), audit, logger);
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(ExactAnswerExportOutcome.Uncertain);
        logger.Messages.Should().ContainSingle();
        string.Join("\n", logger.Messages).Should().NotContain("outside viewport").And.NotContain("private-export-location");
        System.Text.Json.JsonSerializer.Serialize(audit.Events).Should().NotContain("outside viewport")
            .And.NotContain("private-export-location").And.NotContain(writer.Path);
    }

    private static AdmittedDetailContent Source()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "original USER never exported",
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "# exact e\u0301 😀\r\n```csharp\r\nreturn 1;\r\n```\n  final outside viewport  "),
        };
        return new(new(new(provenance.Turn.Value), message.Revision), DetailContentKind.Markdown,
            DetailContentOrigin.RetainedAssistant, DetailSensitivity.DisclosureConfirmationRequired,
            "ASSISTANT", "Private retained original", message.AssistantResponse.Body, conversationMessage: message);
    }

    private sealed class Destination : IExactAnswerExportDestination
    {
        public string Path { get; init; } = System.IO.Path.GetFullPath("answer.md");
        internal int Calls { get; private set; }
        internal bool Disposed { get; private set; }
        internal AdmittedDetailContent? Source { get; private set; }
        internal ExactAnswerExportOutcome Outcome { get; init; } = ExactAnswerExportOutcome.Saved;
        internal Exception? Error { get; init; }
        public async Task<ExactAnswerExportOutcome> PublishAsync(AdmittedDetailContent source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            Source = source;
            await revalidate(token);
            admitted().Should().BeTrue();
            if (Error is not null) { throw Error; }
            return Outcome;
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal bool FailRequested { get; init; }
        internal bool FailTerminal { get; init; }
        internal Action? AfterRequested { get; init; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == SecurityAuditOutcome.Requested)
            {
                if (FailRequested) { throw new IOException("synthetic audit unavailable"); }
                AfterRequested?.Invoke();
            }
            else if (FailTerminal) { throw new IOException("synthetic terminal audit unavailable"); }
            Events.Add(auditEvent);
        }
    }
}
