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
    [Theory]
    [InlineData("tab")]
    [InlineData("hide-return")]
    [InlineData("collapse")]
    public async Task SupportingReviewTabRetirementDisablesApprovalUntilTheSameExactReviewIsShownAgain(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await NativeQuestionTests.PublishRealFixtureProposalAsync(fixture);
            var record = await fixture.PresentAsync();
            var model = new NativeQuestionViewModel(record, fixture.Questions, fixture.Authorization,
                new(fixture.Store, fixture.Time), static () => true, fixture.Time, NullLogger<NativeQuestionViewModel>.Instance);
            var owner = new ResponseWindow(native.Main);
            owner.Show();
            owner.Activate();
            try
            {
                var view = await ShowInlineAsync(owner, model);
                model.Edit(new(["once"]));
                model.CanSubmit.Should().BeFalse();
                view.FindControl<Button>("ReviewQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.InteractionOperation;
                owner.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                owner.IsExpanded.Should().BeTrue();
                owner.FindControl<ContentControl>("NativeReviewContent")!.IsVisible.Should().BeTrue();
                view.FindControl<TextBox>("ExactReview")!.Text.Should().Be(model.ReviewText);
                model.CanSubmit.Should().BeTrue(model.Status);
                (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().BeEmpty();
                switch (boundary)
                {
                    case "tab":
                        var unrelated = new TextBox { IsReadOnly = true };
                        owner.ShowNativeReview(new Border(), "Another original review", unrelated,
                            static () => true, shown => unrelated.Text = shown ? "other review" : string.Empty);
                        break;
                    case "hide-return": owner.Hide(); owner.Show(); owner.Activate(); break;
                    case "collapse":
                        owner.FindControl<Button>("CollapseWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                }
                Dispatcher.UIThread.RunJobs();
                model.CanSubmit.Should().BeFalse("hidden complete text cannot retain native review acknowledgement");
                view.FindControl<TextBox>("ExactReview")!.Text.Should().BeNullOrEmpty();
                await model.SubmitAsync();
                model.Completion.IsCompleted.Should().BeFalse();
                (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().BeEmpty();
                if (!view.IsPresented) { view.Activate(); await view.Operation; }
                view.FindControl<Button>("ReviewQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.InteractionOperation;
                Dispatcher.UIThread.RunJobs();
                model.Edit(new(["once"]));
                model.CanSubmit.Should().BeTrue(model.Status);
                await model.SubmitAsync();
                (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Approved);
                (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle();
                view.Close();
            }
            finally { owner.Close(); }
        });
    }
}
