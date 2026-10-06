using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Core;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Idle_presence_can_auto_hide_whether_or_not_microphone_capture_is_active(bool listening)
    {
        var fixture = new Fixture();
        SetPresentationProperty(fixture.ViewModel, nameof(MainViewModel.State), AssistantState.Information);
        SetPresentationProperty(fixture.ViewModel, nameof(MainViewModel.IsListening), listening);

        fixture.ViewModel.CanAutoHidePresence.Should().BeTrue();
    }

    [Fact]
    public void Keeping_response_visible_does_not_pin_the_presence()
    {
        var fixture = new Fixture();
        SetPresentationProperty(fixture.ViewModel, nameof(MainViewModel.State), AssistantState.Information);
        fixture.ViewModel.IsResponseAlwaysVisible = true;

        fixture.ViewModel.CanAutoHidePresence.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(MainViewModel.IsBusy))]
    [InlineData(nameof(MainViewModel.IsSpeaking))]
    [InlineData(nameof(MainViewModel.IsGrantEditorVisible))]
    [InlineData(nameof(MainViewModel.IsLocalModelSetupActive))]
    [InlineData(nameof(MainViewModel.IsPowerShellSetupActive))]
    public void Presence_does_not_auto_hide_during_work_speech_or_grant_editing(string property)
    {
        var fixture = new Fixture();
        SetPresentationProperty(fixture.ViewModel, nameof(MainViewModel.State), AssistantState.Information);
        SetPresentationProperty(fixture.ViewModel, property, true);

        fixture.ViewModel.CanAutoHidePresence.Should().BeFalse();
    }

    [Fact]
    public void Approval_waiting_in_an_information_state_still_prevents_auto_hiding()
    {
        var fixture = new Fixture();
        SetPresentationProperty(fixture.ViewModel, nameof(MainViewModel.State), AssistantState.Information);
        typeof(MainViewModel).GetField("pendingModelAction",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(fixture.ViewModel, BuiltInAction.LockMachine);

        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.CanAutoHidePresence.Should().BeFalse();
    }

    [Fact]
    public void Response_recovery_actions_prevent_presence_auto_hiding()
    {
        var fixture = new Fixture();
        SetPresentationProperty(fixture.ViewModel, nameof(MainViewModel.State), AssistantState.Information);
        SetPresentationProperty<IReadOnlyList<ResponseAction>>(
            fixture.ViewModel,
            nameof(MainViewModel.ResponseActions),
            [new ResponseAction(ResponseActionKind.OpenVoiceSettings, "Review voice settings")]);

        fixture.ViewModel.HasResponseActions.Should().BeTrue();
        fixture.ViewModel.CanAutoHidePresence.Should().BeFalse();
    }

    [Fact]
    public void Presence_hiding_remains_presentation_only_and_does_not_stop_audio()
    {
        var fixture = new Fixture();
        var actions = new List<WindowAction>();
        fixture.ViewModel.WindowActionRequested += (_, action) => actions.Add(action);
        fixture.Events.Clear();

        fixture.ViewModel.HidePresentation();

        actions.Should().Equal(WindowAction.Hide);
        fixture.Events.Should().Equal("window.Hide");
    }

    private static void SetPresentationProperty<T>(MainViewModel viewModel, string name, T value) =>
        typeof(MainViewModel).GetProperty(name)!.SetValue(viewModel, value);
}
