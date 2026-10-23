using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AsyncEventBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copy_event_reports_deferred_fault_or_cancellation_without_claiming_success(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1),
                DetailContentKind.PlainText, DetailContentOrigin.EmbeddedDocument,
                DetailSensitivity.Public, "Boundary fixture", "Synthetic admitted snapshot", "exact source");
            var state = new DetailViewerState(content);
            var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance)
                .Render(content.Source, content.Kind);
            state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
            var copy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(DetailViewerState.Status), StringComparison.Ordinal)
                    && state.Status.StartsWith("Copy failed or is unconfirmed", StringComparison.Ordinal))
                { reported.TrySetResult(); }
            };
            var calls = 0;
            var window = new DetailWindow(state, result, (_, _, _) =>
            {
                calls++;
                return copy.Task;
            });
            try
            {
                window.Show();
                window.FindControl<Button>("CopySource")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                calls.Should().Be(1);
                reported.Task.IsCompleted.Should().BeFalse();
                if (cancel) { copy.SetCanceled(TestContext.Current.CancellationToken); }
                else { copy.SetException(new IOException("synthetic deferred write failure")); }

                await reported.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

                state.Status.Should().Be("Copy failed or is unconfirmed; no successful clipboard write is claimed.");
                state.Content.Should().BeSameAs(content);
                calls.Should().Be(1, "a failed write must not be retried automatically");
            }
            finally { window.ClearAndClose(); }
        });
    }
}
