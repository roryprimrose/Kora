using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Skills;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false, "current")]
    [InlineData(true, "current")]
    [InlineData(false, "current-ui")]
    [InlineData(true, "current-ui")]
    [InlineData(false, "save-before")]
    [InlineData(true, "save-before")]
    [InlineData(false, "save-review")]
    [InlineData(true, "save-review")]
    [InlineData(false, "save-confirm")]
    [InlineData(true, "save-confirm")]
    [InlineData(false, "save-publish")]
    [InlineData(true, "save-publish")]
    [InlineData(false, "proposal-review")]
    [InlineData(true, "proposal-review")]
    public async Task StagedSkillPublicationRequiresSeparateSaveInputAndOriginalProposal(bool initial, string withdrawal)
    {
        await using var f = new SkillRefinementFixture(withdrawal is "current-ui"
            ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        f.Model.Answer("Complete staged guidance");
        var proposal = initial
            ? (await f.Service.ProposeInitial(f.Draft, new("Purpose", "Input", "Output", f.Authority),
                (_, _) => Task.FromResult(true), f.Eligible, Token))!
            : (await f.Propose())!;
        var saveCurrent = true;
        var authority = new OwnedSkillAuthoringAuthority(withdrawal is "current-ui"
            ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice, () => saveCurrent);
        void Withdraw()
        {
            if (withdrawal is "proposal-review") { f.Authority.Retire(); }
            else { saveCurrent = false; }
        }
        if (withdrawal is "save-before") { Withdraw(); }
        var review = new SkillRefinementReview
        {
            BeforeReview = withdrawal is "save-review" or "proposal-review" ? Withdraw : null,
            BeforeConfirm = withdrawal is "save-confirm" ? Withdraw : null,
        };
        f.Store.BeforePublish = withdrawal is "save-publish" ? Withdraw : null;
        var saving = () => f.Service.Save(proposal, authority, review, f.Eligible, Token);
        if (withdrawal is "current" or "current-ui")
        {
            var saved = (await saving())!;
            saved.Document.Guidance.Should().Be("Complete staged guidance");
            saved.Document.IsAvailableForInvocation.Should().BeFalse();
            f.Store.Writes.Should().Be(1);
            review.Calls.Should().Equal("review", "confirm");
            f.Audit.Events.Select(entry => entry.Outcome).Should().Equal(
                Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        }
        else
        {
            if (withdrawal is "save-review" or "proposal-review") { (await saving()).Should().BeNull(); }
            else { await saving.Should().ThrowAsync<InvalidOperationException>(); }
            f.Store.Writes.Should().Be(0);
            f.Store.Target.Should().BeNull();
            f.Audit.Events.Should().NotContain(entry => entry.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
            review.Calls.Should().HaveCount(withdrawal is "save-before" ? 0
                : withdrawal is "save-review" or "proposal-review" ? 1 : 2);
        }
        var initiator = authority.Origin == RequestOrigin.ActivatedVoice
            ? Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand : Kora.Core.Auditing.SecurityAuditInitiator.LocalUser;
        f.Audit.Events.Should().OnlyContain(entry => entry.Initiator == initiator);
        f.Model.Model.RetainCalls.Should().Be(0);
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task OwnedGuidanceRefinementCompletesExactComparisonAndSeparateImmutableSave(RequestOrigin origin, bool inputs)
    {
        await using var f = new SkillRefinementFixture(origin, inputs);
        var proposed = inputs ? "  NEW ${input:topic}\r\nowned skill save  " : "  NEW e\u0301\U0001f642\r\nowned skill save  ";
        f.Model.Answer(proposed);
        const string critique = "  Original e\u0301\U0001f642 critique\r\nMake it clearer.  ";
        var reviewed = false;
        var proposal = await f.Propose((candidate, _) =>
        {
            reviewed = true;
            candidate.Draft.Should().BeSameAs(f.Draft);
            candidate.Critique.Should().Be(critique);
            candidate.Proposed.Guidance.Should().Be(proposed);
            candidate.Proposed.ManifestBytes.ToArray().Should().Equal(f.Draft.ManifestBytes.ToArray());
            candidate.Proposed.InputTemplate?.RequiredNames.Should().Equal("topic");
            f.Store.Writes.Should().Be(0);
            return Task.FromResult(true);
        }, critique);
        reviewed.Should().BeTrue();
        proposal!.IsCurrent().Should().BeTrue();
        using var wire = JsonDocument.Parse(f.Model.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(critique);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(f.Draft.Guidance);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
        var review = new SkillRefinementReview { Confirm = false };
        (await f.Service.Save(proposal, f.Authority, review, f.Eligible, Token)).Should().BeNull();
        f.Store.Writes.Should().Be(0);
        review.Confirm = true;
        var saved = (await f.Service.Save(proposal, f.Authority, review, f.Eligible, Token))!;
        saved.Document.ManifestBytes.ToArray().Should().Equal(f.Draft.ManifestBytes.ToArray());
        saved.Document.Guidance.Should().Be(proposed);
        saved.Document.IsAvailableForInvocation.Should().BeFalse();
        review.Calls.Should().Equal("review", "confirm", "review", "confirm");
        f.Store.Writes.Should().Be(1);
        f.Model.Model.RetainCalls.Should().Be(0);
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
        f.Audit.Events.Should().OnlyContain(entry => entry.Initiator == (origin == RequestOrigin.ActivatedVoice
            ? Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand : Kora.Core.Auditing.SecurityAuditInitiator.LocalUser));
        f.Model.Model.Messages.Should().NotContain(message => message.Contains(critique, StringComparison.Ordinal)
            || message.Contains(proposed, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("draft")]
    [InlineData("authority")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("target")]
    [InlineData("source-directory")]
    [InlineData("source-manifest")]
    [InlineData("source-guidance")]
    [InlineData("source-bytes")]
    [InlineData("source-missing")]
    [InlineData("review-source")]
    [InlineData("review-draft")]
    public async Task OwnedGuidanceNeverDisclosesLateOrStaleProposal(string change)
    {
        await using var f = new SkillRefinementFixture(source: true);
        f.Model.Answer("NEW");
        f.Model.Model.Adapter.OnRun = () =>
        {
            switch (change)
            {
                case "draft": f.Open = false; break;
                case "authority": f.Authority.Retire(); break;
                case "privacy": f.Model.Model.CanControl = false; break;
                case "owner": f.Model.Model.Current = false; break;
                case "expiry": f.Model.Model.Time.Now += TimeSpan.FromMinutes(3); break;
                case "clock": f.Model.Model.Time.Now -= TimeSpan.FromMinutes(1); break;
                case "target": f.Store.Target = new(f.Draft, "target", "manifest", "guidance"); break;
                default: f.ChangeSource(change); break;
            }
        };
        var action = () => f.Propose((_, _) =>
        {
            if (change is "review-source") { f.ChangeSource("source-directory"); }
            if (change is "review-draft") { f.Open = false; }
            return Task.FromResult(change is not "reject");
        });
        if (change is "reject") { (await action()).Should().BeNull(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.Draft.Guidance.Should().Be("CURRENT");
        f.Store.Writes.Should().Be(0);
        f.Model.Model.RetainCalls.Should().Be(0);
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("blank")]
    [InlineData("unicode")]
    [InlineData("large-critique")]
    [InlineData("complete-input")]
    [InlineData("target")]
    [InlineData("source")]
    public async Task OwnedGuidanceInvalidInputNeverReachesProvider(string invalid)
    {
        await using var f = new SkillRefinementFixture(source: invalid is "source");
        var draft = invalid is "tool" ? OwnedDeclarativeSkillDocument.Create("example", "2.0.0", "Example", "Description",
            "CURRENT", OwnedSkillToolSelection.WebPageGet) : f.Draft;
        if (invalid is "target") { f.Store.Target = new(f.Draft, "target", "manifest", "instructions"); }
        if (invalid is "source") { f.Store.Source = null; }
        var critique = invalid switch
        {
            "blank" => " \r\n",
            "unicode" => "\uD800",
            "large-critique" => new string('x', 65537),
            "complete-input" => new string('x', 32768),
            _ => "original critique",
        };
        await f.Service.Invoking(service => service.Propose(draft, f.Source, critique, f.Authority,
            (_, _) => Task.FromResult(true), f.Eligible, Token)).Should().ThrowAsync<Exception>();
        f.Model.Model.Adapter.Calls.Should().Be(0);
        f.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("{\"body\":\"NEW\",\"id\":\"other\"}", false)]
    [InlineData("```json\n{\"body\":\"NEW\"}\n```", false)]
    [InlineData("{\"body\":\"\\uD800\"}", false)]
    [InlineData("{\"body\":\"  \"}", false)]
    [InlineData("{\"body\":\"new ${input:other}\"}", true)]
    [InlineData("{\"body\":\"new without inputs\"}", true)]
    public async Task OwnedGuidanceInvalidOutputNeverChangesDraftOrMetadata(string answer, bool inputs)
    {
        await using var f = new SkillRefinementFixture(inputs: inputs);
        f.Model.Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(answer, null));
        await f.Invoking(value => value.Propose()).Should().ThrowAsync<InvalidDataException>();
        f.Store.Writes.Should().Be(0);
        f.Model.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    public async Task OwnedGuidanceProviderFailureHasNoFallbackOrDraftWrite(ModelTurnOutcome outcome)
    {
        await using var f = new SkillRefinementFixture();
        f.Model.Model.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate);
        await f.Invoking(value => value.Propose()).Should().ThrowAsync<InvalidOperationException>();
        f.Model.Model.Adapter.Calls.Should().Be(1);
        f.Store.Writes.Should().Be(0);
        f.Model.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("draft")]
    [InlineData("foreign")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("confirm-source")]
    [InlineData("commit-source")]
    [InlineData("cancel")]
    public async Task OwnedGuidanceSaveNeverRenewsReviewedAuthority(string change)
    {
        await using var f = new SkillRefinementFixture(source: true);
        f.Model.Answer("NEW");
        var proposal = (await f.Propose())!;
        var review = new SkillRefinementReview();
        switch (change)
        {
            case "expiry": f.Model.Model.Time.Now += TimeSpan.FromMinutes(3); break;
            case "draft": f.Open = false; break;
            case "source": f.Store.Source = null; break;
            case "target": f.Store.Target = new(f.Draft, "target", "manifest", "instructions"); break;
            case "confirm-source": review.BeforeConfirm = () => f.Store.Source = null; break;
            case "commit-source": f.Store.BeforePublish = () => f.Store.Source = null; break;
        }
        await using var other = new SkillRefinementFixture();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (change is "cancel") { cancel.Cancel(); }
        var service = change is "foreign" ? other.Service : f.Service;
        await service.Invoking(value => value.Save(proposal, f.Authority, review, f.Eligible, cancel.Token)).Should().ThrowAsync<Exception>();
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task OwnedGuidanceCancellationJoinsIgnoringProviderBeforeReturning()
    {
        await using var f = new SkillRefinementFixture();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Model.Model.Adapter.OnRun = () => started.SetResult();
        f.Model.Model.Adapter.Pending = pending.Task;
        var operation = f.Service.Propose(f.Draft, null, "original critique", f.Authority,
            (_, _) => throw new InvalidOperationException("No late review allowed"), f.Eligible, cancel.Token);
        await started.Task;
        await cancel.CancelAsync();
        operation.IsCompleted.Should().BeFalse();
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeFalse();
        pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("{\"body\":\"LATE\"}", null)));
        await operation.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
        f.Store.Writes.Should().Be(0);
    }

    private sealed class SkillRefinementFixture : IAsyncDisposable
    {
        internal RefinementFixture Model { get; }
        internal SkillRefinementStore Store { get; } = new();
        internal OwnedDeclarativeSkillSnapshot? Source { get; }
        internal OwnedDeclarativeSkillDocument Draft { get; }
        internal OwnedSkillAuthoringAuthority Authority { get; }
        internal SharedSkillAudit Audit { get; } = new();
        internal OwnedSkillGuidanceRefinementService Service { get; }
        private readonly OwnedSkillControlAdmission admission;
        internal bool Open = true;
        internal bool Eligible() => Open;
        internal SkillRefinementFixture(RequestOrigin origin = RequestOrigin.LocalUi, bool inputs = false, bool source = false)
        {
            Model = new(origin);
            Draft = OwnedDeclarativeSkillDocument.Create("example", "2.0.0", "Example", "Description",
                inputs ? "CURRENT ${input:topic}" : "CURRENT", guidanceFormat: inputs
                    ? OwnedSkillGuidanceFormat.RequiredLiteralInputs : OwnedSkillGuidanceFormat.Literal);
            if (source)
            {
                Source = new(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "OLD"),
                    "directory", "manifest", "instructions");
                Store.Source = Source;
            }
            Authority = new(origin, Eligible);
            admission = new(Model.Model, Model.Model, new(Model.Model));
            Service = new(Store, admission, new(Store, admission, Audit), Model.Runner, Model.Model.Time);
        }
        internal Task<OwnedSkillGuidanceRefinement?> Propose(
            Func<OwnedSkillGuidanceRefinement, CancellationToken, Task<bool>>? review = null, string critique = "original critique") =>
            Service.Propose(Draft, Source, critique, Authority, review ?? ((_, _) => Task.FromResult(true)), Eligible, Token);
        internal void ChangeSource(string change)
        {
            var before = Store.Source!;
            Store.Source = change switch
            {
                "source-directory" => before with { DirectoryIdentity = "replaced" },
                "source-manifest" => before with { ManifestIdentity = "replaced" },
                "source-guidance" => before with { InstructionsIdentity = "replaced" },
                "source-bytes" => before with { Document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "OTHER") },
                "source-missing" => null,
                _ => before,
            };
        }
        public async ValueTask DisposeAsync()
        {
            await admission.DisposeAsync();
            await Model.DisposeAsync();
        }
    }

    private sealed partial class Fixture : IOwnedSkillControlSessionStore
    {
        public ValueTask<WorkSessionAuthorization> CreateOwnedSkillSession(HostRequest request, Func<bool> admitted, CancellationToken token) =>
            CreateInstructionSession(request, admitted, token);
        public ValueTask<T> WithOwnedSkillSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token) =>
            WithInstructionSession(request, generation, operation, token);
    }

    private sealed class SkillRefinementStore : IOwnedDeclarativeSkillStore
    {
        internal OwnedDeclarativeSkillSnapshot? Source;
        internal OwnedDeclarativeSkillSnapshot? Target;
        internal Action? BeforePublish;
        internal Func<Task>? BeforeRead;
        internal int Writes;
        internal string Path = "fixed-owned-destination";
        internal IReadOnlyList<OwnedDeclarativeSkillSnapshot> Catalogue = [];
        public string Destination(OwnedDeclarativeSkillDocument document) => Path;
        public Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token) => Task.FromResult(Catalogue);
        public async Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token,
            OwnedSkillReadScope scope = OwnedSkillReadScope.CompletePackage, OwnedDeclarativeSkillSnapshot? expectedPrimary = null)
        {
            if (BeforeRead is not null) { await BeforeRead(); }
            return version is "1.0.0" ? Source : Target;
        }
        public Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token,
            OwnedDeclarativeSkillSnapshot? expectedSource = null)
        {
            BeforePublish?.Invoke();
            return commit(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!eligible() || Target is not null) { throw new InvalidOperationException("Publication retired"); }
                Writes++;
                Target = new(document, "target", "manifest", "instructions");
                return Target;
            });
        }
    }

    private sealed class SkillRefinementReview : IOwnedSkillSaveReview
    {
        internal bool Confirm = true;
        internal Action? BeforeReview;
        internal Action? BeforeConfirm;
        internal List<string> Calls { get; } = [];
        public Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token)
        { Calls.Add("review"); BeforeReview?.Invoke(); return Task.FromResult(true); }
        public Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token)
        { Calls.Add("confirm"); BeforeConfirm?.Invoke(); return Task.FromResult(Confirm); }
    }
}
