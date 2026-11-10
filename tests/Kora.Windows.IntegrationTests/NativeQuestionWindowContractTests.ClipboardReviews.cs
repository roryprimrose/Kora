using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Context;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Interaction;
using Kora.NativeUxFixture;
using Kora.Tools.Clipboard;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NativeQuestionWindowContractTests
{
    [Fact]
    public async Task InlineClipboardPermissionPolicyAndSnapshotUseOriginalSupportingReviewsBeforeSeparateAcceptance()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            using var data = new Storage.InteractionStorageFixture();
            await data.InitializeAsync();
            data.Time.UseFrozenTimers = true;
            var spec = new QuestionSpec("Describe the answer", QuestionKind.Text, [],
                maximumTextLength: LocalModelQuestion.MaximumFreeTextLength,
                purpose: LocalModelQuestionPolicy.Purpose, sourceId: LocalModelQuestionPolicy.SourceId);
            var record = (await data.RunAsync(() => data.Questions.CreateAsync(data.Request, spec,
                data.Time.Now.AddMinutes(2), data.Token))).Question!;
            var model = new NativeQuestionViewModel(record, data.Questions, data.Authorization, new(data.Store, data.Time),
                static () => true, data.Time, NullLogger<NativeQuestionViewModel>.Instance);
            await using var admission = new ClipboardExplanationAdmission(data.Store, data.Store, new(data.Tasks));
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            var reader = new QuestionClipboardAnswerTests.Reader();
            using var answer = new QuestionClipboardAnswer(model, reader, admission, data.Store,
                static () => static () => true,
                () => new(1, Kora.Core.Communication.CallState.Clear, false, Kora.Core.Communication.CallAwareSettings.Default),
                static () => 1, static () => 1, data.Time, NullLogger<ClipboardSnapshotBroker>.Instance,
                NullLogger<NativeQuestionViewModel>.Instance, renderer);
            model.BindClipboardAnswer(answer);
            var owner = new ResponseWindow(native.Main);
            owner.Show();
            owner.Activate();
            try
            {
                var view = await ShowInlineAsync(owner, model);
                var permissionShown = new TaskCompletionSource<QuestionView>(TaskCreationOptions.RunContinuationsAsynchronously);
                owner.FindControl<StackPanel>("InlineQuestions")!.Children.CollectionChanged += (_, args) =>
                {
                    if (args.NewItems is { Count: 1 } && args.NewItems[0] is QuestionView added && !ReferenceEquals(added, view))
                    { permissionShown.TrySetResult(added); }
                };
                var capture = answer.CaptureAsync();
                var permission = await permissionShown.Task.WaitAsync(TimeSpan.FromSeconds(5), data.Token);
                await permission.Operation;
                reader.Calls.Should().Be(0);
                permission.State.Edit(new(["once"]));
                permission.FindControl<Button>("ExplainPolicy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await permission.InteractionOperation;
                Dispatcher.UIThread.RunJobs();
                var supporting = owner.FindControl<ContentControl>("NativeReviewContent")!;
                var explanation = permission.FindControl<TextBox>("PolicyExplanation")!;
                supporting.GetVisualDescendants().Should().Contain(explanation);
                explanation.Text.Should().Contain("No clipboard contents were read");
                permission.State.CanSubmit.Should().BeFalse("policy explanation is not approval");
                permission.FindControl<Button>("ReturnToApproval")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await permission.InteractionOperation;
                permission.FindControl<Button>("ReviewQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await permission.InteractionOperation;
                Dispatcher.UIThread.RunJobs();
                permission.State.Edit(new(["once"]));
                permission.State.CanSubmit.Should().BeTrue(permission.State.Status);
                permission.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await permission.InteractionOperation;
                await capture.WaitAsync(TimeSpan.FromSeconds(5), data.Token);
                Dispatcher.UIThread.RunJobs();
                reader.Calls.Should().Be(1);
                var snapshot = view.FindControl<TextBox>("ClipboardAnswerReview")!;
                supporting.GetVisualDescendants().Should().Contain(snapshot);
                snapshot.Text.Should().NotBeNullOrEmpty();
                answer.CanAccept.Should().BeTrue(model.Status);
                var other = new TextBox { IsReadOnly = true };
                owner.ShowNativeReview(new Border(), "Another original review", other,
                    static () => true, shown => other.Text = shown ? "other review" : string.Empty);
                answer.CanAccept.Should().BeFalse("inactive snapshot text cannot retain native acceptance acknowledgement");
                snapshot.Text.Should().BeNullOrEmpty();
                answer.Clear();
                await answer.Quiescence;
                model.Completion.IsCompleted.Should().BeFalse();
                view.Close();
            }
            finally
            {
                answer.Dispose();
                await answer.Quiescence;
                owner.Close();
            }
        });
    }
}
