using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Interaction;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

public sealed partial class PendingQuestionNavigationTests
{
    [Fact]
    public async Task ProductionControllerCompletesItsExactQuestionInTheSharedResponseWindow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            using var host = Host(f);
            using var controller = Controller(f, host);
            var owner = new ResponseWindow(f.Main);
            owner.Show();
            owner.Activate();
            var shown = new TaskCompletionSource<QuestionView>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stack = owner.FindControl<StackPanel>("InlineQuestions")!;
            stack.Children.CollectionChanged += (_, args) =>
            {
                if (args.NewItems is { Count: 1 } && args.NewItems[0] is QuestionView view) { shown.TrySetResult(view); }
            };
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((_, _) => extraWindows++);
            var run = controller.ReviewVersionAsync(owner);
            NativeQuestionViewModel? model = null;
            try
            {
                var view = await shown.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
                model = State(controller) ?? throw new InvalidOperationException("The inline controller lost its original question state.");
                await EditableAsync(model, f.Token);
                await view.Operation;
                view.IsPresented.Should().BeTrue();
                var question = model.Question;
                model.Edit(new(["show"]));
                await model.SubmitAsync();
                await run.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
                (await model.Completion).Question!.Status.Should().Be(QuestionStatus.Answered);
                model.Question.Key.Request.Should().Be(question.Key.Request);
                view.FindControl<TextBlock>("QuestionStatus")!.Text.Should().Contain("Local version");
                extraWindows.Should().Be(0);
                owner.IsVisible.Should().BeTrue();
            }
            finally { await CloseAndDrainAsync(controller, owner, null, model, run); }
        });
    }
}
