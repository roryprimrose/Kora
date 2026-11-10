using System.Reflection;
using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class MarkdownDisplaySettingsTests
{
    [Fact]
    public async Task NativeReviewCancelSaveAtomicRestartAndNewSeparateViewerPreserveOldViewerChoice()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."));
            await using var admission = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var preferences = new LocalMarkdownDisplayPreferences(f);
            await using var settings = new MarkdownDisplayConfigurationService(preferences, admission, f.Audit, TimeProvider.System,
                NullLogger<MarkdownDisplayConfigurationService>.Instance);
            await f.InitializeAsync(null, markdownDisplayConfiguration: settings);
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            controller.Navigate(SettingsSection.Responses, static () => true);
            // The controller has no public native-window getter; reflection only locates its existing owned window.
            var window = (SettingsWindow)typeof(SettingsWindowController).GetField("window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            async Task Run(string name)
            {
                var button = window.FindControl<Button>(name)!;
                button.IsEnabled.Should().BeTrue();
                await ((AsyncCommand)button.Command!).ExecuteAsync();
            }
            await Run("SourceMarkdownDisplay");
            f.Main.MarkdownDisplayReviewText.Should().Contain("Rendered (unsaved default) -> Source");
            preferences.Load().Should().BeNull("review does not save");
            await Run("CancelMarkdownDisplay");
            preferences.Load().Should().BeNull();
            await Run("SourceMarkdownDisplay");
            await Run("ConfirmMarkdownDisplay");
            preferences.Load().Should().Be(MarkdownDisplayMode.Source);
            new LocalMarkdownDisplayPreferences(f).Load().Should().Be(MarkdownDisplayMode.Source);
            f.Main.MarkdownDisplayStatus.Should().Contain("Source (saved)");
            var views = new List<DetailWindow>();
            var constructionFailures = new List<InvalidOperationException>();
            using var details = new DetailWindowController(f.Documents, () => f.Main.CanRevealPrivatePresentation,
                new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance), new NoClipboard(),
                (state, result, copy) =>
                {
                    try
                    {
                        var view = new DetailWindow(state, result, copy, f.Main);
                        views.Add(view);
                        return view;
                    }
                    catch (InvalidOperationException exception) { constructionFailures.Add(exception); throw; }
                }, NullLogger<DetailWindowController>.Instance, markdownDisplay: settings.CaptureAsync);
            var first = Content();
            var opened = await details.OpenCurrentResponseAsync(first, window, () => true, TestContext.Current.CancellationToken);
            constructionFailures.Should().BeEmpty();
            opened.Should().BeTrue();
            var reader = views[0].Reader;
            reader.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().BeTrue();
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(first.Source);
            reader.FindControl<CheckBox>("SourceToggle")!.IsChecked = false;
            await Run("RenderedMarkdownDisplay");
            await Run("ConfirmMarkdownDisplay");
            views.Should().ContainSingle("saving must never open another result");
            reader.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().BeFalse();
            (await details.OpenCurrentResponseAsync(first, window, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
            views.Should().ContainSingle();
            reader.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().BeFalse();
            (await details.OpenCurrentResponseAsync(Content(), window, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
            views[1].Reader.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().BeFalse();
            await f.Main.ResetMarkdownDisplayCommand.ExecuteAsync();
            f.Main.MarkdownDisplayReviewText.Should().Contain("-> Rendered");
            await Run("CancelMarkdownDisplay");
            controller.Dispose();
            f.Main.CanControlMarkdownDisplayNative.Should().BeFalse();
            f.Main.HasVoiceConsent.Should().BeFalse();
        });
    }

    private static AdmittedDetailContent Content()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        return new(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown, DetailContentOrigin.FinalizedResponse,
            DetailSensitivity.DisclosureConfirmationRequired, "Exact final answer", "Original host final source",
            "# Answer\n\nExact **Markdown** source.", new(request.SessionId, request.RequestId, request.TaskId));
    }

    internal sealed class Preferences : IMarkdownDisplayPreferences
    {
        internal MarkdownDisplayMode? Value { get; set; }
        private bool pending;
        public MarkdownDisplayMode? Load() => pending ? throw new InvalidDataException() : Value;
        public MarkdownDisplayMode? ReadBack() => Value;
        public void BeginWrite() => pending = true;
        public void Save(MarkdownDisplayMode mode) => Value = mode;
        public void ConfirmWrite() => pending = false;
    }

    private sealed class NoClipboard : IDetailClipboard
    {
        public Task WriteAsync(IDetailView view, string source, string? html) =>
            throw new InvalidOperationException("This preference grants no clipboard authority.");
    }
}
