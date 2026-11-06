using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string FormClarification = """
        {"kind":"question","prompt":"Describe the answer","fields":[
        {"label":"Audience","type":"text","maximumLength":100,"maximumUtf8Bytes":200},
        {"label":"Style","type":"singleChoice","options":["Compact","Detailed"]}],"callId":"ask_1"}
        """;

    [Fact]
    public async Task ExactOriginalLocalTaskReceivesOneCanonicalTypedFormAnswerAndContinuesNormally()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(FormClarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var service = QuestionService(f, f.ConfiguredProfiles(adapter: loop));
        var receipt = await service.SubmitAsync(new(f.Session, null), "Original form request", static () => true, Token,
            questionPresenter: async (request, _, spec, expiry, _, token) =>
            {
                spec.Kind.Should().Be(QuestionKind.Form);
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var questions = new HostQuestionService(f.Questions, f.Time);
                var initial = (await questions.CreateAsync(request, spec, expiry, token)).Question!;
                transport.Requests.Should().HaveCount(1, "no model continuation happens during draft review");
                var answer = new QuestionAnswer([], fields: [new("field-1", Choice: "option-1"), new("field-0", "Reviewers")]);
                return new(initial, await questions.SubmitAsync(initial.Key, answer, RequestOrigin.LocalUi, token));
            });
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(request => request.FormsAllowed && request.Context.Tools.Count == 0);
        var answer = (ModelQuestionAnswerMessage)transport.Requests[1].Messages[1];
        answer.Answer.Fields.Should().Equal(new QuestionFieldAnswer("field-0", "Reviewers"), new("field-1", Choice: "option-1"));
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
        f.ExecutionCompletions.Should().Be(1);
        f.Questions.Snapshot.Grants.Should().BeEmpty();
    }
}
