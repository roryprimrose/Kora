using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Controls;

namespace Kora.SettingInspection;

internal sealed class SettingInspectionWindow : Window
{
    private readonly SettingInspectionPresenter presenter;
    private readonly Action<Exception> failure;
    private bool joined;
    private bool closing;

    internal SettingInspectionWindow(SettingInspectionPresenter presenter, Action<Exception> failure, MainViewModel? typography = null)
    {
        this.presenter = presenter;
        this.failure = failure;
        Title = "Inspect current Kora settings";
        Width = 850;
        Height = 720;
        DataContext = presenter;
        var question = new TextBox { PlaceholderText = "Original question (256 characters / 512 UTF-8 bytes maximum)" };
        question.Bind(TextBox.TextProperty, new Binding(nameof(presenter.Question)) { Mode = BindingMode.TwoWay });
        if (typography is not null) { NativeReaderTypography.Bind(question, typography); }
        AutomationProperties.SetName(question, "Original question about confirmed Kora settings");
        var review = new Button { Content = "Inspect confirmed settings and review" };
        review.Command = new AsyncCommand(presenter.ReviewAsync, failure);
        review.Bind(IsEnabledProperty, new Binding(nameof(presenter.CanReview)));
        var confirm = new Button { Content = "Confirm ONE tool-free LocalOnly request" };
        confirm.Command = new AsyncCommand(presenter.ConfirmAsync, failure);
        confirm.Bind(IsEnabledProperty, new Binding(nameof(presenter.CanConfirm)));
        var cancel = new Button { Content = "Cancel and clear inspection" };
        cancel.Command = new AsyncCommand(presenter.CancelAsync, failure);
        var panel = new StackPanel { Margin = new(20), Spacing = 12 };
        foreach (var control in new Control[] { question, review,
            Text(nameof(presenter.ReviewText), "Complete confirmed source provenance question and local target"),
            confirm, cancel, Text(nameof(presenter.Status), "Setting inspection outcome"),
            Text(nameof(presenter.Answer), "Genuine local setting inspection answer") })
        {
            if (typography is not null && control is TextBlock reader) { NativeReaderTypography.Bind(reader, typography); }
            panel.Children.Add(control);
        }
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => presenter.RefreshAdmission();
        timer.Start();
        Closing += OnClosing;
        Closed += (_, _) => timer.Stop();
    }

    private static NamedTextBlock Text(string property, string name)
    {
        var text = new NamedTextBlock { TextWrapping = TextWrapping.Wrap, Focusable = true };
        text.Bind(TextBlock.TextProperty, new Binding(property));
        AutomationProperties.SetName(text, name);
        return text;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods",
        Justification = "Avalonia Closing boundary; all asynchronous join failures are explicitly reported.")]
    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (joined) { return; }
        args.Cancel = true;
        if (closing) { return; }
        closing = true;
        try { Hide(); await presenter.CloseAsync(); }
        catch (Exception exception) { failure(exception); }
        finally { joined = true; Close(); }
    }
}
