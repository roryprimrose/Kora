using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SkillReviewNavigationTests
{
    [WindowsFact]
    public async Task ReviewReusesExactSingletonsAndDraftsWithOnlyExistingRegistrationMetadataRefresh()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            var preferences = new LocalSharedSkillPreferences(f);
            var source = new SharedSkillSource(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
            preferences.Save([source]);
            var before = File.ReadAllBytes(Path.Combine(f.LocalRoot, "Preferences", "shared-skill-sources.json"));
            var reader = new NoSourceOperations();
            var discovery = new SharedSkillDiscoveryService(preferences, reader, f.SharedAdmission, f.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            using var controller = Controller(f, discovery);
            SkillPackagesWindow? bundled = null;
            SharedSkillSourcesWindow? shared = null;
            var bundledOpens = 0;
            var sharedOpens = 0;
            using var one = Window.WindowOpenedEvent.AddClassHandler<SkillPackagesWindow>((view, _) => { bundled = view; bundledOpens++; });
            using var two = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((view, _) => { shared = view; sharedOpens++; });
            using var request = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Presentation);
            controller.TryOpen(SkillReviewSurface.BundledPackages, static () => true).Should().BeTrue();
            var bundledView = bundled ?? throw new InvalidOperationException("The bundled inspector did not open.");
            bundledView.FindControl<TextBox>("PackageQuery")!.Text = "unsaved bundled query";
            controller.Open();
            bundledOpens.Should().Be(1);
            bundledView.FindControl<TextBox>("PackageQuery")!.Text.Should().Be("unsaved bundled query");
            controller.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeTrue();
            var sharedView = shared ?? throw new InvalidOperationException("The shared inspector did not open.");
            await WhenEnabled(sharedView.FindControl<Button>("SearchSharedPackages")!).WaitAsync(f.Token);
            sharedView.RegisteredSources.Should().Equal(source);
            sharedView.FindControl<TextBox>("SharedQuery")!.Text = "unsaved shared query";
            controller.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeTrue();
            sharedOpens.Should().Be(1);
            sharedView.FindControl<TextBox>("SharedQuery")!.Text.Should().Be("unsaved shared query");
            reader.Calls.Should().Be(0);
            File.ReadAllBytes(Path.Combine(f.LocalRoot, "Preferences", "shared-skill-sources.json")).Should().Equal(before);
            controller.Dispose();
            bundledView.IsVisible.Should().BeFalse();
            sharedView.IsVisible.Should().BeFalse();
            controller.TryOpen(SkillReviewSurface.BundledPackages, static () => true).Should().BeFalse();
            controller.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeFalse();
            controller.Open();
            bundledOpens.Should().Be(1);
            sharedOpens.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("host")]
    [InlineData("eligibility")]
    [InlineData("digest")]
    [InlineData("native")]
    [InlineData("late-resource-revocation")]
    public async Task RequestedReviewFailureNeverOpensAnAlternateSurface(string reason)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            var eligible = true;
            using var controller = Controller(f, f.SharedSkills, () =>
            {
                if (reason is "digest") { throw new InvalidDataException("Synthetic resource verification failure"); }
                if (reason is "native") { throw new InvalidOperationException("Synthetic native failure"); }
                if (reason is "late-resource-revocation") { eligible = false; }
                return EmbeddedSkillCatalogue.Load();
            }, () => reason is not "host");
            var opens = 0;
            using var one = Window.WindowOpenedEvent.AddClassHandler<SkillPackagesWindow>((_, _) => opens++);
            using var two = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((_, _) => opens++);
            controller.TryOpen(SkillReviewSurface.BundledPackages, () => eligible && reason is not "eligibility")
                .Should().BeFalse();
            opens.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalDuringNativeShowCannotReviveOrReportAnOpenedWindow(bool shared)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            using var controller = Controller(f, f.SharedSkills);
            Window? opened = null;
            using var one = Window.WindowOpenedEvent.AddClassHandler<SkillPackagesWindow>((view, _) =>
            {
                opened = view;
                view.FindControl<TextBox>("PackageQuery")!.Text = "private opening query";
                controller.Dispose();
                view.FindControl<TextBox>("PackageQuery")!.Text.Should().BeEmpty();
                view.SelectedPackage.Should().BeNull();
            });
            using var two = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((view, _) =>
            {
                opened = view;
                view.FindControl<TextBox>("SharedQuery")!.Text = "private opening query";
                controller.Dispose();
                view.FindControl<TextBox>("SharedQuery")!.Text.Should().BeEmpty();
            });
            controller.TryOpen(shared ? SkillReviewSurface.SharedSources : SkillReviewSurface.BundledPackages, static () => true)
                .Should().BeFalse();
            opened.Should().NotBeNull();
            opened!.IsVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidMetadataOrVoiceOriginRetainsSeparateTruthfulNativeRecoveryWithoutDiscovery(bool voice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            if (!voice)
            {
                File.WriteAllText(Path.Combine(f.LocalRoot, "Preferences", "shared-skill-sources.json"), "not valid registration metadata");
            }
            var reader = new NoSourceOperations();
            var discovery = new SharedSkillDiscoveryService(new(f), reader, f.SharedAdmission, f.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            using var controller = Controller(f, discovery);
            SharedSkillSourcesWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((view, _) => window = view);
            using var request = HostActivity.BeginRoot(HostRequest.Create(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Presentation);
            controller.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeTrue();
            var view = window ?? throw new InvalidOperationException("The shared inspector did not open.");
            await WhenEnabled(view.FindControl<Button>("SearchSharedPackages")!).WaitAsync(f.Token);
            view.FindControl<TextBlock>("SharedStatus")!.Text.Should().Contain("Shared source unavailable:")
                .And.NotContain("not valid registration metadata");
            view.RegisteredSources.Should().BeEmpty();
            reader.Calls.Should().Be(0);
        });
    }

    private static NativeUxFixtureSession Fixture()
    {
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
        Directory.CreateDirectory(parent);
        return new(parent);
    }

    private static SkillPackagesWindowController Controller(NativeUxFixtureSession f, SharedSkillDiscoveryService discovery,
        Func<SkillPackageCatalogue>? load = null, Func<bool>? host = null) =>
        new(f.Main, host ?? (static () => true), NullLogger<SkillPackagesWindowController>.Instance,
            new(NullLogger<NativeDetailRenderer>.Instance), discovery, NullLogger<SharedSkillSourcesWindowController>.Instance, load);

    private static Task WhenEnabled(Button button)
    {
        if (button.IsEnabled) { return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        button.PropertyChanged += Changed;
        return completion.Task;
        void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Avalonia.Input.InputElement.IsEnabledProperty && button.IsEnabled)
            {
                button.PropertyChanged -= Changed;
                completion.TrySetResult();
            }
        }
    }

    private sealed class NoSourceOperations : ISharedSkillSourceReader
    {
        public int Calls { get; private set; }
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Navigation must not select a root."); }
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Navigation must not discover or verify source content."); }
    }
}
