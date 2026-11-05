using System.ComponentModel;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;

namespace Kora;

internal sealed class ExceptionReportWindow : Window
{
    private readonly ExceptionReportViewModel model;
    private readonly MainViewModel? typographyHost;
    private readonly TextBox preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel matches = new() { Spacing = 6 };
    private readonly Button check = new() { Content = "Check GitHub using this fingerprint" };
    private readonly Button openNew = new() { Content = "Approve report and review new issue on GitHub" };
    private readonly Button regression = new() { Content = "Approve report and review possible regression on GitHub" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    internal ExceptionReportWindow(ExceptionReportViewModel model, MainViewModel? typographyHost = null)
    {
        typographyHost?.EnsureReaderTypographyAvailable();
        this.model = model;
        this.typographyHost = typographyHost;
        Title = "Review a problem report";
        Width = 900;
        Height = 760;
        MinWidth = 640;
        MinHeight = 500;
        AutomationProperties.SetName(preview, "Exact public GitHub report preview");
        AutomationProperties.SetName(check, "Approve fingerprint lookup on GitHub");
        var cancel = new Button { Content = "Close without posting" };
        var layout = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto") };
        var disclosure = new TextBlock { Text = ExceptionReportViewModel.Disclosure, TextWrapping = TextWrapping.Wrap };
        if (typographyHost is not null)
        {
            NativeReaderTypography.Bind(preview, typographyHost);
            NativeReaderTypography.Bind(disclosure, typographyHost);
            NativeReaderTypography.Bind(status, typographyHost);
        }
        var existing = new ScrollViewer { MaxHeight = 160, Content = matches };
        var actions = new WrapPanel { Children = { check, openNew, regression, cancel } };
        Grid.SetRow(preview, 1);
        Grid.SetRow(existing, 2);
        Grid.SetRow(status, 3);
        Grid.SetRow(actions, 4);
        layout.Children.Add(disclosure);
        layout.Children.Add(preview);
        layout.Children.Add(existing);
        layout.Children.Add(status);
        layout.Children.Add(actions);
        Content = layout;
        check.Click += async (_, _) => await RunAsync(model.CheckAsync);
        openNew.Click += async (_, _) => await RunAsync(model.OpenNewAsync);
        regression.Click += async (_, _) => await RunAsync(model.OpenNewAsync);
        cancel.Click += (_, _) => Close();
        model.PropertyChanged += OnChanged;
        timer.Tick += OnTick;
        Opened += (_, _) => timer.Start();
        Closed += (_, _) =>
        {
            timer.Stop();
            timer.Tick -= OnTick;
            model.PropertyChanged -= OnChanged;
            model.Close();
            preview.Text = string.Empty;
            status.Text = string.Empty;
            disclosure.Text = string.Empty;
            matches.Children.Clear();
        };
        Refresh();
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, action); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status.Text = "Problem-report operation unconfirmed. No issue submission is verified; inspect the browser before retrying.";
        }
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (!model.IsCurrent) { Close(); }
        else if (typographyHost?.IsReaderTypographyUpdatePending != true)
        {
            try { typographyHost?.EnsureReaderTypographyAvailable(); }
            catch (InvalidOperationException)
            {
                Close();
                typographyHost!.ReportEvidencePresentationUnavailable();
            }
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Refresh();

    private void Refresh()
    {
        preview.Text = model.ReportText;
        status.Text = model.Status;
        check.IsEnabled = model.CanCheck;
        openNew.IsEnabled = model.CanOpenNew;
        regression.IsEnabled = model.CanOpenRegression;
        matches.Children.Clear();
        foreach (var match in model.Matches)
        {
            var button = new Button
            {
                Content = "#" + match.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " [" + (match.Closed ? "Closed" : "Open") + "] " + match.Title,
                IsEnabled = model.CanCheck,
            };
            button.Click += async (_, _) => await RunAsync(() => model.OpenExistingAsync(match));
            matches.Children.Add(button);
        }
    }
}
