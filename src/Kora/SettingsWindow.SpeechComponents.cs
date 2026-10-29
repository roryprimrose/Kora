using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora;

public sealed partial class SettingsWindow
{
    private CancellationTokenSource? speechComponentCancellation;

    private void BindSpeechComponents(MainViewModel viewModel)
    {
        var panel = this.FindControl<StackPanel>("SpeechComponentsPanel")
            ?? throw new InvalidOperationException("The optional speech setup panel is unavailable.");
        panel.Children.Add(new TextBlock { Text = "Optional speech-input components", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Preparing data never selects a speech profile, opens the microphone, enrolls a verifier or enables learning.",
            TextWrapping = TextWrapping.Wrap,
        });
        if (viewModel.SpeechComponentOffers.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = viewModel.IsSpeechComponentSetupAvailable
                    ? "No qualified optional speech-input components are offered by this Kora release. Existing Windows, typed and native input choices are unchanged."
                    : "Optional speech-input setup is unavailable. Existing input choices are unchanged; no preparation can start.",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        foreach (var component in viewModel.SpeechComponentInspectionTargets)
        {
            var status = new TextBlock { Text = "Inspect to check current local data. Installed is not selected or usable.", TextWrapping = TextWrapping.Wrap };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var inspect = new Button { Content = "Inspect" };
            AutomationProperties.SetName(inspect, component.Name + ": Inspect");
            actions.Children.Add(inspect);
            inspect.Click += async (_, _) => await RunSpeechComponentUiAsync(status, async token =>
            {
                var readiness = await viewModel.InspectSpeechComponentAsync(component.Identity, token);
                status.Text = DescribeSpeechComponent(readiness);
            });
            foreach (var operation in Enum.GetValues<SpeechComponentOperation>())
            {
                if (!viewModel.SpeechComponentOffers.Any(offer => offer.Identity == component.Identity)) { continue; }
                var button = new Button { Content = operation == SpeechComponentOperation.Prepare ? "Prepare / retry" : operation.ToString() };
                AutomationProperties.SetName(button, component.Name + ": " + operation);
                actions.Children.Add(button);
                button.Click += async (_, _) => await RunSpeechComponentUiAsync(status, async token =>
                {
                    var plan = await viewModel.ReviewSpeechComponentAsync(component.Identity, operation, token);
                    var approve = new Button { Content = "Approve " + operation };
                    var decline = new Button { Content = "Decline" };
                    var review = new Window
                    {
                        Title = "Review optional speech data operation", Width = 640, Height = 480,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new ScrollViewer
                        {
                            Content = new StackPanel
                            {
                                Margin = new Avalonia.Thickness(20), Spacing = 12,
                                Children =
                                {
                                    new TextBlock { Text = plan.Disclosure, TextWrapping = TextWrapping.Wrap },
                                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { approve, decline } },
                                },
                            },
                        },
                    };
                    approve.Click += (_, _) => review.Close(true);
                    decline.Click += (_, _) => review.Close(false);
                    using var cancellation = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => review.Close(false)));
                    status.Text = "Review the exact speech assets and approve or decline. Capture remains unchanged.";
                    var consent = await review.ShowDialog<bool>(this);
                    var result = await viewModel.ApplySpeechComponentAsync(plan, consent,
                        new Progress<string>(message => status.Text = message), token);
                    status.Text = DescribeSpeechComponent(result);
                });
            }
            panel.Children.Add(new TextBlock { Text = component.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            panel.Children.Add(status);
            panel.Children.Add(actions);
        }
        var cancel = new Button { Content = "Cancel speech data operation" };
        cancel.Click += (_, _) => speechComponentCancellation?.Cancel();
        cancel.IsVisible = viewModel.SpeechComponentInspectionTargets.Count != 0;
        panel.Children.Add(cancel);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible) { speechComponentCancellation?.Cancel(); }
        };
    }

    private async Task RunSpeechComponentUiAsync(TextBlock status,
        Func<CancellationToken, Task> operation)
    {
        if (speechComponentCancellation is not null)
        {
            status.Text = "A speech data operation is already active. Cancel or wait for its exact outcome.";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        speechComponentCancellation = cancellation;
        try
        {
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () => operation(cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            status.Text = "Cancelled. Data may remain installed or removal may be pending. Inspect, then review a fresh retry; no runtime was selected.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            status.Text = "Speech data operation did not complete: " + exception.Message
                + " Inspect current state before reviewing a retry. Existing input choices and personal profiles are unchanged.";
        }
        finally { speechComponentCancellation = null; }
    }

    private static string DescribeSpeechComponent(SpeechComponentReadiness state) =>
        (state.Storage is not { } storage ? "Unconfirmed. " : !storage.Installed ? "Assets unavailable. "
            : storage.Verified && state.Runtime.Functional == SpeechComponentFunctionalEvidence.Verified ? "Prepared assets. "
            : "Unconfirmed assets; review Verify or inspect recovery before retrying. ")
        + state.Description;
}
