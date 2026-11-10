using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Core.Interaction;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NativeQuestionWindowContractTests
{
    [Fact]
    public async Task InlineQuestionCompletesTheOriginalAnswerWithoutOpeningAnotherNativeWindow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            using var fixture = new InteractionStorageFixture();
            var model = await CreateModelAsync(fixture, static () => true);
            var owner = new ResponseWindow(native.Main);
            var extraWindows = 0;
            using var observed = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((_, _) => extraWindows++);
            try
            {
                owner.Show();
                owner.Activate();
                var view = await ShowInlineAsync(owner, model);
                view.IsPresented.Should().BeTrue();
                model.Edit(new(["show"]));
                model.CanSubmit.Should().BeTrue(model.Status);
                view.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var answer = await model.Completion.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
                answer.Outcome.Should().Be(HostInteractionOutcome.Answered);
                model.Question.Key.Request.Should().Be(fixture.Request);
                extraWindows.Should().Be(0);
                owner.FindControl<StackPanel>("InlineQuestions")!.Children.Should().Contain(view);
                view.Close();
                owner.FindControl<StackPanel>("InlineQuestions")!.Children.Should().BeEmpty();
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData("hide-return")]
    [InlineData("minimize-restore")]
    public async Task InlineQuestionWithdrawalRetiresReviewAndFocusWithoutAnsweringOrCancellingTheWait(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            using var fixture = new InteractionStorageFixture();
            var model = await CreateModelAsync(fixture, static () => true);
            var owner = new ResponseWindow(native.Main);
            try
            {
                owner.Show();
                owner.Activate();
                var view = await ShowInlineAsync(owner, model);
                var lifetime = view.CaptureFocusLifetime();
                view.FindControl<Button>("ReviewQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.InteractionOperation;
                view.FindControl<TextBox>("ExactReview")!.Text.Should().NotBeEmpty();
                if (string.Equals(boundary, "hide-return", StringComparison.Ordinal)) { owner.Hide(); owner.Show(); }
                else { owner.WindowState = WindowState.Minimized; owner.WindowState = WindowState.Normal; }
                Dispatcher.UIThread.RunJobs();
                lifetime().Should().BeFalse();
                view.IsPresented.Should().BeFalse();
                view.FindControl<TextBox>("ExactReview")!.Text.Should().BeEmpty();
                view.FindControl<Button>("ReopenQuestion")!.IsVisible.Should().BeTrue();
                model.Completion.IsCompleted.Should().BeFalse("presentation withdrawal is not an answer or cancellation");
                view.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Completion.IsCompleted.Should().BeFalse("stale hidden controls cannot submit");
                view.FindControl<Button>("ReopenQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.Operation;
                view.IsPresented.Should().BeTrue();
                model.Question.Key.Request.Should().Be(fixture.Request);
                lifetime().Should().BeFalse("explicit reopening creates a fresh presentation, not renewed old focus admission");
                view.Close();
            }
            finally { owner.Close(); }
        });
    }

    private static async Task<QuestionView> ShowInlineAsync(ResponseWindow owner, NativeQuestionViewModel model)
    {
        var view = new QuestionView(model, new(NullLogger<NativeDetailRenderer>.Instance), owner);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (view.IsPresented && view.IsActive && model.IsEditable && !model.HasPendingOperation)
            { ready.TrySetResult(); }
        }
        model.PropertyChanged += Check;
        try
        {
            owner.ShowInlineQuestion(view);
            Check(null, new(string.Empty));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            return view;
        }
        finally { model.PropertyChanged -= Check; }
    }
}
