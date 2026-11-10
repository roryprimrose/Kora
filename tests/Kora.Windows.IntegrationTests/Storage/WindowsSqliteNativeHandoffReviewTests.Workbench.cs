using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Infrastructure;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class WindowsSqliteNativeHandoffReviewTests
{
    [Theory]
    [InlineData(false, "approve")]
    [InlineData(true, "approve")]
    [InlineData(false, "hide")]
    [InlineData(true, "hide")]
    [InlineData(false, "tab")]
    [InlineData(true, "tab")]
    public async Task HostedHandoffReviewsExactEnvelopeAndEvidenceWithoutGrantingPhysicalSend(bool remove, string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await AdmitCurrentTask(storage);
            using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var boundary = new TestBoundaries();
            await using var modelHost = CreateHost(storage, dependencies, boundary);
            var workflow = new ModelProviderHandoffWorkflow(modelHost, storage.Questions, boundary,
                NullLogger<ModelProviderHandoffWorkflow>.Instance, new(storage.Store, storage.Time));
            var questionIds = new HashSet<Guid>();
            await storage.RunAsync(async () =>
            {
                var policy = new ModelProviderPolicy(storage.Request.SessionId, new(1), ModelProviderMode.LocalFirst,
                    ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);
                await modelHost.SetPolicyAsync(policy, storage.Token);
                var context = new ModelContextEnvelope(storage.Request, "exact local system", "original hosted-review task",
                    [new(new(Guid.NewGuid()), storage.Request, new(1), "private removable evidence", ModelEvidenceDisclosure.HostedEligible)],
                    storage.Time.Now, storage.Time.Now.AddMinutes(2));
                var offer = (await workflow.OfferAsync(policy, context, ModelHandoffReason.UserRequestedHosted, storage.Token)).Offer;
                offer.Should().NotBeNull();
                questionIds.Add(offer!.Question.Key.QuestionId.Value);
            });
            using var native = NativeUxTestFixture.Create(OwnedStorageFixture.ScratchParent());
            await native.InitializeAsync();
            var shell = new ResponseWindow(native.Main);
            using var controller = new ModelHandoffWindowController(native.Main, new(workflow),
                NullLogger<ModelHandoffWindowController>.Instance);
            controller.BindWorkbench(() => shell);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            try
            {
                native.Main.ReviewPendingHandoff();
                await controller.Operation;
                var view = controller.View!;
                var offers = view.FindControl<ComboBox>("Offers")!;
                var approve = view.FindControl<Button>("Approve")!;
                offers.SelectedIndex.Should().Be(-1);
                approve.IsEnabled.Should().BeFalse();
                offers.SelectedIndex = 0;
                await ((AsyncCommand)view.FindControl<Button>("Inspect")!.Command!).ExecuteAsync();
                var preview = view.FindControl<TextBlock>("Preview")!;
                preview.Text.Should().Contain("original hosted-review task").And.Contain("private removable evidence");
                approve.IsEnabled.Should().BeTrue();
                boundary.Calls.Should().Be(0);
                if (remove)
                {
                    view.FindControl<StackPanel>("Evidence")!.Children.OfType<CheckBox>().Single().IsChecked = true;
                    await ((AsyncCommand)view.FindControl<Button>("Remove")!.Command!).ExecuteAsync();
                    questionIds.Add(workflow.Pending().Single().Question.Key.QuestionId.Value);
                    approve.IsEnabled.Should().BeFalse("a reduced envelope needs a fresh complete review");
                    await ((AsyncCommand)view.FindControl<Button>("Inspect")!.Command!).ExecuteAsync();
                    preview.Text.Should().Contain("original hosted-review task").And.NotContain("private removable evidence");
                }
                if (action is "approve") { await ((AsyncCommand)approve.Command!).ExecuteAsync(); }
                else if (action is "hide") { shell.Hide(); shell.Show(); shell.Activate(); }
                else
                {
                    var other = new TextBlock { Text = "Another exact review" };
                    shell.ShowNativeReview(other, "Another exact review", other, static () => true, _ => { });
                }
                if (action is not "approve")
                {
                    controller.View.Should().BeNull();
                    preview.Text.Should().BeEmpty();
                    await controller.CloseOperation;
                }
                var questions = (await storage.Store.ReadQuestionsAsync(storage.Request.SessionId, storage.Token))
                    .Where(question => questionIds.Contains(question.Key.QuestionId.Value)).ToArray();
                questions.Should().HaveCount(remove ? 2 : 1);
                questions.Should().OnlyContain(question => question.Status != QuestionStatus.Pending);
                var expected = action is "approve" ? QuestionStatus.Answered : QuestionStatus.Cancelled;
                questions.Should().Contain(question => question.Status == expected);
                storage.Count("conversation_messages").Should().Be(0);
                (await storage.Store.ReadGrantsAsync(storage.Token)).Should().BeEmpty();
                boundary.Calls.Should().Be(0, "context approval is not physical-send consent");
                windows.Distinct().Should().Equal(shell);
            }
            finally { controller.Dispose(); await controller.CloseOperation; shell.Close(); }
        });
    }
}
