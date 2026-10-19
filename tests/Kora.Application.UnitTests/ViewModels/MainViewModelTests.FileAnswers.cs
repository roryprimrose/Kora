using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("answer file")]
    [InlineData("Kora, answer file")]
    public async Task ExactAnswerRequestOpensOnlyCurrentSingleFileNativeQuestionWithoutModelUse(string command)
    {
        var fixture = new Fixture();
        using var preview = new LocalFilePreview(new FakeFileInspector(), fixture.Audit,
            TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        var opened = 0;
        fixture.ViewModel.FileAnswerRequested += (_, _) => opened++;
        await fixture.RunAsync(command);
        opened.Should().Be(0);
        await fixture.ViewModel.PreviewFileAsync();
        await fixture.ViewModel.ConfirmFilePreviewAsync(fixture.ViewModel.FileReview!.ReviewId);
        await fixture.RunAsync(command);
        opened.Should().Be(1);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.ClipboardReader.Calls.Should().Be(0);
        fixture.ViewModel.ClearFilePreview();
        await fixture.RunAsync(command);
        opened.Should().Be(1);
    }

    [Fact]
    public async Task FileAnswerIsDeniedWithoutBoundActionAndStaleSourceNeverGetsImplicitlyChosen()
    {
        var fixture = new Fixture();
        var source = new LocalFileReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "unadmitted");
        (await fixture.ViewModel.AnswerFileAsync(source, "question", () => true, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
    }

    [Fact]
    public async Task ProductionSourcePolicyRefusesBeforeSharedAdmissionEvidenceProviderOrBodyPersistence()
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(new FakeFileInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var runner = new NativeFileAnswerModel();
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        f.ViewModel.BindFileAnswer(new(preview, new LocalFileLexicalRetrieval(), new UnavailableLocalFileModelUsePolicy(), runner,
            f.Audit, TimeProvider.System, NullLogger<LocalFileAnswer>.Instance));
        await f.ViewModel.PreviewFileAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FileReview!.ReviewId);
        var source = f.ViewModel.FileRevision!;
        const string question = "private marker";
        var result = await f.ViewModel.AnswerFileAsync(source.Reference, question, () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.SourceUseUnavailable);
        result.SourceUseDecision.Should().Be(LocalFileModelUseDecision.SourceUsePolicyMissing);
        result.Claims.Should().BeEmpty();
        result.ExecutionReason.Should().BeNull();
        result.ExecutionState.Should().BeNull();
        runner.AdmissionCalls.Should().Be(0);
        runner.Context.Should().BeNull();
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.ViewModel.ResponseBody.Should().NotContain(FakeFileInspector.Text);
        f.ViewModel.Transcript.Should().NotContain(question);
    }

    [Theory]
    [InlineData("unsupported", LocalFileAnswerOutcome.Unsupported)]
    [InlineData("success", LocalFileAnswerOutcome.Succeeded)]
    [InlineData("cancelled", LocalFileAnswerOutcome.Cancelled)]
    [InlineData("unavailable", LocalFileAnswerOutcome.Unavailable)]
    [InlineData("none", LocalFileAnswerOutcome.NoEvidence)]
    public async Task NativeFileAnswerUsesNewLinkedProvenanceAndNeverConversationOrLegacyReasoner(string outcome, LocalFileAnswerOutcome expected)
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(new FakeFileInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var model = new NativeFileAnswerModel { Kind = outcome };
        var action = new LocalFileAnswer(preview, new LocalFileLexicalRetrieval(), new NativeFileAnswerPolicy(), model,
            f.Audit, TimeProvider.System, NullLogger<LocalFileAnswer>.Instance);
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        f.ViewModel.BindFileAnswer(action);
        var duplicate = () => f.ViewModel.BindFileAnswer(action);
        duplicate.Should().Throw<InvalidOperationException>();
        var openings = 0;
        f.ViewModel.FileAnswerRequested += (_, _) => openings++;
        await f.ViewModel.PreviewFileAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FileReview!.ReviewId);
        var source = f.ViewModel.FileRevision!;
        await f.ViewModel.RequestFileAnswerAsync();
        openings.Should().Be(1);
        const string question = "  private marker? ";
        var result = await f.ViewModel.AnswerFileAsync(source.Reference,
            string.Equals(outcome, "none", StringComparison.Ordinal) ? "absent" : question, () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(expected);
        if (model.Context is { } context)
        {
            context.UserRequest.Should().Be(question);
            context.Request.Origin.Should().Be(RequestOrigin.LocalUi);
            context.Request.SessionId.Should().Be(source.Review.Request.SessionId);
            context.Request.TaskId.Should().Be(source.Review.Request.TaskId);
            context.Request.RequestId.Should().NotBe(source.Review.Request.RequestId);
        }
        f.ViewModel.Transcript.Should().NotContain(question);
        f.ViewModel.ResponseBody.Should().NotContain(FakeFileInspector.Text);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        using var scope = HostActivity.BeginRoot(source.Review.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.ViewModel.RequestFileAnswerAsync();
        openings.Should().Be(2);
        (await f.ViewModel.AnswerFileAsync(source.Reference, "private", () => true, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(string.Equals(outcome, "none", StringComparison.Ordinal) ? LocalFileAnswerOutcome.Unsupported : expected);
    }

    [Theory]
    [InlineData("native", LocalFileAnswerOutcome.Denied)]
    [InlineData("owner", LocalFileAnswerOutcome.Denied)]
    [InlineData("system", LocalFileAnswerOutcome.Denied)]
    [InlineData("foreign", LocalFileAnswerOutcome.Denied)]
    [InlineData("task", LocalFileAnswerOutcome.Denied)]
    [InlineData("invocation", LocalFileAnswerOutcome.Denied)]
    [InlineData("stopped", LocalFileAnswerOutcome.Denied)]
    [InlineData("missing", LocalFileAnswerOutcome.Stale)]
    [InlineData("revision", LocalFileAnswerOutcome.Stale)]
    public async Task NativeFileAnswerCannotRebindForeignContextOrImplicitlySelectMissingSource(string boundary, LocalFileAnswerOutcome expected)
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(new FakeFileInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var model = new NativeFileAnswerModel();
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        f.ViewModel.BindFileAnswer(new(preview, new LocalFileLexicalRetrieval(), new NativeFileAnswerPolicy(), model,
            f.Audit, TimeProvider.System, NullLogger<LocalFileAnswer>.Instance));
        await f.ViewModel.PreviewFileAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FileReview!.ReviewId);
        var source = f.ViewModel.FileRevision!;
        var original = source.Review.Request;
        using var scope = boundary switch
        {
            "system" => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request),
            "foreign" => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request),
            "task" => HostActivity.BeginRoot(new(original.RequestId, original.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request),
            "invocation" => HostActivity.BeginRoot(new(original.RequestId, original.SessionId, original.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())),
                HostActivityLayer.Application, HostOperation.Request),
            "stopped" => HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Request),
            _ => null,
        };
        if (string.Equals(boundary, "stopped", StringComparison.Ordinal)) { scope!.Activity!.Stop(); }
        if (string.Equals(boundary, "missing", StringComparison.Ordinal)) { f.ViewModel.ClearFilePreview(); }
        if (string.Equals(boundary, "owner", StringComparison.Ordinal)) { f.ViewModel.BindClipboardOwnershipGate(() => false); }
        var exact = string.Equals(boundary, "revision", StringComparison.Ordinal) ? source.Reference with { RevisionId = Guid.NewGuid() } : source.Reference;
        (await f.ViewModel.AnswerFileAsync(exact, "private", () => !string.Equals(boundary, "native", StringComparison.Ordinal),
            TestContext.Current.CancellationToken)).Outcome.Should().Be(expected);
        model.Context.Should().BeNull();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("question")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("ownership")]
    public async Task NativeFileAnswerFinalPresentationFencesSourceQuestionAndHostRevocationAfterFakeAllow(string change)
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(new FakeFileInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var current = true;
        var model = new NativeFileAnswerModel
        {
            OnRunAsync = async () =>
            {
                switch (change)
                {
                    case "question": current = false; break;
                    case "privacy": f.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
                    case "call":
                        await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision,
                            TestContext.Current.CancellationToken);
                        break;
                    case "ownership": f.ViewModel.BindClipboardOwnershipGate(() => false); break;
                    default: f.ViewModel.ClearFilePreview(); break;
                }
            },
        };
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        f.ViewModel.BindFileAnswer(new(preview, new LocalFileLexicalRetrieval(), new NativeFileAnswerPolicy(), model,
            f.Audit, TimeProvider.System, NullLogger<LocalFileAnswer>.Instance));
        await f.ViewModel.PreviewFileAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FileReview!.ReviewId);
        await f.ViewModel.RequestFileAnswerAsync();
        var result = await f.ViewModel.AnswerFileAsync(f.ViewModel.FileRevision!.Reference, "private", () => current,
            TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        result.Claims.Should().BeEmpty();
        model.Context.Should().NotBeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    private sealed class NativeFileAnswerPolicy : ILocalFileModelUsePolicy
    {
        public Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
            LocalFileSearchResult excerpts, CancellationToken cancellationToken) => Task.FromResult(LocalFileModelUseDecision.Allowed);
    }

    private sealed class NativeFileAnswerModel : IModelContextRunner
    {
        internal int AdmissionCalls { get; private set; }
        internal string Kind { get; init; } = "unsupported";
        internal Action? OnRun { get; init; }
        internal Func<Task>? OnRunAsync { get; init; }
        internal ModelContextEnvelope? Context { get; private set; }
        public Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
            CancellationToken token, Func<CancellationToken, Task>? validateSource = null)
        {
            AdmissionCalls++;
            sourceCurrent().Should().BeTrue();
            target.Request.Should().BeSameAs(HostActivity.RequireCurrent().Request);
            return Task.FromResult(new ModelContextAdmission(new Lease(this, target.Request)));
        }
        private sealed class Lease(NativeFileAnswerModel owner, HostRequest request) : IAdmittedModelContextTurn
        {
            public HostRequest Request => request;
            public HostRevision SessionGeneration => new(1);
            public Task<LocalContextTurnReceipt> Completion { get; private set; } = Task.FromResult(
                new LocalContextTurnReceipt(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled));
            public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token) =>
                Completion = owner.RunModel(context);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private async Task<LocalContextTurnReceipt> RunModel(ModelContextEnvelope context)
        {
            Context = context;
            OnRun?.Invoke();
            if (OnRunAsync is { } callback) { await callback(); }
            var outcome = Kind switch
            {
                "cancelled" => ModelTurnOutcome.Cancelled,
                "unavailable" => ModelTurnOutcome.Unavailable,
                _ => ModelTurnOutcome.Succeeded,
            };
            var answer = string.Equals(Kind, "success", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "Exact marker", evidenceIds = new[] { context.Evidence[0].Id.Value } } } })
                : "{\"unsupported\":true,\"claims\":[]}";
            var result = new ModelTurnResult(outcome, ModelTurnReason.None,
                new(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate), new(answer, null));
            return new LocalContextTurnReceipt(context.Request, outcome, LocalContextTurnReason.Completed,
                HostTaskState.Succeeded, result);
        }
    }
}
