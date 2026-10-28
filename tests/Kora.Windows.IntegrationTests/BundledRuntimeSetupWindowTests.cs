using Avalonia;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills.RuntimeSetup;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class BundledRuntimeSetupWindowTests
{
    private static readonly string[] RecoveryActions = ["Verify", "Forget registration only"];
    [Fact]
    public async Task Actual_package_surface_native_review_prepare_restart_verify_and_forget_use_production_owner_and_audit()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var storage = new OwnedStorageFixture();
            using var native = new NativeUxFixtureSession(storage.LocalRoot);
            await native.InitializeAsync();
            native.Main.CanRevealPrivatePresentation.Should().BeTrue();
            native.Main.IsSkillReviewCallEligible.Should().BeTrue();
            var runtimeRoot = Path.Combine(storage.LocalRoot, "Runtime");
            Directory.CreateDirectory(runtimeRoot);
            WindowsBundledRuntimeInventoryTests.Populate(runtimeRoot);
            var inspector = new WindowsBundledRuntimeInstallationInspector(runtimeRoot, new WindowsBundledRuntimeInventoryTests.Metadata());
            var preferences = new LocalBundledRuntimeRegistrationPreferences(storage);
            await using var setup = new WindowsBundledRuntimeSetup(preferences, inspector, native.Audit,
                NullLogger<WindowsBundledRuntimeSetup>.Instance, TimeProvider.System);
            var services = new ServiceCollection();
            BundledRuntimeSetupComposition.Configure(services);
            services.AddSingleton(setup);
            await using var provider = services.BuildServiceProvider();
            var worker = provider.GetRequiredService<IBundledScriptWorker>();
            worker.Should().BeSameAs(setup.CreateWorker());
            BundledRuntimeSetupWindow? runtime = null;
            var packages = new SkillPackagesWindow(EmbeddedSkillCatalogue.Load(), new(NullLogger<NativeDetailRenderer>.Instance),
                current: () => true, runtimeSetup: _ =>
                {
                    runtime = new(native.Main, setup, () => true);
                    runtime.Show();
                });
            packages.Show();
            try
            {
                packages.FindControl<Button>("ReviewBundledRuntime")!.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                runtime.Should().NotBeNull();
                var window = runtime!;
                await Execute(window, "Inspect installed runtime");
                preferences.Load().Should().BeNull();
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var approve = Button(window, "Approve exact registration operation");
                approve.PropertyChanged += (_, args) =>
                {
                    if (args.Property == Visual.IsVisibleProperty && approve.IsVisible) { ready.TrySetResult(); }
                };
                var preparing = Execute(window, "Prepare");
                await AwaitReviewAsync(ready.Task, preparing, window);
                window.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain("Synthetic bundled notices")
                    .And.Contain("Modules\\shared.dll").And.Contain("Download: 0 bytes");
                preferences.Load().Should().BeNull();
                approve.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await preparing;
                setup.Description.Should().StartWith("Prepared.");
                preferences.LoadPending().Should().BeNull();
                worker.QualifiedImplementationDigest.Should().BeNull();
                await using var restarted = new WindowsBundledRuntimeSetup(
                    new LocalBundledRuntimeRegistrationPreferences(storage), inspector, native.Audit,
                    NullLogger<WindowsBundledRuntimeSetup>.Instance, TimeProvider.System);
                (await restarted.InspectAsync(CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Prepared);
                foreach (var action in RecoveryActions)
                {
                    ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var applying = Execute(window, action);
                    await AwaitReviewAsync(ready.Task, applying, window);
                    approve.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                    await applying;
                }
                preferences.Load().Should().BeNull();
                preferences.LoadPending().Should().BeNull();
                Directory.GetFiles(runtimeRoot, "*", SearchOption.AllDirectories).Should().HaveCount(12);
                worker.IsQuiescent.Should().BeTrue();
                window.Close();
            }
            finally { runtime?.Close(); packages.Close(); }
        });
    }

    [Fact]
    public async Task Native_close_clears_and_hides_but_retains_owner_until_ignoring_inspection_joins()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var storage = new OwnedStorageFixture();
            using var native = new NativeUxFixtureSession(storage.LocalRoot);
            await native.InitializeAsync();
            native.Main.CanRevealPrivatePresentation.Should().BeTrue();
            native.Main.IsSkillReviewCallEligible.Should().BeTrue();
            var fixture = new BundledRuntimeSetupFixture();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Native.Hold = () => release.Task;
            await using var setup = fixture.Service;
            var window = new BundledRuntimeSetupWindow(native.Main, setup, () => true);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            var work = Execute(window, "Inspect installed runtime");
            await fixture.Native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            window.Close();
            window.IsVisible.Should().BeFalse();
            closed.Task.IsCompleted.Should().BeFalse();
            work.IsCompleted.Should().BeFalse();
            window.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().BeEmpty();
            await FluentActions.Awaiting(() => setup.InspectAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
            release.TrySetResult();
            await work;
            await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
            fixture.Saved.Writes.Should().Be(0);
        });
    }

    private static Button Button(Window window, string label) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Content as string, label, StringComparison.Ordinal));

    private static Task Execute(Window window, string label) => ((AsyncCommand)Button(window, label).Command!).ExecuteAsync();

    private static async Task AwaitReviewAsync(Task ready, Task operation, Window window)
    {
        await Task.WhenAny(ready, operation).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        if (operation.IsCompleted) { await operation; }
        ready.IsCompleted.Should().BeTrue("the actual native review must be presented; status: {0}",
            string.Join('\n', window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)));
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
