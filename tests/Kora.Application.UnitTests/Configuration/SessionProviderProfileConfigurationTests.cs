using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed partial class SessionProviderProfileConfigurationTests
{
    [Fact]
    public async Task PassiveInspectionCannotRetargetForeignSessionPolicyOrWriteAControlIntent()
    {
        await using var f = new Fixture();
        var draft = await f.Inspect();
        f.Store.Tasks.Should().BeEmpty();
        f.Service.Discard(draft);
        f.Snapshot = f.Snapshot with { Session = f.Session with { SessionId = new(Guid.NewGuid()) } };
        await f.Invoking(value => value.Inspect()).Should().ThrowAsync<InvalidDataException>();
        f.Store.Tasks.Should().BeEmpty();
    }
    [Theory]
    [InlineData("model")]
    [InlineData("stopped")]
    [InlineData("stopped-bound")]
    [InlineData("completed")]
    [InlineData("expired-control")]
    [InlineData("system")]
    [InlineData("user")]
    public async Task IncomingCallbacksCannotRelabelModelOrExpiredContextAsOriginalSessionPolicyInput(string context)
    {
        await using var f = new Fixture();
        var request = HostRequest.Create(context is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (context is "model") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (context is "stopped") { root.Activity!.Stop(); }
        if (context is "stopped-bound") { root.Activity!.Stop(); Activity.Current = root.Activity; }
        if (context is "completed" or "expired-control") { root.Complete(HostOperationOutcome.Completed); }
        if (context is "expired-control") { root.Activity!.Stop(); Activity.Current = root.Activity; }
        if (context is "user")
        {
            var draft = await f.Inspect();
            draft.Saved.Session.Should().Be(f.Session);
            f.Service.Discard(draft);
        }
        else { await f.Invoking(value => value.Inspect()).Should().ThrowAsync<InvalidOperationException>(); }
        f.Snapshot.Revision.Should().Be(0);
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("publication")]
    [InlineData("cancel")]
    public async Task PassiveInspectionRevalidatesAndTruthfullyTerminatesWithoutWritingIntent(string failure)
    {
        await using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        bool Admission()
        {
            calls++;
            return failure is not "initial" && (failure is not "publication" || calls != 4);
        }
        if (failure is "cancel") { f.Profiles.Catalogue.BeforeObservation = () => throw new OperationCanceledException(); }
        await f.Service.Invoking(service => service.Inspect(f.Session.SessionId, RequestOrigin.LocalUi,
            Admission, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.Store.Tasks.Should().BeEmpty();
        f.Snapshot.Revision.Should().Be(0);
    }

    [Fact]
    public async Task FailureDiagnosticsRetainOriginalExactSessionAndLiveTraceWithoutSelectionContent()
    {
        await using var f = new Fixture();
        var draft = await f.Inspect();
        f.CommitFailure = new IOException();
        var proposal = f.Service.SelectService(draft, ProviderService.Claude);
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token))
            .Should().ThrowAsync<IOException>();
        f.Logger.Requests.Should().ContainSingle().Which.SessionId.Should().Be(f.Session.SessionId);
    }

    [Fact]
    public async Task HeldUnsetDraftStillSeparatesDesiredStateWithoutInventingCatalogue()
    {
        await using var f = new Fixture();
        f.Profiles.Preferences.Value = new(ProviderService.Unset, []);
        var draft = await f.Inspect();
        draft.Catalogue.Should().BeNull();
        draft.EffectiveService.Should().Be(ProviderService.Unset);
        f.Service.Discard(draft);
    }
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task AllInheritServiceChangeAndAllResetUseOnlyExactSessionPolicy(RequestOrigin origin)
    {
        await using var f = new Fixture();
        var draft = await f.Inspect(origin);
        draft.Saved.Revision.Should().Be(0);
        draft.Saved.Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        draft.Effective.Should().BeNull();
        draft.Status.Should().Contain("Unavailable");
        draft.Catalogue!.Models.Should().BeEmpty();
        var proposal = f.Service.SelectService(draft, ProviderService.Claude);
        proposal.ConfirmationId.Should().NotBeEmpty();
        var committed = await f.Service.Confirm(proposal, origin, () => f.Eligible, f.Token);
        committed.Desired.Service.Should().Be(ProviderService.Claude);
        f.Profiles.Preferences.Writes.Should().Be(0);
        draft = await f.Inspect(origin);
        draft.EffectiveService.Should().Be(ProviderService.Claude);
        var reset = f.Service.Reset(draft);
        (await f.Service.Confirm(reset, origin, () => f.Eligible, f.Token)).Desired.Serialize()
            .Should().Be(SessionProviderOverrides.Inherit.Serialize());
        f.Profiles.Preferences.Writes.Should().Be(0);
        f.Snapshot.Revision.Should().Be(2);
    }

    [Theory]
    [InlineData(SessionProviderField.Model)]
    [InlineData(SessionProviderField.Reasoning)]
    [InlineData(SessionProviderField.Context)]
    public async Task SparseFieldChoicesAndIndividualResetsRetainOtherInheritedValues(SessionProviderField field)
    {
        await using var f = new Fixture(current: true);
        var draft = await f.Inspect();
        draft.Effective.Should().NotBeNull();
        var model = draft.Catalogue!.Models.Single();
        var proposal = f.Service.Select(draft, field, model, model.Reasoning[0], model.Context[0]);
        var committed = await f.Service.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        committed.Desired.Service.Should().BeNull();
        committed.Desired.Model.Should().Be(field == SessionProviderField.Model ? model.Id : null);
        committed.Desired.Reasoning.Should().Be(field == SessionProviderField.Reasoning ? model.Reasoning[0].Id : null);
        committed.Desired.Context.Should().Be(field == SessionProviderField.Context ? model.Context[0].Id : null);
        draft = await f.Inspect();
        draft.Effective.Should().Be(f.Profiles.Preferences.Value!.For(ProviderService.Local));
        await f.Service.Confirm(f.Service.Reset(draft, field), RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        f.Snapshot.Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        f.Profiles.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CompleteTupleChoiceCapturesExactAdvertisedObjectsAndKeepsAppImmutable()
    {
        await using var f = new Fixture(current: true);
        var draft = await f.Inspect();
        var model = draft.Catalogue!.Models.Single();
        var proposal = f.Service.Select(draft, null, model, model.Reasoning[0], model.Context[0]);
        proposal.Desired.Binding.Should().Be(draft.Effective);
        await f.Service.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        f.Snapshot.Desired.Model.Should().Be(model.Id);
        f.Snapshot.Desired.Reasoning.Should().Be(model.Reasoning[0].Id);
        f.Snapshot.Desired.Context.Should().Be(model.Context[0].Id);
        f.Profiles.Preferences.Writes.Should().Be(0);
        f.Service.Invoking(service => service.Reset(draft)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("expiry")]
    [InlineData("status")]
    [InlineData("installation")]
    [InlineData("app")]
    [InlineData("private")]
    [InlineData("input")]
    [InlineData("origin")]
    public async Task StaleCatalogueAppAccountOrOriginalReviewCannotConfirm(string drift)
    {
        await using var f = new Fixture(current: true);
        var draft = await f.Inspect();
        var proposal = f.Service.SelectField(draft, SessionProviderField.Context, new("managed-window"));
        switch (drift)
        {
            case "account": f.Profiles.Catalogue.Account = "other"; break;
            case "revision": f.Profiles.Catalogue.Revision = 2; break;
            case "model": f.Profiles.Catalogue.Model = "other"; break;
            case "reasoning": f.Profiles.Catalogue.Reasoning = "other"; break;
            case "context": f.Profiles.Catalogue.Context = "other"; break;
            case "expiry": f.Profiles.Time.Now = f.Profiles.Catalogue.Expiry; break;
            case "status": f.Profiles.Catalogue.Status = ProviderCatalogueStatus.Unavailable; break;
            case "installation": f.Profiles.Catalogue.Installation = LocalProviderInstallation.Missing; break;
            case "app": f.Profiles.Preferences.Value = new(ProviderService.Codex, []); break;
            case "private": f.Access.ControlRevision = 2; break;
            case "input": f.Eligible = false; break;
        }
        await f.Service.Invoking(service => service.Confirm(proposal,
            drift is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => f.Eligible, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Snapshot.Revision.Should().Be(0);
        f.Profiles.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("discarded")]
    [InlineData("held")]
    [InlineData("done")]
    [InlineData("input")]
    [InlineData("capacity")]
    public async Task OnlyIssuedCurrentActiveOriginalUserDraftCanIssueAProposal(string kind)
    {
        await using var f = new Fixture();
        var draft = await f.Inspect();
        if (kind is "discarded") { f.Service.Discard(draft); }
        if (kind is "held" or "done")
        {
            f.Snapshot = kind is "held" ? f.Snapshot with { Held = true } : f.Snapshot with { Session = f.Snapshot.Session with { IsActive = false } };
            f.Service.Discard(draft);
            draft = await f.Inspect();
            draft.Status.Should().Contain("Unavailable");
        }
        if (kind is "input") { f.Eligible = false; }
        if (kind is "capacity")
        {
            for (var index = 0; index < 63; index++) { _ = f.Service.SelectService(draft, ProviderService.Claude); }
            await f.Service.Invoking(service => service.Inspect(f.Session.SessionId, RequestOrigin.LocalUi, () => f.Eligible, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        await using var other = new Fixture();
        var service = kind is "foreign" ? other.Service : f.Service;
        service.Invoking(value => value.SelectService(draft, ProviderService.Claude)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("expiry")]
    [InlineData("installation")]
    [InlineData("unset")]
    public async Task ModelChoicesRequireCurrentCatalogueAndExactObjectProvenance(string drift)
    {
        await using var f = new Fixture(current: true);
        if (drift is "unset") { f.Profiles.Preferences.Value = new(ProviderService.Unset, []); }
        var draft = await f.Inspect();
        var model = (draft.Catalogue ?? draft.Observation.For(ProviderService.Local)).Models.Single();
        if (drift is "expiry") { f.Profiles.Time.Now = f.Profiles.Catalogue.Expiry; }
        if (drift is "installation")
        {
            f.Service.Discard(draft);
            f.Profiles.Catalogue.Installation = LocalProviderInstallation.Unknown;
            draft = await f.Inspect();
        }
        var other = await f.Profiles.Catalogue.ObserveAsync(f.Token);
        f.Service.Invoking(value => value.Select(draft, null, drift is "model" ? other.For(ProviderService.Local).Models.Single() : model,
            drift is "reasoning" ? other.For(ProviderService.Local).Models.Single().Reasoning[0] : model.Reasoning[0],
            drift is "context" ? other.For(ProviderService.Local).Models.Single().Context[0] : model.Context[0]))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageOrCancellationFailureNeverClaimsConfirmation(bool cancel)
    {
        await using var f = new Fixture();
        var draft = await f.Inspect();
        var proposal = f.Service.SelectService(draft, ProviderService.Claude);
        f.CommitFailure = cancel ? new OperationCanceledException() : new IOException("uncertain storage");
        f.Logger.Enabled = cancel;
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token))
            .Should().ThrowAsync<Exception>();
        f.Snapshot.Revision.Should().Be(0);
        f.Service.Invoking(service => service.Reset(draft)).Should().Throw<InvalidOperationException>();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal Fixture(bool current = false)
        {
            ActivitySource.AddActivityListener(listener);
            Session = new(new(Guid.NewGuid()), new(1), true);
            Snapshot = new(Session, 0, SessionProviderOverrides.Inherit, false);
            SourceSnapshot = new(new(new(Guid.NewGuid()), new(1), true), 0, SessionProviderOverrides.Inherit, false);
            Store = new(this);
            if (current)
            {
                Profiles.Catalogue.Status = ProviderCatalogueStatus.Current;
                Profiles.Preferences.Value = new(ProviderService.Local,
                    [new(ProviderService.Local, new("account"), new(1), new("exact-model"), new("managed-effort"), new("managed-window"))]);
            }
            Workspace = new(Store, new(Store), Access, NullLogger<SessionWorkspaceService>.Instance);
            Service = new(Workspace, Store, Profiles.Preferences, Profiles.Catalogue, Profiles.Time,
                Logger, Profiles.Service);
        }
        internal SessionStore Store { get; }
        internal AccessState Access { get; } = new();
        internal ProviderProfileConfigurationFixture Profiles { get; } = new();
        internal WorkSessionAuthorization Session { get; }
        internal SessionProviderProfile Snapshot { get; set; }
        internal SessionProviderProfile SourceSnapshot { get; set; }
        internal Action? BeforeCommit { get; set; }
        internal Action<HostId<SessionIdentity>>? AfterProfileRead { get; set; }
        internal bool FailReadback { get; set; }
        internal SessionWorkspaceService Workspace { get; }
        internal SessionProviderProfileConfigurationService Service { get; }
        internal bool Eligible { get; set; } = true;
        internal Exception? CommitFailure { get; set; }
        internal CaptureLogger Logger { get; } = new();
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal Task<SessionProviderDraft> Inspect(RequestOrigin origin = RequestOrigin.LocalUi) =>
            Service.Inspect(Session.SessionId, origin, () => Eligible, Token);
        internal Task<SessionProviderProposal> Copy(RequestOrigin origin = RequestOrigin.LocalUi) =>
            Service.ReviewCopy(SourceSnapshot.Session.SessionId, Session.SessionId, origin, () => Eligible, Token);
        public async ValueTask DisposeAsync() { await Profiles.DisposeAsync(); listener.Dispose(); }
        internal sealed class CaptureLogger : ILogger<SessionProviderProfileConfigurationService>
        {
            internal bool Enabled { get; set; } = true;
            internal List<HostRequest> Requests { get; } = [];
            public bool IsEnabled(LogLevel logLevel) => Enabled;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => Requests.Add(HostActivity.RequireCurrent().Request);
        }
        internal sealed class AccessState : ISessionWorkspaceAccess
        {
            public bool CanInspect { get; set; } = true;
            public bool CanControl { get; set; } = true;
            public long ControlRevision { get; set; } = 1;
        }
        internal sealed class SessionStore(Fixture owner) : AudioControlTestStore, ISessionProviderProfileStore
        {
            public override ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) =>
                ValueTask.FromResult(new SessionWorkspaceEntry(session == owner.SourceSnapshot.Session.SessionId ? owner.SourceSnapshot.Session : owner.Snapshot.Session, null));
            public override ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) =>
                ValueTask.FromResult<HostTaskObservation?>(new(Tasks.Last(record => record.Request.TaskId == task),
                    owner.Session.Generation, "fake-current-authority", true, null));
            public ValueTask<SessionProviderProfile> ReadProviderProfile(HostId<SessionIdentity> session, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var result = session == owner.SourceSnapshot.Session.SessionId ? owner.SourceSnapshot : owner.Snapshot;
                owner.AfterProfileRead?.Invoke(session);
                return ValueTask.FromResult(result);
            }
            public ValueTask<SessionProviderProfile> CommitProviderProfile(HostRequest request, SessionProviderProfile expected,
                SessionProviderOverrides desired, Func<bool> admitted, CancellationToken token, SessionProviderProfile? source = null)
            {
                owner.BeforeCommit?.Invoke();
                token.ThrowIfCancellationRequested();
                if (owner.CommitFailure is { } failure) { throw failure; }
                if (!admitted() || expected.Session != owner.Snapshot.Session || expected.Revision != owner.Snapshot.Revision) { throw new InvalidOperationException(); }
                if (source is not null && (source.Held || owner.SourceSnapshot.Held || source.Session != owner.SourceSnapshot.Session
                    || source.Revision != owner.SourceSnapshot.Revision
                    || !string.Equals(source.Desired.Serialize(), owner.SourceSnapshot.Desired.Serialize(), StringComparison.Ordinal)
                    || source.Session.SessionId == request.SessionId
                    || !string.Equals(desired.Serialize(), source.Desired.Serialize(), StringComparison.Ordinal)))
                { throw new InvalidOperationException(); }
                owner.Snapshot = owner.Snapshot with { Revision = owner.Snapshot.Revision + 1, Desired = desired };
                if (owner.FailReadback)
                {
                    owner.Snapshot = owner.Snapshot with { Held = true };
                    throw new IOException("Uncertain readback; inspect exact durable receipt, never retry.");
                }
                Tasks.Add(Tasks.Last(record => record.Request.TaskId == request.TaskId).Next(HostTaskState.Succeeded));
                return ValueTask.FromResult(owner.Snapshot);
            }
        }
    }
}
