using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task OwnedInstructionRefinementUsesExactBodyAndCritiqueUntilConfirmedNormalSave(RequestOrigin origin, bool legacy)
    {
        await using var f = new RefinementFixture(origin, legacy: legacy);
        using var root = f.Model.Root();
        var original = f.Store.Current!;
        var draft = original.Document.Text.Replace("OLD_BODY", "CURRENT_DRAFT", StringComparison.Ordinal);
        const string critique = "  Preserve e\u0301\U0001f642.\r\nMake this clearer, not policy.  ";
        const string replacement = "  NEW_BODY e\u0301\U0001f642\r\n---\nname: inert-body-data\n";
        f.Answer(replacement);
        var reviewed = false;
        var proposal = await f.Service.Propose(original, draft, critique, (candidate, _) =>
        {
            candidate.Draft.Should().Be(draft);
            candidate.Critique.Should().Be(critique);
            candidate.Proposed.Body.Should().Be(replacement);
            candidate.Proposed.Metadata["description"].Should().Be("Exact host metadata");
            candidate.Proposed.HasBom.Should().BeTrue();
            candidate.Proposed.Metadata["kora-format"].Should().Be("1");
            f.Store.Writes.Should().Be(0);
            f.Store.Current.Should().BeSameAs(original);
            reviewed = true;
            return Task.FromResult(true);
        }, f.Eligible, Token);
        reviewed.Should().BeTrue();
        proposal!.Origin.Should().Be(origin);
        proposal.IsCurrent().Should().BeTrue();
        using var wire = JsonDocument.Parse(f.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(critique);
        var evidence = wire.RootElement.GetProperty("Evidence");
        evidence.GetArrayLength().Should().Be(1);
        evidence[0].GetProperty("Content").GetString().Should().Be(original.Document.Body.Replace("OLD_BODY", "CURRENT_DRAFT", StringComparison.Ordinal));
        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().NotContain("CURRENT_DRAFT").And.NotContain(critique);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        f.Store.Writes.Should().Be(0);
        f.Model.RetainCalls.Should().Be(0);
        var review = new RefinementSaveReview { Confirmed = false };
        (await f.Service.Save(proposal, review, f.Eligible, Token)).Saved.Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        review.Confirmed = true;
        var saved = await f.Service.Save(proposal, review, f.Eligible, Token);
        saved.Saved.Should().BeTrue();
        f.Store.Current!.Document.CopyBytes().Should().Equal(proposal.Proposed.CopyBytes());
        review.Proposed.Should().Be(proposal.Proposed.Text);
        f.Catalogue.Publish(f.Revisions.Reconcile([f.Store.Current.Document.Definition with { OwnedInstructionRevision = f.Store.Current.Revision }]));
        f.Catalogue.Artifacts.Should().BeEmpty();
        (await f.Actions.Activate(f.Store.Current, _ => Task.FromResult(true), f.Eligible, Token)).Should().BeTrue();
        f.Catalogue.Artifacts.Should().ContainSingle();
        f.Model.RetainCalls.Should().Be(0);
        f.Model.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("cancel-review")]
    [InlineData("source-before")]
    [InlineData("source-during")]
    [InlineData("source-review")]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("draft")]
    [InlineData("expiry")]
    [InlineData("host")]
    [InlineData("source-unavailable")]
    public async Task OwnedInstructionRefinementCannotPublishLateBodyOrWriteAfterRevocation(string change)
    {
        await using var f = new RefinementFixture();
        var original = f.Store.Current!;
        var draft = original.Document.Text;
        f.Answer("NEW_BODY");
        if (change is "source-before") { f.Store.Current = f.Store.Changed(); }
        f.Model.Adapter.OnRun = () =>
        {
            switch (change)
            {
                case "source-during": f.Store.Current = f.Store.Changed(); break;
                case "missing": f.Store.Current = null; break;
                case "identity": f.Store.Current = original with { FileIdentity = "replacement-identity" }; break;
                case "draft": f.Open = false; break;
                case "expiry": f.Model.Time.Now += TimeSpan.FromMinutes(3); break;
                case "host": f.Model.Current = false; break;
                case "source-unavailable": f.Catalogue.ReportReloadFailure(new IOException("Synthetic unavailable")); break;
            }
        };
        var operation = () => f.Service.Propose(original, draft, "exact critique", (_, _) =>
        {
            if (change is "source-review") { f.Store.Current = f.Store.Changed(); }
            return Task.FromResult(change is not "cancel-review");
        }, f.Eligible, Token);
        if (change is "cancel-review") { (await operation()).Should().BeNull(); }
        else { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        original.Document.Text.Should().Be(draft);
        f.Store.Writes.Should().Be(0);
        f.Model.RetainCalls.Should().Be(0);
        f.Model.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("missing")]
    [InlineData("owner")]
    [InlineData("expiry")]
    [InlineData("save-review-source")]
    [InlineData("save-review-privacy")]
    [InlineData("save-review-cancel")]
    [InlineData("different-service")]
    public async Task OwnedInstructionRefinementSaveCannotCleanseRevocationOrOverwriteAChangedBase(string change)
    {
        await using var f = new RefinementFixture(RequestOrigin.ActivatedVoice);
        using var root = f.Model.Root();
        f.Answer("NEW_BODY");
        var proposal = (await f.Propose())!;
        var review = new RefinementSaveReview();
        switch (change)
        {
            case "source": f.Store.Current = f.Store.Changed(); break;
            case "missing": f.Store.Current = null; break;
            case "owner": f.Open = false; break;
            case "expiry": f.Model.Time.Now += TimeSpan.FromMinutes(3); break;
            case "save-review-source": review.BeforeConfirm = () => f.Store.Current = f.Store.Changed(); break;
            case "save-review-privacy": review.BeforeConfirm = () => f.Open = false; break;
            case "save-review-cancel": review.BeforeConfirm = () => throw new OperationCanceledException(); break;
        }
        var service = change is "different-service" ? f.CreateService() : f.Service;
        var operation = () => service.Save(proposal, review, f.Eligible, Token);
        if (change is "save-review-cancel") { await operation.Should().ThrowAsync<OperationCanceledException>(); }
        else { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        f.Store.Writes.Should().Be(0);
        review.Conflicts.Should().Be(0, "refinement never offers Overwrite on a changed base");
        f.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("```json\n{\"body\":\"NEW_BODY\"}\n```")]
    [InlineData("{\"body\":\"NEW_BODY\",\"path\":\"other\"}")]
    [InlineData("{\"body\":\"one\",\"body\":\"two\"}")]
    [InlineData("{\"body\":42}")]
    [InlineData("{\"files\":[]}")]
    [InlineData("[]")]
    [InlineData("{\"body\":\"  \\r\\n\"}")]
    [InlineData("{\"body\":\"\\uD800\"}")]
    public async Task OwnedInstructionRefinementNeverStripsTruncatesOrReinterpretsInvalidOutput(string answer)
    {
        await using var f = new RefinementFixture();
        f.Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(answer, null));
        await f.Invoking(item => item.Propose()).Should().ThrowAsync<Exception>();
        f.Store.Writes.Should().Be(0);
        f.Store.Current!.Document.Body.Should().Contain("OLD_BODY");
        f.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task OwnedInstructionRefinementNeverFallsBackOrRetainsDefinitionContentAfterModelFailure(ModelTurnOutcome outcome)
    {
        await using var f = new RefinementFixture();
        f.Model.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate);
        await f.Invoking(item => item.Propose()).Should().ThrowAsync<InvalidOperationException>();
        f.Store.Writes.Should().Be(0);
        f.Model.Adapter.Calls.Should().Be(1);
        f.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("unicode")]
    [InlineData("body-envelope")]
    [InlineData("critique-envelope")]
    [InlineData("prompt-actions")]
    [InlineData("prompt-source")]
    [InlineData("profile")]
    public async Task OwnedInstructionRefinementRejectsInvalidFrozenInputBeforeDisclosure(string invalid)
    {
        await using var f = new RefinementFixture();
        var original = f.Store.Current!;
        var critique = "critique";
        var draft = original.Document.Text;
        var service = f.Service;
        switch (invalid)
        {
            case "empty": critique = "  "; break;
            case "unicode": critique = "\ud800"; break;
            case "body-envelope": draft += new string('<', 6000); break;
            case "critique-envelope": critique = new string('x', ModelContextEnvelope.MaximumInputUtf8Bytes + 1); break;
            case "prompt-actions": service = f.CreateService(ArtifactKind.Prompt); break;
            case "prompt-source": original = original with { Document = ArtifactFileDocument.Parse(original.Document.CopyBytes(), ArtifactKind.Prompt, "kora-profile", "example") }; break;
            case "profile": original = original with { Document = ArtifactFileDocument.Parse(original.Document.CopyBytes(), ArtifactKind.Instruction, "profile", "example") }; break;
        }
        var operation = () => service.Propose(original, draft, critique, (_, _) => Task.FromResult(true), f.Eligible, Token);
        await operation.Should().ThrowAsync<InvalidDataException>();
        f.Model.Adapter.Calls.Should().Be(0);
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task OwnedInstructionRefinementRetainsModelOwnershipUntilIgnoredCancellationJoins()
    {
        await using var f = new RefinementFixture();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Model.Adapter.OnRun = () => started.SetResult();
        f.Model.Adapter.Pending = reply.Task;
        var operation = f.Service.Propose(f.Store.Current!, f.Store.Current!.Document.Text, "critique",
            (_, _) => Task.FromResult(true), f.Eligible, cancellation.Token);
        (await Task.WhenAny(started.Task, operation)).Should().BeSameAs(started.Task, "the real model adapter must start before cancellation is exercised");
        await started.Task.WaitAsync(Token);
        cancellation.Cancel();
        operation.IsCompleted.Should().BeFalse();
        f.Model.PolicyHost.IsQuiescent.Should().BeFalse();
        reply.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("{\"body\":\"LATE_BODY\"}", null)));
        await ((Func<Task>)(async () => await operation)).Should().ThrowAsync<OperationCanceledException>();
        f.Model.PolicyHost.IsQuiescent.Should().BeTrue();
        f.Store.Writes.Should().Be(0);
        f.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("disabled")]
    [InlineData("unavailable")]
    [InlineData("name")]
    [InlineData("revision")]
    [InlineData("digest")]
    [InlineData("prompt")]
    public async Task OwnedInstructionRefinementObservesSourceIdentityIndependentlyOfReviewedUse(string change)
    {
        await using var f = new RefinementFixture();
        var original = f.Store.Current!;
        var candidate = f.Catalogue.Candidates.Single();
        var actions = f.Actions;
        switch (change)
        {
            case "disabled": candidate = candidate with { IsUseEnabled = false, PreservesCapturedUse = false }; break;
            case "unavailable": f.Catalogue.ReportReloadFailure(new IOException("Synthetic unavailable")); break;
            case "name": candidate = candidate with { OwnedInstructionName = "different" }; break;
            case "revision": candidate = candidate with { OwnedInstructionRevision = "different" }; break;
            case "digest": candidate = original.Document.WithInstructionBody("different").Definition with { OwnedInstructionRevision = original.Revision }; break;
            case "prompt": actions = new(f.Store, f.Admission, f.Revisions, f.Catalogue, f.Model, new(new()), ArtifactKind.Prompt); break;
        }
        if (change is not "unavailable") { f.Catalogue.Publish([candidate]); }
        actions.IsRefinementSourceObserved(original).Should().Be(change is "same" or "disabled");
    }

    [Fact]
    public async Task OwnedInstructionRefinementSaveWithoutAmbientInputStillRequiresCurrentExplicitSaveEligibility()
    {
        await using var f = new RefinementFixture();
        f.Answer("NEW_BODY");
        var proposal = (await f.Propose())!;
        await f.Service.Invoking(service => service.Save(proposal, new RefinementSaveReview(), () => false, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Store.Writes.Should().Be(0);
        (await f.Service.Save(proposal, new RefinementSaveReview(), f.Eligible, Token)).Saved.Should().BeTrue();
        f.Store.Writes.Should().Be(1);
    }

    private sealed class RefinementFixture : IAsyncDisposable
    {
        internal Fixture Model { get; }
        internal RefinementStore Store { get; } = new();
        internal InstructionControlAdmission Admission { get; }
        internal InstructionRevisionCatalogue Revisions { get; } = new(new Kora.Application.UnitTests.Configuration.LocalSharedSkillPreferencesTests.Store());
        internal ArtifactCatalogue Catalogue { get; }
        internal OwnedInstructionEditorActions Actions { get; }
        internal OwnedInstructionRefinementService Service { get; }
        internal LocalContextTurnWorkflow Runner { get; }
        internal bool Open = true;
        internal bool Eligible() => Open;
        internal RefinementFixture(RequestOrigin origin = RequestOrigin.LocalUi, bool legacy = false)
        {
            Model = new(origin);
            if (legacy) { Store.Current = Store.Current! with { Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                Store.Current.Document.Text.Replace("kora-format: 1\r\n", "", StringComparison.Ordinal), true), ArtifactKind.Instruction, "kora-profile", "example") }; }
            Catalogue = new(Revisions.Reconcile([Store.Current!.Document.Definition with { OwnedInstructionRevision = Store.Current.Revision }]));
            Admission = new(Model, Model, new(Model));
            Actions = new(Store, Admission, Revisions, Catalogue, Model, new(new()));
            Runner = new(Model, new RefinementContextTasks(Model),
                new SessionWorkspaceService(Model, new(Model), Model, NullLogger<SessionWorkspaceService>.Instance),
                new(Model), Model, Model.PolicyHost, Model.PolicyHost, new RefinementContextAudit(Model), NullLogger<LocalContextTurnWorkflow>.Instance);
            Service = CreateService();
        }
        internal OwnedInstructionRefinementService CreateService(ArtifactKind kind = ArtifactKind.Instruction) =>
            new(kind == ArtifactKind.Instruction ? Actions : new(Store, Admission, Revisions, Catalogue, Model, new(new()), kind),
                Store, Admission, Runner, Model.Time);
        internal void Answer(string body) =>
            Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new(JsonSerializer.Serialize(new { body }), null));
        internal Task<OwnedInstructionRefinement?> Propose() =>
            Service.Propose(Store.Current!, Store.Current!.Document.Text, "exact critique", (_, _) => Task.FromResult(true), Eligible, Token);
        public async ValueTask DisposeAsync()
        {
            await Admission.DisposeAsync();
            await Runner.DisposeAsync();
            Catalogue.Dispose();
            Model.Dispose();
        }

        private sealed class RefinementContextTasks(Fixture model) : ILocalContextTaskStore
        {
            public ValueTask<HostTaskRecord> AdmitLocalContextDispatchAsync(HostRequest request, HostRevision generation, Func<bool> eligible, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Context source retired"); }
                var dispatch = model.Task!.Task.Next(HostTaskState.DispatchRecorded);
                model.Task = model.Task with { Task = dispatch, Generation = generation, CurrentSource = true };
                return ValueTask.FromResult(dispatch);
            }
        }

        private sealed class RefinementContextAudit(Fixture model) : ISecurityAuditLog
        {
            public void Write(SecurityAuditEvent auditEvent)
            {
                HostActivity.RequireCurrent().Request.Origin.Should().BeOneOf(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
                model.Audits.Add(auditEvent);
            }
        }
    }

    private sealed class RefinementStore : IOwnedInstructionStore
    {
        internal OwnedInstructionSnapshot? Current = new("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\r\nkora-format: 1\r\nname: 'example'\r\ndescription: Exact host metadata\r\n---\r\n  OLD_BODY e\u0301\U0001f642\r\n", true),
                ArtifactKind.Instruction, "kora-profile", "example"));
        internal int Writes;
        internal OwnedInstructionSnapshot Changed() => Current! with { FileIdentity = Guid.NewGuid().ToString("N") };
        public Task<IReadOnlyList<string>> List(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["example"]);
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token) => Task.FromResult(Current);
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
            Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            commit(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Host closed"); }
                if (!string.Equals(Current?.Revision, expected?.Revision, StringComparison.Ordinal)) { return new(false, false, true, Current); }
                Writes++;
                Current = expected is null ? new(name, new string('a', 48), new string('c', 48), replacement!)
                    : expected with { Document = replacement! };
                return new(true, false, false, Current);
            });
    }

    private sealed class RefinementSaveReview : IInstructionMutationReview
    {
        internal bool Confirmed = true;
        internal Action? BeforeConfirm;
        internal int Conflicts;
        internal string? Proposed;
        public Task<bool> Confirm(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? proposed, CancellationToken token)
        {
            Proposed = proposed!.Text;
            BeforeConfirm?.Invoke();
            return Task.FromResult(Confirmed);
        }
        public Task<InstructionConflictChoice> Conflict(OwnedInstructionSnapshot? observed, ArtifactFileDocument? proposed, CancellationToken token)
        { Conflicts++; return Task.FromResult(InstructionConflictChoice.Overwrite); }
        public Task<bool> ConfirmCopy(OwnedDefinitionCopy copy, CancellationToken token) => throw new NotSupportedException();
    }
}
