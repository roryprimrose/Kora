using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Theory]
    [InlineData("exact", true)]
    [InlineData("draft", true)]
    [InlineData("answered", true)]
    [InlineData("expired", false)]
    [InlineData("multiple", false)]
    [InlineData("purpose", false)]
    [InlineData("source", false)]
    [InlineData("unsupported-shape", false)]
    [InlineData("wrong-options", false)]
    [InlineData("approval", false)]
    [InlineData("restart", false)]
    [InlineData("other-task", false)]
    [InlineData("other-session", true)]
    public async Task ConversationCurrentSourceAdmitsOnlyItsOneLiveOrdinaryClarificationAndPreservesOtherBlockers(
        string scenario, bool current)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var expiry = f.Time.Now.AddSeconds(15);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "exact original question source", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            await f.Store.PublishTrustedSnapshotAsync(request, new(true, true, false, true), null, 0, f.Token);
            if (scenario is not "approval")
            {
                var spec = LocalModelQuestionPolicy.CreateSpec(new("Choose format", ["Compact", "Detailed"]));
                if (scenario is "purpose" or "source")
                {
                    spec = new(spec.Text, spec.Kind, spec.Options,
                        purpose: scenario is "purpose" ? "unrelated-question" : spec.Purpose,
                        sourceId: scenario is "source" ? "unrelated-source" : spec.SourceId);
                }
                if (scenario is "unsupported-shape" or "wrong-options")
                {
                    spec = new(spec.Text, scenario is "unsupported-shape" ? QuestionKind.MultipleChoice : spec.Kind,
                        scenario is "wrong-options" ? [new("foreign-a", "Compact"), new("foreign-b", "Detailed")] : spec.Options,
                        purpose: spec.Purpose, sourceId: spec.SourceId);
                }
                var question = (await f.Questions.CreateAsync(request, spec, expiry, f.Token)).Question!;
                if (scenario is "multiple") { await f.Questions.CreateAsync(request, spec, expiry, f.Token); }
                if (scenario is "draft") { await f.Questions.DraftAsync(question.Key, new(["option-0"]), RequestOrigin.LocalUi, f.Token); }
                if (scenario is "answered") { await f.Questions.SubmitAsync(question.Key, new(["option-0"]), RequestOrigin.LocalUi, f.Token); }
                question.Key.Request.Should().Be(request);
            }
        }
        if (scenario is "approval")
        {
            f.Request = new(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
            await f.AdmitAsync(newSession: false);
            await f.PresentAsync();
        }
        if (scenario is "other-task" or "other-session")
        {
            f.Request = InteractionStorageFixture.NewRequest(scenario is "other-task" ? request.SessionId : null);
            await f.AdmitAsync(newSession: scenario is "other-session");
            var spec = LocalModelQuestionPolicy.CreateSpec(new("Foreign task question", ["Compact", "Detailed"]));
            await f.RunAsync(() => f.Questions.CreateAsync(f.Request, spec, expiry, f.Token));
        }
        if (scenario is "expired") { f.Time.Now = expiry; }
        if (scenario is "restart")
        {
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
        }
        var observation = (await f.Store.ReadTaskAsync(request.SessionId, request.TaskId, f.Token))!;
        observation.Task.Request.Should().Be(request);
        observation.Task.Revision.Value.Should().Be(2);
        observation.Task.State.Should().Be(HostTaskState.DispatchRecorded);
        observation.Question.Should().BeNull("this is not authority to answer through the task observation");
        observation.CurrentSource.Should().Be(current);
    }
}
