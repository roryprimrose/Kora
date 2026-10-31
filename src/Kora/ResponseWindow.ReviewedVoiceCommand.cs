using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kora.Application.ViewModels;

namespace Kora;

public sealed partial class ResponseWindow
{
    private long reviewedCommandControlRevision;
    private Func<string, bool>? reviewedCommandEdit;
    private Func<Task>? reviewedCommandConfirm;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native Click boundary reports failures through RunResponseActionAsync.")]
    private async void OnBeginReviewedVoiceCommand(object? sender, RoutedEventArgs args) =>
        await RunResponseActionAsync(() =>
        {
            var revision = ++reviewedCommandControlRevision;
            return viewModel.BeginReviewedVoiceCommandAsync(() => !sessionWindowClosed && IsVisible
                && revision == reviewedCommandControlRevision);
        });

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native Click boundary reports failures through RunResponseActionAsync.")]
    private async void OnFinishReviewedVoiceCommand(object? sender, RoutedEventArgs args) =>
        await RunResponseActionAsync(viewModel.FinishReviewedVoiceCommandAsync);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native Click boundary reports failures through RunResponseActionAsync.")]
    private async void OnCancelReviewedVoiceCommand(object? sender, RoutedEventArgs args) =>
        await RunResponseActionAsync(viewModel.CancelReviewedVoiceCommandAsync);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native Click boundary reports failures through RunResponseActionAsync.")]
    private async void OnConfirmReviewedVoiceCommand(object? sender, RoutedEventArgs args)
    {
        if (reviewedCommandConfirm is { } confirm) { await RunResponseActionAsync(confirm); }
    }

    private void OnReviewedVoiceCommandTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (sender is TextBox input && viewModel.HasReviewedVoiceCommand
            && !string.Equals(input.Text, viewModel.ReviewedVoiceCommandText, StringComparison.Ordinal))
        { reviewedCommandEdit?.Invoke(input.Text ?? string.Empty); }
    }

    private void RefreshReviewedVoiceCommand()
    {
        reviewedCommandEdit = viewModel.CaptureReviewedVoiceCommandEdit();
        reviewedCommandConfirm = viewModel.CaptureReviewedVoiceCommandConfirmation();
        var input = this.FindControl<TextBox>("ReviewedVoiceCommandInput")
            ?? throw new InvalidOperationException("The reviewed command transcript control is unavailable.");
        if (!string.Equals(input.Text, viewModel.ReviewedVoiceCommandText, StringComparison.Ordinal))
        { input.Text = viewModel.ReviewedVoiceCommandText; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native lifetime boundary joins capture and reports failures through RunResponseActionAsync.")]
    private async void OnReviewedCommandUnavailable(object? sender, EventArgs args)
    {
        ++reviewedCommandControlRevision;
        if (viewModel.IsReviewedVoiceCommandActive)
        { await RunResponseActionAsync(viewModel.CancelReviewedVoiceCommandAsync); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native visibility boundary joins capture and reports failures through RunResponseActionAsync.")]
    private async void OnReviewedCommandVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty && !IsVisible)
        {
            ++reviewedCommandControlRevision;
            await RunResponseActionAsync(viewModel.CancelReviewedVoiceCommandAsync);
        }
    }
}
