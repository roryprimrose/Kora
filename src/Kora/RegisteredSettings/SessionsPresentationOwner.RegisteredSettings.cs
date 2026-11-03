using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    internal Task RegisteredSettingOperation { get; private set; } = Task.CompletedTask;
    private (Window Surface, ConversationMessage Message)? registeredSettingReviewSource;

    private void OnRegisteredSettingSuggestion(object? sender, RoutedEventArgs args)
    {
        if (revoked || sender is not Window surface || args.Source is not Button button) { return; }
        if (button is { Name: "ReviewRegisteredSettingSuggestion", DataContext: ConversationMessage message }
            && model.CanReviewRegisteredSettingSuggestion)
        {
            args.Handled = true;
            registeredSettingReviewSource = (surface, message);
            RegisteredSettingOperation = ReviewRegisteredSettingAndFocusAsync(surface, button, message);
        }
        else if (string.Equals(button.Name, "ConfirmRegisteredSettingSuggestion", StringComparison.Ordinal) && model.CanConfirmRegisteredSettingSuggestion)
        {
            args.Handled = true;
            RegisteredSettingOperation = ConfirmRegisteredSettingAndReturnAsync(surface);
        }
        else if (string.Equals(button.Name, "CancelRegisteredSettingSuggestion", StringComparison.Ordinal))
        {
            args.Handled = true;
            model.CancelRegisteredSettingSuggestion();
            ReturnToRegisteredSettingSource(surface);
        }
    }

    private async Task ReviewRegisteredSettingAndFocusAsync(Window surface, Button source, ConversationMessage message)
    {
        var originalFocus = surface.FocusManager?.GetFocusedElement() as Control;
        var readback = surface.FindControl<TextBlock>("RegisteredSettingReviewReadback");
        var followReview = source.IsFocused && readback is not null && readback.Focus();
        await model.ReviewRegisteredSettingSuggestionAsync(message);
        if (!revoked && surface.IsActive && surface.IsVisible && followReview && readback is { IsFocused: true }
            && registeredSettingReviewSource is { } current
            && ReferenceEquals(current.Surface, surface) && ReferenceEquals(current.Message, message))
        {
            if (model.CanConfirmRegisteredSettingSuggestion) { readback.BringIntoView(); }
            else { ReturnToRegisteredSettingSource(surface); }
        }
        else if (!followReview) { RestoreRegisteredSettingFocus(surface, originalFocus); }
    }

    private async Task ConfirmRegisteredSettingAndReturnAsync(Window surface)
    {
        var originalFocus = surface.FocusManager?.GetFocusedElement() as Control;
        var returnToSource = originalFocus is not null && IsRegisteredSettingReviewControl(originalFocus);
        await model.ConfirmRegisteredSettingSuggestionAsync();
        if (returnToSource && (surface.FocusManager?.GetFocusedElement() is not Control focused || IsRegisteredSettingReviewControl(focused)))
        { ReturnToRegisteredSettingSource(surface); }
        else
        {
            registeredSettingReviewSource = null;
            RestoreRegisteredSettingFocus(surface, originalFocus);
        }
    }

    private void RestoreRegisteredSettingFocus(Window surface, Control? originalFocus)
    {
        if (!revoked && surface.IsActive && surface.IsVisible && surface.FocusManager?.GetFocusedElement() is null
            && originalFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
            && ReferenceEquals(TopLevel.GetTopLevel(originalFocus), surface))
        { originalFocus.Focus(); }
    }

    private void OnRegisteredSettingReviewKeyDown(object? sender, KeyEventArgs args)
    {
        if (revoked || args.Handled || args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None
            || sender is not Window surface || args.Source is not Control control
            || !IsRegisteredSettingReviewControl(control) || !model.CanConfirmRegisteredSettingSuggestion)
        { return; }
        args.Handled = true;
        model.CancelRegisteredSettingSuggestion();
        ReturnToRegisteredSettingSource(surface);
    }

    private static bool IsRegisteredSettingReviewControl(Control control) =>
        control.Name is "RegisteredSettingReviewReadback" or "ConfirmRegisteredSettingSuggestion" or "CancelRegisteredSettingSuggestion";

    private void ReturnToRegisteredSettingSource(Window surface)
    {
        var source = registeredSettingReviewSource;
        registeredSettingReviewSource = null;
        if (!revoked && surface.IsActive && surface.IsVisible && source is { } original
            && ReferenceEquals(original.Surface, surface) && model.CanReviewRegisteredSettingSuggestion
            && model.ConversationMessages.Any(row => ReferenceEquals(row, original.Message)))
        {
            surface.FindControl<ListBox>("AnswerSourceRecords")?.ScrollIntoView(original.Message);
            surface.UpdateLayout();
            var button = surface.GetVisualDescendants().OfType<Button>().FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal)
                && ReferenceEquals(candidate.DataContext, original.Message)
                && candidate.IsEffectivelyVisible && candidate.IsEffectivelyEnabled);
            if (button is not null)
            {
                button.BringIntoView();
                button.Focus();
            }
        }
    }
}
