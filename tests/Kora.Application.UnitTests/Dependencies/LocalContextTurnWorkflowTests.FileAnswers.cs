using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class LocalContextTurnWorkflowTests
{
    [Fact]
    public async Task FileAnswerUsesSharedDurableLocalToolFreeWorkflowWithOriginalRequest()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var bytes = Encoding.UTF8.GetBytes("alpha exact marker");
        var source = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), f.Request,
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
        var search = new LocalFileLexicalRetrieval().Search(source, source.Reference, "alpha", DateTimeOffset.UtcNow, Token);
        IModelContextRunner runner = f.Workflow;
        var admitted = await runner.AdmitAsync(f.Target, () => true, Token);
        await using var turn = admitted.Turn ?? throw new InvalidOperationException("Missing durable admission.");
        f.Store.Tasks.Last().State.Should().Be(HostTaskState.DispatchRecorded);
        var binding = new LocalFileAnswerContext(source, search, "alpha?", turn.Request, new(1), DateTimeOffset.UtcNow);
        binding.Envelope.AllowTools.Should().BeFalse();
        var result = await turn.RunAsync(binding.Envelope, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Request.Should().BeSameAs(f.Request);
        result.ModelResult!.Provenance!.Selection.Should().Be(ModelProviderSelection.OllamaCandidate);
        result.DurableState.Should().Be(HostTaskState.Succeeded);
        f.Model.Calls.Should().Be(1);
        f.Model.Choice.Should().Be(ModelTurnChoice.Local);
        f.Store.Tasks.Last().State.Should().Be(HostTaskState.Succeeded);
        f.Store.Tasks.Should().OnlyContain(task => task.Request.SessionId == source.Review.Request.SessionId);
        LocalContextSessionTarget.FreshSessionDisclosure.Should().Contain("source text and the answer are not saved");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileAnswerSharedWorkflowReportsUnavailableRuntimeOrSourceRevocation(bool revoke)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Model.Outcome = ModelTurnOutcome.Unavailable;
        var issued = await f.Workflow.AdmitAsync(f.Target, () => !revoke, Token);
        await using var turn = issued.Turn;
        var result = turn is null ? issued.Rejection! : await turn.RunAsync(f.Context(), Token);
        result.Outcome.Should().Be(revoke ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unavailable);
        result.Answer.Should().BeNull();
        f.Model.Calls.Should().Be(0);
        if (!revoke)
        {
            result.ModelResult!.Reason.Should().Be(ModelTurnReason.QualificationPending);
            result.DurableState.Should().Be(HostTaskState.Unavailable);
        }
    }

    [Fact]
    public async Task FileAnswerSharedWorkflowSuppressesLateModelResponseAndKeepsTruthfulDurableState()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var current = true;
        f.Model.BeforeRun = () => current = false;
        var issued = await f.Workflow.AdmitAsync(f.Target, () => current, Token);
        await using var turn = issued.Turn ?? throw new InvalidOperationException("Missing durable admission.");
        var result = await turn.RunAsync(f.Context(), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(LocalContextTurnReason.SourceNotCurrent);
        result.DurableState.Should().Be(HostTaskState.Denied);
        result.ModelResult.Should().BeNull();
    }

    [Fact]
    public async Task ProductionFileSourceUsePolicyIsExplicitlyUnavailableRatherThanPreviewConsent()
    {
        var policy = new UnavailableLocalFileModelUsePolicy();
        (await policy.AdmitAsync(null!, "question", null!, Token)).Should().Be(LocalFileModelUseDecision.Unavailable);
    }
}
