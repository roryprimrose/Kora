using Avalonia.Controls;
using Avalonia.Interactivity;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora;

public sealed partial class ResponseWindow
{
    private ExternalBrowserReviewWindow? externalBrowserReviewWindow;

    private void OnExternalBrowserClicked(object? sender, RoutedEventArgs eventArgs)
    {
        try
        {
            HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
            {
                externalBrowserReviewWindow?.Close();
                if (sender is not Button { Tag: DetailContentReference reference })
                { throw new InvalidOperationException("The exact web-result control was retired."); }
                var review = viewModel.PrepareExternalBrowser(reference);
                var opened = new ExternalBrowserReviewWindow(viewModel, review);
                externalBrowserReviewWindow = opened;
                void CloseReview(object? source, EventArgs args) => opened.Close();
                Closed += CloseReview;
                opened.Closed += (_, _) =>
                {
                    Closed -= CloseReview;
                    if (ReferenceEquals(externalBrowserReviewWindow, opened)) { externalBrowserReviewWindow = null; }
                };
                opened.Show(this);
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            viewModel.CancelExternalBrowserReview();
            viewModel.ReportWebResultDetailStatus("External browser unavailable: the exact source was retired or native host/network admission failed. "
                + "Offline/local-only mode denies opening; no mode was changed. Retrieve a fresh source and review again when eligible.");
        }
    }
}
