using AwesomeAssertions;
using Kora.Application.Context;
using Kora.Application.Dependencies;
using Kora.Core.Context;
using Kora.Core.Context.FolderAnswers;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;
using Kora.Tools.Files.FolderAnswers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("success", LocalFileAnswerOutcome.Succeeded)]
    [InlineData("unsupported", LocalFileAnswerOutcome.Unsupported)]
    [InlineData("cancelled", LocalFileAnswerOutcome.Cancelled)]
    [InlineData("unavailable", LocalFileAnswerOutcome.Unavailable)]
    [InlineData("policy", LocalFileAnswerOutcome.SourceUseUnavailable)]
    public async Task FolderAnswerCompletesSeparateExactReviewAndConfirmationWithoutConversationOrPersistence(
        string kind, LocalFileAnswerOutcome expected)
    {
        var f = new Fixture();
        var inspector = new FolderInspector();
        using var preview = new LocalFilePreview(inspector, f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var model = new NativeFileAnswerModel { Kind = kind };
        var action = new LocalFolderAnswer(preview, new LocalFileLexicalRetrieval(),
            string.Equals(kind, "policy", StringComparison.Ordinal) ? new UnavailableLocalFileModelUsePolicy() : LocalSourceUsePolicy.Instance,
            model, f.Audit, TimeProvider.System, NullLogger<LocalFolderAnswer>.Instance);
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        f.ViewModel.BindFolderAnswer(action);
        var duplicate = () => f.ViewModel.BindFolderAnswer(action);
        duplicate.Should().Throw<InvalidOperationException>();
        await f.ViewModel.PreviewFolderAsync();
        inspector.Reads.Should().Be(0);
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FolderReview!.ReviewId);
        var source = f.ViewModel.FolderRevision!;
        var prepared = await f.ViewModel.PrepareFolderAnswerAsync(source.Reference, "private folder?", () => true, TestContext.Current.CancellationToken);
        prepared.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        var review = prepared.Review!.SelectEvidence([prepared.Review.Search.Citations[1]]);
        model.AdmissionCalls.Should().Be(0);
        review.Search.Citations.Should().ContainSingle();
        review.RetrievedSearch.Citations.Should().HaveCount(2);
        var result = await f.ViewModel.AnswerFolderAsync(review, () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(expected);
        inspector.Reads.Should().Be(2);
        f.ViewModel.Transcript.Should().NotContain("private folder");
        f.ViewModel.ResponseBody.Should().NotContain(FolderInspector.Text);
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        if (model.Context is { } context)
        {
            context.Request.Should().BeSameAs(review.Target.Request);
            context.Request.SessionId.Should().NotBe(source.Review.Request.SessionId);
            context.Tools.Should().BeEmpty();
            context.Evidence.Select(item => item.Content).Should().Equal(review.Search.Citations.Select(citation => citation.Excerpt));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubsetCannotHideCredentialRiskElsewhereInWholeEvidenceFileOrOriginalQuestion(bool questionRisk)
    {
        var f = new Fixture();
        var inspector = new FolderInspector
        {
            Content = questionRisk ? FolderInspector.Text : "private folder marker\n" + new string('z', 2048) + "\napi_key=example",
        };
        using var preview = new LocalFilePreview(inspector, f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var model = new NativeFileAnswerModel { Kind = "success" };
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        f.ViewModel.BindFolderAnswer(new(preview, new LocalFileLexicalRetrieval(), LocalSourceUsePolicy.Instance,
            model, f.Audit, TimeProvider.System, NullLogger<LocalFolderAnswer>.Instance));
        await f.ViewModel.PreviewFolderAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FolderReview!.ReviewId);
        var raw = (await f.ViewModel.PrepareFolderAnswerAsync(f.ViewModel.FolderRevision!.Reference,
            questionRisk ? "private api_key=example" : "private", () => true, TestContext.Current.CancellationToken)).Review!;
        var selected = raw.SelectEvidence([raw.Search.Citations[0]]);
        selected.Search.Citations.Single().Excerpt.Should().NotContain("api_key");
        var result = await f.ViewModel.AnswerFolderAsync(selected, () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        result.SourceUseDecision.Should().Be(LocalFileModelUseDecision.Denied);
        result.Claims.Should().BeEmpty();
        model.AdmissionCalls.Should().Be(0);
        model.Context.Should().BeNull();
        inspector.Reads.Should().Be(2);
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FolderAnswerNeedsBoundActionAndCurrentExactSourceBeforeAnyReview()
    {
        var f = new Fixture();
        var reference = new LocalFolderReference(Guid.NewGuid(), Guid.NewGuid());
        (await f.ViewModel.PrepareFolderAnswerAsync(reference, "question", () => true, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        using var preview = new LocalFilePreview(new FolderInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var model = new NativeFileAnswerModel();
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        f.ViewModel.BindFolderAnswer(new(preview, new LocalFileLexicalRetrieval(), new NativeFileAnswerPolicy(), model,
            f.Audit, TimeProvider.System, NullLogger<LocalFolderAnswer>.Instance));
        (await f.ViewModel.PrepareFolderAnswerAsync(reference, "private", () => true, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        await f.ViewModel.PreviewFolderAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FolderReview!.ReviewId);
        var source = f.ViewModel.FolderRevision!;
        (await f.ViewModel.PrepareFolderAnswerAsync(reference, "private", () => true, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        (await f.ViewModel.PrepareFolderAnswerAsync(source.Reference, "absent", () => true, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(LocalFileAnswerOutcome.NoEvidence);
        model.AdmissionCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("clear")]
    [InlineData("system")]
    [InlineData("foreign")]
    [InlineData("task")]
    [InlineData("invocation")]
    [InlineData("stopped")]
    public async Task FolderAnswerRejectsChangedNativePrivateOwnershipCallAndInputAuthority(string boundary)
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(new FolderInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var model = new NativeFileAnswerModel();
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        f.ViewModel.BindFolderAnswer(new(preview, new LocalFileLexicalRetrieval(), new NativeFileAnswerPolicy(), model,
            f.Audit, TimeProvider.System, NullLogger<LocalFolderAnswer>.Instance));
        await f.ViewModel.PreviewFolderAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FolderReview!.ReviewId);
        var source = f.ViewModel.FolderRevision!;
        var review = (await f.ViewModel.PrepareFolderAnswerAsync(source.Reference, "private", () => true,
            TestContext.Current.CancellationToken)).Review!;
        var current = true;
        HostActivity? scope = null;
        try
        {
            switch (boundary)
            {
                case "native": current = false; break;
                case "owner": f.ViewModel.BindClipboardOwnershipGate(() => false); break;
                case "call":
                    await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken);
                    break;
                case "privacy": f.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
                case "clear": f.ViewModel.ClearFilePreview(); break;
                default:
                    var input = review.Target.SourceRequest;
                    var request = boundary switch
                    {
                        "system" => new(input.RequestId, input.SessionId, input.TaskId, RequestOrigin.HostSystem),
                        "invocation" => new(input.RequestId, input.SessionId, input.TaskId, input.Origin, new(Guid.NewGuid())),
                        "task" => new(input.RequestId, input.SessionId, new(Guid.NewGuid()), input.Origin),
                        _ => HostRequest.Create(RequestOrigin.LocalUi),
                    };
                    scope = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
                    if (string.Equals(boundary, "stopped", StringComparison.Ordinal)) { scope.Complete(HostOperationOutcome.Completed); }
                    break;
            }
            (await f.ViewModel.PrepareFolderAnswerAsync(source.Reference, "private", () => current, TestContext.Current.CancellationToken))
                .Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
            (await f.ViewModel.AnswerFolderAsync(review, () => current, TestContext.Current.CancellationToken))
                .Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        }
        finally { scope?.Dispose(); }
        model.AdmissionCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("native")]
    [InlineData("clear")]
    public async Task LateFolderModelCompletionNeverRendersAfterRevocation(string boundary)
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(new FolderInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        var current = true;
        var model = new NativeFileAnswerModel
        {
            Kind = "success",
            OnRunAsync = async () =>
            {
                switch (boundary)
                {
                    case "owner": f.ViewModel.BindClipboardOwnershipGate(() => false); break;
                    case "call":
                        await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken);
                        break;
                    case "privacy": f.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
                    case "native": current = false; break;
                    default: f.ViewModel.ClearFilePreview(); break;
                }
            },
        };
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        f.ViewModel.BindFolderAnswer(new(preview, new LocalFileLexicalRetrieval(), new NativeFileAnswerPolicy(), model,
            f.Audit, TimeProvider.System, NullLogger<LocalFolderAnswer>.Instance));
        await f.ViewModel.PreviewFolderAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FolderReview!.ReviewId);
        var source = f.ViewModel.FolderRevision!;
        var review = (await f.ViewModel.PrepareFolderAnswerAsync(source.Reference, "private", () => current,
            TestContext.Current.CancellationToken)).Review!;
        review = review.SelectEvidence([review.Search.Citations[0]]);
        var result = await f.ViewModel.AnswerFolderAsync(review, () => current, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        result.Claims.Should().BeEmpty();
        preview.IsQuiescent.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("native", LocalFileAnswerOutcome.Stale)]
    [InlineData("source", LocalFileAnswerOutcome.Stale)]
    [InlineData("cancel", LocalFileAnswerOutcome.Cancelled)]
    [InlineData("invalid", LocalFileAnswerOutcome.InvalidQuestion)]
    public async Task FolderPreparationRechecksAuthorityAndReportsCancellationAfterLexicalWork(
        string boundary, LocalFileAnswerOutcome expected)
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(new FolderInspector(), f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        using var cancellation = new CancellationTokenSource();
        var current = true;
        var retrieval = new CallbackFolderRetrieval(() =>
        {
            switch (boundary)
            {
                case "native": current = false; break;
                case "source": preview.Clear(); break;
                case "cancel": cancellation.Cancel(); break;
            }
        });
        var model = new NativeFileAnswerModel();
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        f.ViewModel.BindFolderAnswer(new(preview, retrieval, new NativeFileAnswerPolicy(), model,
            f.Audit, TimeProvider.System, NullLogger<LocalFolderAnswer>.Instance));
        await f.ViewModel.PreviewFolderAsync();
        await f.ViewModel.ConfirmFilePreviewAsync(f.ViewModel.FolderReview!.ReviewId);
        var source = f.ViewModel.FolderRevision!;
        var result = await f.ViewModel.PrepareFolderAnswerAsync(source.Reference,
            string.Equals(boundary, "invalid", StringComparison.Ordinal) ? "!!!" : "private",
            () => current, cancellation.Token);
        result.Outcome.Should().Be(expected);
        result.Review.Should().BeNull();
        model.AdmissionCalls.Should().Be(0);
    }
}
