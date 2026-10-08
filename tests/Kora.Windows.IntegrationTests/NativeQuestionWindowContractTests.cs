using System.ComponentModel;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeQuestionWindowContractTests
{
    [Theory]
    [InlineData("ReviewQuestion", "ReviewQuestion")]
    [InlineData("SaveDraft", "SaveDraft")]
    [InlineData("SubmitAnswer", "CloseQuestion")]
    [InlineData("CancelQuestion", "CloseQuestion")]
    public async Task Explicit_keyboard_actions_restore_focus_to_an_enabled_control(string action, string destination)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            var model = await CreateModelAsync(fixture, static () => true);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                model.Edit(new(["show"]));
                var button = window.FindControl<Button>(action)!;
                var target = window.FindControl<Button>(destination)!;
                window.IsActive.Should().BeTrue();
                button.Focus().Should().BeTrue();
                var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                target.GotFocus += (_, _) => restored.TrySetResult();

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                await restored.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

                window.FocusManager!.GetFocusedElement().Should().BeSameAs(target);
                target.IsEffectivelyEnabled.Should().BeTrue();
                if (string.Equals(action, "ReviewQuestion", StringComparison.Ordinal)) { model.ReviewText.Should().NotBeEmpty(); }
                if (string.Equals(action, "SaveDraft", StringComparison.Ordinal)) { model.Key.Revision.Value.Should().Be(2); }
                if (action is "SubmitAnswer" or "CancelQuestion") { model.Completion.IsCompletedSuccessfully.Should().BeTrue(); }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Action_completion_does_not_steal_focus_that_moved_or_reveal_a_closed_presentation(bool closeGate)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            var gate = true;
            var model = await CreateModelAsync(fixture, () => gate);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                var review = window.FindControl<TextBox>("ExactReview")!;
                var button = window.FindControl<Button>("ReviewQuestion")!;
                button.Focus().Should().BeTrue();
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var admitted = false;
                void OnChanged(object? sender, PropertyChangedEventArgs args)
                {
                    if (!string.Equals(args.PropertyName, nameof(NativeQuestionViewModel.IsEditable), StringComparison.Ordinal)) { return; }
                    if (!admitted && !model.IsEditable)
                    {
                        admitted = true;
                        if (closeGate) { gate = false; }
                        else { review.Focus().Should().BeTrue(); }
                    }
                    else if (admitted && (model.IsEditable || !model.HasPresentation)) { finished.TrySetResult(); }
                }
                model.PropertyChanged += OnChanged;
                try
                {
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                }
                finally { model.PropertyChanged -= OnChanged; }

                if (closeGate)
                {
                    model.HasPresentation.Should().BeFalse();
                    model.ReviewText.Should().BeEmpty();
                    window.FocusManager!.GetFocusedElement().Should().NotBeSameAs(button);
                }
                else { window.FocusManager!.GetFocusedElement().Should().BeSameAs(review); }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_outcome_survives_eligibility_refresh_and_is_cleared_by_privacy_or_close(bool closeWindow)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            var gate = true;
            var model = await CreateModelAsync(fixture, () => gate);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                model.Edit(new(["show"]));
                await model.SubmitAsync();
                const string outcome = "Synthetic terminal receipt and private-storage disclosure.";
                window.ReportOutcome(outcome);
                model.RefreshEligibility();
                model.RefreshEligibility();

                var status = window.FindControl<TextBlock>("QuestionStatus")!;
                model.Status.Should().Be(outcome);
                status.Text.Should().EndWith(outcome);
                ControlAutomationPeer.CreatePeerForElement(status)!.GetName().Should().EndWith(outcome);

                if (closeWindow) { window.Close(); }
                else
                {
                    gate = false;
                    model.RefreshEligibility();
                }
                window.ReportOutcome("Late terminal content must not return.");
                model.HasPresentation.Should().BeFalse();
                model.Status.Should().NotContain(outcome).And.NotContain("Late terminal content");
                status.Text.Should().NotContain(outcome).And.NotContain("Late terminal content");
                window.FindControl<TextBox>("ExactReview")!.Text.Should().BeNullOrEmpty();
            }
            finally { window.Close(); }
        });
    }

    private static QuestionWindow CreateWindow(NativeQuestionViewModel model) =>
        new(model, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));

    private static async Task ShowAsync(QuestionWindow window, NativeQuestionViewModel model)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (window.IsActive && model.IsEditable) { ready.TrySetResult(); }
        }
        model.PropertyChanged += OnChanged;
        try
        {
            window.Show();
            window.Activate();
            if (window.IsActive && model.IsEditable) { ready.TrySetResult(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { model.PropertyChanged -= OnChanged; }
    }

    private static async Task<NativeQuestionViewModel> CreateModelAsync(InteractionStorageFixture fixture, Func<bool> gate)
    {
        await fixture.InitializeAsync();
        var spec = new QuestionSpec("Synthetic native question", QuestionKind.SingleChoice, [new("show", "Show local version")]);
        var record = (await fixture.RunAsync(() => fixture.Questions.CreateAsync(
            fixture.Request, spec, fixture.Time.Now.AddMinutes(5), fixture.Token))).Question!;
        var store = new NativeInteractionStore(fixture.Store, gate);
        return new(record, new(store, fixture.Time), new(store, fixture.Time), new(store, fixture.Time),
            gate, fixture.Time, NullLogger<NativeQuestionViewModel>.Instance);
    }
}
