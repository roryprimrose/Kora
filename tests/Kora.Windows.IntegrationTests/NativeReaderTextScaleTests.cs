using System.Text;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeReaderTextScaleTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task DetailRenderedSourceAndContinuousTextUseStableTypographyWithoutChangingOffsetsOrChrome(int percent)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = NativeUxTestFixture.Create(Path.GetTempPath());
            await fixture.InitializeAsync();
            var source = "# Heading\n\n" + string.Concat(Enumerable.Repeat("α 🙂 literal needle long line\n\n", 80));
            var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
                DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "Reader test", "Synthetic immutable fixture", source);
            var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(source);
            var state = new DetailViewerState(content);
            state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback).Should().BeTrue();
            var window = new DetailWindow(state, result, (_, _, _) => Task.CompletedTask, fixture.Main)
            {
                Width = 640,
                Height = 500,
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                var reader = window.FindControl<TextBox>("ContentReader")!;
                var originalFont = reader.FontSize;
                var findSpan = window.FindControl<NamedTextBlock>("FindSpan")!;
                var findFont = findSpan.FontSize;
                var heading = window.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<TextBlock>().First();
                var headingFont = heading.FontSize;
                var headingLineHeight = heading.LineHeight;
                var title = window.FindControl<TextBlock>("DetailTitle")!;
                var titleFont = title.FontSize;
                var close = window.FindControl<Button>("CloseDetail")!;
                var sourceToggle = window.FindControl<CheckBox>("SourceToggle")!;
                var viewport = window.FindControl<ScrollViewer>("RenderedViewport")!;
                var digest = content.Digest;
                foreach (var scale in new[] { percent, 200, 75, 100, percent })
                {
                    fixture.Main.ReaderTextScalePercent = scale;
                    await fixture.Main.AppearanceUpdate;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    reader.FontSize.Should().Be(originalFont * scale / 100d);
                    findSpan.FontSize.Should().Be(findFont * scale / 100d);
                    heading.FontSize.Should().Be(headingFont * scale / 100d);
                    heading.LineHeight.Should().Be(headingLineHeight * scale / 100d);
                    title.FontSize.Should().Be(titleFont);
                    close.IsVisible.Should().BeTrue();
                    close.Bounds.Height.Should().BeGreaterThan(0);
                    viewport.Bounds.Height.Should().BeGreaterThan(0);
                    viewport.Extent.Height.Should().BeGreaterThan(viewport.Viewport.Height);
                }
                window.RequestedThemeVariant = ThemeVariant.Light;
                window.UpdateLayout();
                heading.FontSize.Should().Be(headingFont * percent / 100d);
                sourceToggle.IsChecked = true;
                reader.Text.Should().Be(source);
                state.Search("needle").Should().BeTrue();
                var offset = state.MatchStart;
                offset.Should().Be(source.IndexOf("needle", StringComparison.Ordinal));
                reader.SelectionStart = offset;
                reader.SelectionEnd = offset + 6;
                fixture.Main.ReaderTextScalePercent = percent == 200 ? 75 : 200;
                await fixture.Main.AppearanceUpdate;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                reader.SelectionStart.Should().Be(offset);
                reader.SelectionEnd.Should().Be(offset + 6);
                state.MatchStart.Should().Be(offset);
                findSpan.Text.Should().Be("needle");
                findSpan.FontSize.Should().Be(findFont * (percent == 200 ? 75 : 200) / 100d);
                content.Source.Should().Be(source);
                content.Digest.Should().Be(digest);
                content.Utf8Bytes.Should().Be(Encoding.UTF8.GetByteCount(source));
                reader.Bounds.Height.Should().BeGreaterThan(0);
                state.SetSource(false);
                window.FindControl<CheckBox>("ReadingTextToggle")!.IsChecked = true;
                reader.Text.Should().Be(result.SemanticText);
                fixture.Main.ReaderTextScalePercent = percent;
                await fixture.Main.AppearanceUpdate;
                Dispatcher.UIThread.RunJobs();
                var secondState = new DetailViewerState(content);
                secondState.CompleteRender(secondState.Generation, result.SemanticText, result.Status, false);
                var second = new DetailWindow(secondState, result, (_, _, _) => Task.CompletedTask, fixture.Main);
                try { second.FindControl<TextBox>("ContentReader")!.FontSize.Should().Be(originalFont * percent / 100d); }
                finally { second.ClearAndClose(); }
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task OriginalFileAndClipboardBodiesAndExactSpansScaleButConsentAndLocationsDoNot(int percent)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = NativeUxTestFixture.Create(Path.GetTempPath());
            await fixture.InitializeAsync();
            var text = string.Concat(Enumerable.Repeat("α 🙂 needle " + new string('x', 200) + "\r\n", 80));
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            var review = new LocalFileReview(Guid.NewGuid(), Guid.NewGuid(), request,
                new(@"C:\Synthetic\reader.txt", "synthetic-file", Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch));
            var file = new LocalFileRevision(review, Encoding.UTF8.GetBytes(text), DateTimeOffset.UnixEpoch);
            var snapshot = new ClipboardSnapshot(Guid.NewGuid(), Guid.NewGuid(), request, text, 1, DateTimeOffset.UnixEpoch);
            var bytes = snapshot.GetUtf8Bytes();
            var failures = new List<string>();
            var fileWindow = new LocalFilePreviewWindow(failures.Add, fixture.Main);
            fileWindow.ShowRevision(file, (_, mode, _) => Task.FromResult(
                LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)),
                () => true, () => Task.CompletedTask);
            fileWindow.Show();
            var clipboardWindow = new ClipboardPreviewWindow(exception => failures.Add(exception.GetType().Name), fixture.Main);
            clipboardWindow.Show(snapshot, () => Task.CompletedTask, () => Task.CompletedTask, () => true);
            Dispatcher.UIThread.RunJobs();
            fileWindow.UpdateLayout();
            clipboardWindow.UpdateLayout();
            try
            {
                var fileReader = Text(fileWindow, "Untrusted local file plain text content");
                var clipboardReader = Text(clipboardWindow, "Clipboard snapshot plain text content");
                var identity = Text(fileWindow, "Local file review identity and bounds");
                var baseline = fileReader.FontSize;
                var chromeFont = identity.FontSize;
                var find = fileWindow.GetVisualDescendants().OfType<TextBox>().Single(control =>
                    AutomationProperties.GetName(control)?.StartsWith("Literal Find in", StringComparison.Ordinal) == true);
                find.Text = "needle";
                var findButton = fileWindow.GetVisualDescendants().OfType<Button>().Single(control =>
                    string.Equals(control.Content as string, "Find first", StringComparison.Ordinal));
                await ((Kora.Application.Infrastructure.AsyncCommand)findButton.Command!).ExecuteAsync();
                fileWindow.UpdateLayout();
                var location = Text(fileWindow, "Exact original source identity and end-exclusive location");
                var span = fileWindow.GetVisualDescendants().OfType<NamedTextBlock>().Single(control =>
                    AutomationProperties.GetName(control)?.StartsWith("Exact literal original source match", StringComparison.Ordinal) == true);
                var exactLocation = location.Text;
                exactLocation.Should().Contain($"UTF-16 [{text.IndexOf("needle", StringComparison.Ordinal)}, {text.IndexOf("needle", StringComparison.Ordinal) + 6})");
                var clipboardFind = clipboardWindow.GetVisualDescendants().OfType<TextBox>().Single(control =>
                    AutomationProperties.GetName(control)?.StartsWith("Case-sensitive literal Find", StringComparison.Ordinal) == true);
                clipboardFind.Text = "needle";
                var clipboardQueryFont = clipboardFind.FontSize;
                var clipboardFindButton = clipboardWindow.GetVisualDescendants().OfType<Button>().Single(control =>
                    string.Equals(control.Content as string, "Find first", StringComparison.Ordinal));
                clipboardFind.Focus().Should().BeTrue();
                await ((Kora.Application.Infrastructure.AsyncCommand)clipboardFindButton.Command!).ExecuteAsync();
                clipboardWindow.UpdateLayout();
                var clipboardLocation = Text(clipboardWindow, "Clipboard literal Find outcome and exact UTF-16 location");
                var clipboardSpan = Text(clipboardWindow, "Exact inert clipboard literal match");
                clipboardSpan.IsFocused.Should().BeTrue();
                var exactClipboardLocation = clipboardLocation.Text;
                exactClipboardLocation.Should().Contain($"UTF-16 [{text.IndexOf("needle", StringComparison.Ordinal)}, {text.IndexOf("needle", StringComparison.Ordinal) + 6})");
                foreach (var scale in new[] { percent, 200, 75, 100, percent })
                {
                    fixture.Main.ReaderTextScalePercent = scale;
                    await fixture.Main.AppearanceUpdate;
                    Dispatcher.UIThread.RunJobs();
                    fileWindow.UpdateLayout();
                    clipboardWindow.UpdateLayout();
                    fileReader.FontSize.Should().Be(baseline * scale / 100d);
                    clipboardReader.FontSize.Should().Be(baseline * scale / 100d);
                    identity.FontSize.Should().Be(chromeFont);
                    location.FontSize.Should().Be(chromeFont);
                    location.Text.Should().Be(exactLocation);
                    span.Text.Should().Be("needle");
                    span.FontSize.Should().Be(baseline * scale / 100d);
                    clipboardSpan.Text.Should().Be("needle");
                    clipboardSpan.FontSize.Should().Be(baseline * scale / 100d);
                    clipboardSpan.IsFocused.Should().BeTrue();
                    clipboardLocation.Text.Should().Be(exactClipboardLocation);
                    clipboardLocation.FontSize.Should().Be(chromeFont);
                    clipboardFind.Text.Should().Be("needle");
                    clipboardFind.FontSize.Should().Be(clipboardQueryFont);
                    fileReader.Text.Should().Be(text);
                    clipboardReader.Text.Should().Be(text);
                    foreach (var reader in new[] { fileReader, clipboardReader })
                    {
                        var viewport = reader.GetVisualAncestors().OfType<ScrollViewer>().First();
                        viewport.Bounds.Height.Should().BeGreaterThan(0);
                        viewport.Extent.Height.Should().BeGreaterThan(viewport.Viewport.Height);
                    }
                }
                snapshot.GetUtf8Bytes().Should().Equal(bytes);
                file.Text.Should().Be(text);
                failures.Should().BeEmpty();
            }
            finally { fileWindow.ClearAndClose(); clipboardWindow.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task NativeSettingsEditorUsesExactDomainBoundsAndRegistryBinding()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = NativeUxTestFixture.Create(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new SettingsWindow(fixture.Main);
            window.Show();
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 0;
            window.UpdateLayout();
            try
            {
                var spinner = window.GetVisualDescendants().OfType<NumericUpDown>().Single(control =>
                    string.Equals(AutomationProperties.GetName(control), "Native reader text scale percentage", StringComparison.Ordinal));
                spinner.Minimum.Should().Be(75);
                spinner.Maximum.Should().Be(200);
                spinner.Increment.Should().Be(1);
                spinner.Value.Should().Be(100);
                spinner.Value = 75;
                Dispatcher.UIThread.RunJobs();
                await fixture.Main.AppearanceUpdate;
                fixture.Main.ReaderTextScalePercent.Should().Be(75);
                fixture.Main.EnsureReaderTypographyAvailable();
            }
            finally { window.Close(); }
        });
    }

    private static NamedTextBlock Text(Window window, string name) =>
        window.GetVisualDescendants().OfType<NamedTextBlock>().Single(control =>
            string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));
}
