using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Skills;

[Collection("Host tracing")]
public sealed partial class OwnedSkillSaveWorkflowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OwnedSkillSaveWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task CompleteSeparateExactReviewPublishesListsAndReopensWithoutAnyInvocation(RequestOrigin origin)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new Store();
        var audit = new Audit();
        var flow = new OwnedSkillSaveWorkflow(store, admission, audit);
        var authority = new OwnedSkillAuthoringAuthority(origin, static () => true);
        var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "owned skill save\nliteral only");
        var review = new NativeReview();
        var saved = await flow.Save(document, authority, static () => true, review, Token);
        saved!.Document.Should().BeSameAs(document);
        saved.Document.IsAvailableForInvocation.Should().BeFalse();
        review.Calls.Should().Equal("review", "confirm");
        review.Document.Should().BeSameAs(document);
        review.Destination.Should().Be("fixed-owned-destination");
        audit.Events.Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(e => e.Initiator == (origin == RequestOrigin.LocalUi
            ? SecurityAuditInitiator.LocalUser : SecurityAuditInitiator.VoiceCommand));
        (await flow.List(authority, Token)).Should().ContainSingle().Which.Should().BeSameAs(saved);
        (await flow.Read("example", "1.0.0", authority, Token)).Should().BeSameAs(saved);
        store.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("confirm")]
    [InlineData("original")]
    [InlineData("draft")]
    [InlineData("cancel")]
    public async Task NoPublicationBeforeSeparateConfirmationOrAfterOriginalAuthorityRetires(string change)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new Store();
        var audit = new Audit();
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.ActivatedVoice, static () => true);
        var exact = true;
        using var cancel = new CancellationTokenSource();
        var review = new NativeReview { First = change is not "review", Second = change is not "confirm" };
        review.AfterReview = () =>
        {
            if (change is "original") { authority.Retire(); }
            if (change is "draft") { exact = false; }
            if (change is "cancel") { cancel.Cancel(); }
        };
        var flow = new OwnedSkillSaveWorkflow(store, admission, audit);
        var save = () => flow.Save(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal"),
            authority, () => exact, review, cancel.Token);
        if (change is "cancel") { await save.Should().ThrowAsync<OperationCanceledException>(); }
        else { (await save()).Should().BeNull(); }
        store.Writes.Should().Be(0);
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationFailuresHaveTruthfulTypedAuditAndNoSuccessResult(bool uncertain)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new Store { Failure = uncertain ? new OwnedSkillPublicationUnconfirmedException(new IOException("readback")) : new IOException("staging") };
        var audit = new Audit();
        var flow = new OwnedSkillSaveWorkflow(store, admission, audit);
        var save = () => flow.Save(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal"),
            new(RequestOrigin.LocalUi, static () => true), static () => true, new NativeReview(), Token);
        await save.Should().ThrowAsync<IOException>();
        audit.Events.Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            uncertain ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Failed);
    }

    [Fact]
    public void OriginalAuthorityNeverRecoversAfterObservedRevocation()
    {
        var current = true;
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.ActivatedVoice, () => current);
        authority.IsCurrent().Should().BeTrue();
        current = false;
        authority.IsCurrent().Should().BeFalse();
        current = true;
        authority.IsCurrent().Should().BeFalse();
        var invalid = () => new OwnedSkillAuthoringAuthority(RequestOrigin.HostSystem, static () => true);
        invalid.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("owned skill create example 1.0.0", "create")]
    [InlineData("Nova, owned skill validate", "validate")]
    [InlineData("owned skill save", "save")]
    [InlineData("owned skill refine", "refine")]
    [InlineData("owned skill test", "test")]
    [InlineData("Nova, owned skill cancel-test", "cancel-test")]
    [InlineData("owned skill fixture:  unchanged  ", "fixture")]
    [InlineData("Nova, owned skill cancel-refinement", "cancel-refinement")]
    [InlineData("owned skill critique:  literal unchanged  ", "critique")]
    [InlineData("owned skill list", "list")]
    [InlineData("owned skill reopen example 1.0.0", "reopen")]
    [InlineData("owned skill use example 1.0.0", "use")]
    [InlineData("owned skill enable", "enable")]
    [InlineData("owned skill targets", "targets")]
    [InlineData("owned skill next-targets", "next-targets")]
    [InlineData("owned skill review-use", "review-use")]
    [InlineData("owned skill review-tool", "review-tool")]
    [InlineData("Nova, owned skill tool web-page-get", "tool")]
    [InlineData("owned skill tool none", "tool")]
    [InlineData("owned skill inputs none", "inputs")]
    [InlineData("Nova, owned skill inputs required-literal", "inputs")]
    [InlineData("owned skill inputs optional", "Invalid")]
    [InlineData("owned skill inputs required-literal\n", "Invalid")]
    [InlineData("owned skill input topic: literal", "input")]
    [InlineData("owned skill input : literal", "Invalid")]
    [InlineData("owned skill input topic", "Invalid")]
    [InlineData("owned skill tool app.version", "Invalid")]
    [InlineData("owned skill tool web-page-get\n", "Invalid")]
    [InlineData("owned skill tools web-page-get", "Invalid")]
    [InlineData("owned skill url: https://example.com/", "url")]
    [InlineData("owned skill invoke", "invoke")]
    [InlineData("owned skill close-use", "close-use")]
    [InlineData("owned skill target 12345678-1234-1234-1234-123456789abc", "target")]
    [InlineData("owned skill target invalid", "Invalid")]
    [InlineData("owned skill target 12345678-1234-1234-1234-123456789ABC", "Invalid")]
    [InlineData("owned skill request: unchanged original", "request")]
    [InlineData("owned skill create CON 1.0.0", "Invalid")]
    [InlineData("owned skill run example", "Invalid")]
    [InlineData("create a skill that", "Invalid")]
    [InlineData("Nova, create a skill that explains a failure", "generate-request")]
    public void CommandsRemainDedicatedAndClosed(string input, string operation) =>
        OwnedSkillAuthoringCommand.Parse(input, "Nova")!.Operation.Should().Be(operation);

    [Fact]
    public void OversizeInitialSkillRequestIsRejectedWithoutTruncation() =>
        OwnedSkillAuthoringCommand.Parse("create a skill that " + new string('x', 66049), "Nova")!.Operation.Should().Be("Invalid");

    [Theory]
    [InlineData("owned skill editor", "editor")]
    [InlineData("owned skill cancel", "cancel")]
    [InlineData("owned skill validate", "validate")]
    [InlineData("owned skill unknown", "Invalid")]
    [InlineData("owned skill name: Name", "name")]
    [InlineData("owned skill description: Description", "description")]
    [InlineData("owned skill", "Invalid")]
    [InlineData("owned skill create example 1.0.0 extra", "Invalid")]
    [InlineData("owned skill run example 1.0.0", "Invalid")]
    [InlineData("owned skill reopen example 1.0.0\n", "Invalid")]
    public void RemainingCommandFormsRemainClosed(string input, string operation) =>
        OwnedSkillAuthoringCommand.Parse(input, "Nova")!.Operation.Should().Be(operation);

    [Theory]
    [InlineData("after-publication")]
    [InlineData("terminal")]
    public async Task LateAuthorityOrTerminalFailureCannotReportConfirmedPublication(string failure)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.ActivatedVoice, static () => true);
        var store = new Store { AfterPublication = () => { if (failure is "terminal") { sessions.FailTerminal = true; } else { authority.Retire(); } } };
        var audit = new Audit();
        var flow = new OwnedSkillSaveWorkflow(store, admission, audit);
        var save = () => flow.Save(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal"),
            authority, static () => true, new NativeReview(), Token);
        await save.Should().ThrowAsync<OwnedSkillPublicationUnconfirmedException>();
        store.Writes.Should().Be(1);
        if (failure is "after-publication") { audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalVoiceAuthorityIsNotRecapturedAsLaterNativeOrigin(bool capturedBeforeNative)
    {
        var captured = capturedBeforeNative ? new OwnedSkillAuthoringAuthority(RequestOrigin.ActivatedVoice, static () => true) : null;
        using var native = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var audit = new Audit();
        var flow = new OwnedSkillSaveWorkflow(new Store(), admission, audit);
        await flow.Save(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal"),
            captured ?? new(RequestOrigin.ActivatedVoice, static () => true), static () => true, new NativeReview(), Token);
        audit.Events.Should().OnlyContain(e => e.Initiator == SecurityAuditInitiator.VoiceCommand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredAuthoringLinksOriginalContextWithoutFabricatingItsParent(bool laterNative)
    {
        var stopped = new List<Activity>();
        listener.ActivityStopped = stopped.Add;
        OwnedSkillAuthoringAuthority authority;
        ActivityContext originalContext;
        using (var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            authority = new(RequestOrigin.ActivatedVoice, static () => true);
            originalContext = original.Activity!.Context;
        }
        using var native = laterNative
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request) : null;
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var flow = new OwnedSkillSaveWorkflow(new Store(), admission, new Audit());
        await flow.Save(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal"),
            authority, static () => true, new NativeReview(), Token);
        stopped.Should().Contain(activity => activity.ParentId == null
            && activity.Links.Any(link => link.Context == originalContext)
            && (!laterNative || activity.Links.Any(link => link.Context == native!.Activity!.Context)));
    }

    [Fact]
    public void LiteralPayloadIsNotTrimmedOrInterpretedAsMoreCommands()
    {
        const string payload = "  owned skill save\nowned skill cancel\r\n ";
        OwnedSkillAuthoringCommand.Parse("Nova, owned skill guidance: " + payload, "Nova")!.Literal.Should().Be(payload);
        OwnedSkillAuthoringCommand.Parse("use other", "Nova").Should().BeNull();
        OwnedSkillAuthoringCommand.Parse("owned skill save\0", "Nova")!.Operation.Should().Be("Invalid");
        OwnedSkillAuthoringCommand.Parse("owned skill guidance: " + new string('x', 66049), "Nova")!.Operation.Should().Be("Invalid");
    }

    private sealed class Sessions : AudioControlTestStore, IOwnedSkillControlSessionStore
    {
        public ValueTask<WorkSessionAuthorization> CreateOwnedSkillSession(HostRequest request, Func<bool> admitted, CancellationToken token) =>
            CreateAudioControlSessionAsync(request, admitted, token);
        public ValueTask<T> WithOwnedSkillSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token) =>
            WithAudioControlSessionAsync(request, generation, operation, token);
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
    private sealed class NativeReview : IOwnedSkillSaveReview
    {
        internal bool First { get; init; } = true;
        internal bool Second { get; init; } = true;
        internal Action? AfterReview { get; set; }
        internal List<string> Calls { get; } = [];
        internal OwnedDeclarativeSkillDocument? Document { get; private set; }
        internal string? Destination { get; private set; }
        public Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token)
        {
            Document = document;
            Destination = destination;
            Calls.Add("review");
            AfterReview?.Invoke();
            return Task.FromResult(First);
        }
        public Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token)
        { Calls.Add("confirm"); return Task.FromResult(Second); }
    }
    private sealed class Store : IOwnedDeclarativeSkillStore
    {
        internal int Writes { get; private set; }
        internal Exception? Failure { get; init; }
        internal Action? AfterPublication { get; init; }
        private OwnedDeclarativeSkillSnapshot? saved;
        public string Destination(OwnedDeclarativeSkillDocument document) => "fixed-owned-destination";
        public Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OwnedDeclarativeSkillSnapshot>>(saved is null ? [] : [saved]);
        public Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token) => Task.FromResult(saved);
        public Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token) =>
            commit(() =>
            {
                eligible().Should().BeTrue();
                if (Failure is not null) { throw Failure; }
                Writes++;
                saved = new(document, "directory", "manifest", "instructions");
                AfterPublication?.Invoke();
                return saved;
            });
    }
}
