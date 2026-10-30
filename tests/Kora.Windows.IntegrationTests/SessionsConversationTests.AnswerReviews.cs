using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeExactDetailsThenDeliberateReviewAndUndoPreserveIndependentConversationsAndBackgroundResults(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "first");
            await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "unseen");
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var other = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "other topic");
            var opened = new List<AdmittedDetailContent>();
            await using var composition = new Composition(f, async (content, admitted, resolve, token) =>
            {
                admitted().Should().BeTrue();
                content.Origin.Should().Be(DetailContentOrigin.RetainedAssistant);
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                opened.Add(content);
                return "Exact canonical Details opened; not marked.";
            });
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == first.Request.SessionId));
            model.ComposerBody = "independent unsent input";
            Window window = compact ? new CompactSessionWindow(model, new(model)) : new SessionsWindow(model);
            window.Show();
            try
            {
                var panel = window.GetVisualDescendants().OfType<AnswerReviewPanel>().Single();
                var expander = panel.GetVisualDescendants().OfType<Expander>().Single();
                expander.IsExpanded = true;
                await Click("ReadNotMarked");
                model.AnswerReviewStatus.Should().Contain("2 of 2").And.Contain("not marked reviewed");
                f.Count("conversation_answer_reviews").Should().Be(0);
                var row = model.AnswerReviewRows[0];
                model.SelectedAnswerReview = row with { Sequence = row.Sequence };
                model.SelectedAnswerReview.Should().BeNull("equal/copied rows are not native selection");
                model.SelectedAnswerReview = row;
                model.CanMarkAnswerReviewed.Should().BeFalse("opening is separate from selecting");
                await Click("OpenAnswer");
                model.AnswerReviewStatus.Should().NotStartWith("Unavailable:", model.ConversationStatus);
                opened.Single().ConversationMessage.Should().Be(first);
                f.Count("conversation_answer_reviews").Should().Be(0, "Details is not a read receipt");
                model.CanMarkAnswerReviewed.Should().BeTrue();
                await Click("Mark");
                model.AnswerReviewStatus.Should().Contain("Only this exact answer marked reviewed").And.Contain("1 of 2");
                model.AnswerReviewRows.Should().ContainSingle().Which.RequestId.Should().NotBe(first.Request.RequestId);
                model.ComposerBody.Should().Be("independent unsent input");
                await WindowsSqliteAnswerReviewTests.Answer(f, first.Request.SessionId, "background");
                await Click("ReadNotMarked");
                model.AnswerReviewStatus.Should().Contain("2 of 3");
                await Click("ReadAll");
                model.SelectedAnswerReview = model.AnswerReviewRows.Single(value => value.RequestId == first.Request.RequestId);
                await Click("OpenAnswer");
                model.CanUnmarkAnswerReviewed.Should().BeTrue();
                await Click("Unmark");
                model.AnswerReviewStatus.Should().Contain("Only this exact answer unmarked").And.Contain("3 of 3");
                var otherPage = await f.Store.ReadAnswerReviewsAsync(await f.Store.ReadMetadataAsync(other.Request.SessionId, f.Token),
                    ConversationAnswerReviewFilter.NotMarkedReviewed, null, 25, f.Token);
                otherPage.NotMarkedReviewed.Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);

                async Task Click(string name)
                {
                    panel.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await panel.Operation;
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("presentation")]
    [InlineData("head")]
    public async Task LateAnswerDetailsOrChangedSourceNeverAcquiresReviewChoice(string revoke)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "first");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var composition = new Composition(f, (_, _, _, _) =>
        {
            entered.SetResult();
            return release.Task;
        });
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadAnswerReviewsAsync(ConversationAnswerReviewFilter.NotMarkedReviewed);
        model.SelectedAnswerReview = model.AnswerReviewRows.Single();
        var presentation = true;
        var opening = model.OpenAnswerReviewAsync(() => presentation);
        await entered.Task;
        if (revoke is "privacy") { composition.Access.CanInspect = false; composition.Access.ControlRevision++; }
        if (revoke is "presentation") { presentation = false; }
        release.SetResult("late opening");
        await opening;
        if (revoke is "head")
        {
            await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "background");
            await model.SetAnswerReviewedAsync(true);
            model.AnswerReviewStatus.Should().StartWith("Unavailable:");
        }
        model.CanMarkAnswerReviewed.Should().BeFalse();
        f.Count("conversation_answer_reviews").Should().Be(0);
    }
}
