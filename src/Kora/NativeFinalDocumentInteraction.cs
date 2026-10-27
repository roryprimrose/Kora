using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Hosting;

namespace Kora;

internal sealed class NativeFinalDocumentInteraction(
    FinalDocumentSource source, Func<bool> visible, Action<FinalDocumentNavigationRequest> review, Action<string> report)
{
    private FinalDocumentSource? source = source;
    private readonly List<Button> links = [];
    private readonly CancellationTokenSource lifetime = new();
    private bool retired;
    private bool reviewing;
    internal Task Pending { get; private set; } = Task.CompletedTask;
    internal bool CanInteract => source?.IsCurrent == true && visible();
    internal bool CanReadCode => source?.IsCurrent == true;

    internal Button CreateLink(NativeExternalDocumentLink link)
    {
        var button = new Button { Content = "Review external link: " + link.Destination, Tag = link };
        AutomationProperties.SetName(button, "Review untrusted final-answer external destination: " + link.Destination);
        AutomationProperties.SetHelpText(button, "Does not retrieve or open a website. Separate source and destination review and confirmation are required.");
        button.Click += OnLinkClicked;
        links.Add(button);
        return button;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native Click owns and observes Pending; expected admission failures are explicitly reported.")]
    private async void OnLinkClicked(object? sender, RoutedEventArgs args)
    {
        if (retired || reviewing) { return; }
        reviewing = true;
        Pending = ReviewAsync(sender);
        await Pending;
    }

    private async Task ReviewAsync(object? sender)
    {
        FinalDocumentNavigationRequest? request = null;
        try
        {
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                if (!CanInteract || sender is not Button { Tag: NativeExternalDocumentLink link })
                { throw new InvalidOperationException("The exact final-document link control is retired."); }
                var exactSource = source!;
                request = await exactSource.CaptureNavigationAsync(link.SourceStart, link.SourceLength, link.Destination, lifetime.Token);
                if (!CanInteract)
                { throw new InvalidOperationException("The original final-document action was retired during source validation."); }
                review(request);
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or InvalidDataException or IOException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            request?.Retire();
            report("External link review unavailable: the exact final source, original user action, privacy or browser/network admission changed. Nothing was fetched or opened.");
        }
        finally
        {
            reviewing = false;
            if (retired) { lifetime.Dispose(); }
        }
    }

    internal void Retire()
    {
        if (retired) { return; }
        retired = true;
        source = null;
        lifetime.Cancel();
        foreach (var button in links)
        {
            button.Click -= OnLinkClicked;
            button.Tag = null;
            button.Content = null;
            AutomationProperties.SetName(button, string.Empty);
            AutomationProperties.SetHelpText(button, string.Empty);
        }
        links.Clear();
        if (!reviewing) { lifetime.Dispose(); }
    }
}
