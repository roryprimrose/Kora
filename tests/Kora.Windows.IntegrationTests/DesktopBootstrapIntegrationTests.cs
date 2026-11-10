using System.ComponentModel;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Context;
using Kora.Core.Coordination;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Presentation;
using Kora.Core.Voice;
using Kora.Controls;
using Kora.NativeUxFixture;
using Kora.Windows.Dependencies;
using Kora.Windows.Coordination;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Kora.Windows.Presentation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using static Kora.NativeUxFixture.FixtureBoundaries;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DesktopBootstrapIntegrationTests
{
    [Fact]
    public Task Production_graph_and_framework_binding_complete_with_unavailable_platform_services() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            await using var fixture = new BootstrapFixture();
            fixture.BuildProvider();

            fixture.StartDesktop();
            await fixture.RunWindowStartupAsync();

            fixture.Lifetime.MainWindow.Should().BeOfType<MainWindow>()
                .Which.DataContext.Should().BeSameAs(fixture.Main);
            fixture.App.Details.Should().NotBeNull();
            fixture.Ownership.IsReady.Should().BeTrue();
            fixture.Lifecycle.HasInstallerBinding.Should().BeTrue();
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
            fixture.Main.Dependencies.Should().Contain(status => string.Equals(status.Id, "local.inference", StringComparison.Ordinal)
                && status.Readiness == DependencyReadiness.Blocked);
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.ForbiddenNetworkCalls.Should().Be(0);
            fixture.PowerShell.ForbiddenCalls.Should().Be(0);

            await fixture.StopDesktopAsync();

            fixture.Ownership.IsReady.Should().BeFalse();
            fixture.Lifecycle.HasInstallerBinding.Should().BeFalse();
            fixture.Calls.SubscriberCount.Should().Be(0);
            fixture.ForbiddenNetworkCalls.Should().Be(0);
            fixture.PowerShell.ForbiddenCalls.Should().Be(0);
        });

    [Fact]
    public Task Missing_late_bound_documentation_service_fails_before_desktop_readiness() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            await using var fixture = new BootstrapFixture();
            fixture.BuildProvider(services => services.RemoveAll<IUserDocumentationProvider>());

            var start = fixture.StartDesktop;
            start.Should().Throw<InvalidOperationException>()
                .WithMessage("*IUserDocumentationProvider*");
            fixture.Ownership.IsReady.Should().BeFalse();
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.ForbiddenNetworkCalls.Should().Be(0);
            fixture.PowerShell.ForbiddenCalls.Should().Be(0);

            await fixture.StopDesktopAsync();

            fixture.Calls.SubscriberCount.Should().Be(0);
            var settledProbes = fixture.Probe.Calls;
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            fixture.Probe.Calls.Should().Be(settledProbes);
        });

    [Fact]
    public Task Worker_call_observation_updates_actual_bootstrapped_controls_on_the_ui_dispatcher() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            await using var fixture = new BootstrapFixture();
            fixture.BuildProvider();
            fixture.StartDesktop();
            await fixture.RunWindowStartupAsync();
            var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, PropertyChangedEventArgs args)
            {
                Dispatcher.UIThread.CheckAccess().Should().BeTrue();
                if (string.Equals(args.PropertyName, nameof(MainViewModel.CurrentCallState), StringComparison.Ordinal)
                    && fixture.Main.CurrentCallState == CallState.Active)
                {
                    updated.TrySetResult();
                }
            }
            fixture.Main.PropertyChanged += Changed;
            try
            {
                await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, async () =>
                {
                    await Task.Run(() => fixture.Calls.Publish(CallState.Active), TestContext.Current.CancellationToken);
                    await updated.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
                });
                fixture.Main.IsProtectedCall.Should().BeTrue();
                fixture.Main.IsSpeechResponseEnabled.Should().BeFalse();
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.ForbiddenNetworkCalls.Should().Be(0);
                fixture.PowerShell.ForbiddenCalls.Should().Be(0);
            }
            finally { fixture.Main.PropertyChanged -= Changed; }
        });

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

    private sealed class BootstrapFixture : IAsyncDisposable
    {
        private readonly OwnedStorageFixture storage = new();
        private readonly FileEvidenceHealth fileHealth = new();
        private readonly Serilog.Core.Logger fileLogger;
        private readonly ServiceProvider previousServices = Kora.App.Services;
        private readonly NativeDetailRenderer previousRenderer = MarkdownDocumentRenderer.Renderer;
        private readonly bool previousManualStartup = Kora.App.IsManualStartup;
        private readonly TrayIcons? previousTrayIcons = TrayIcon.GetIcons(Avalonia.Application.Current!);
        private readonly FixtureAccess access = new();
        private readonly FixtureExecution execution = new();
        private readonly List<BlockedNetwork> networks = [];
        private ServiceProvider? provider;
        private bool desktopAttempted;
        private bool stopped;

        internal BootstrapFixture() => fileLogger = DailyFileLoggerFactory.Create(storage, fileHealth);

        internal App App { get; } = new();
        internal ClassicDesktopStyleApplicationLifetime Lifetime { get; } = new();
        internal DesktopInstanceOwnershipBridge Ownership { get; } = new();
        internal FixtureVoice Voice { get; } = new();
        internal CallSource Calls { get; } = new();
        internal DeniedPowerShell PowerShell { get; } = new();
        internal DeniedLifecycle Lifecycle { get; } = new();
        internal UnavailableProbe Probe { get; } = new();
        internal int ForbiddenNetworkCalls => networks.Sum(network => network.ForbiddenCalls);
        internal MainViewModel Main => (provider ?? throw new InvalidOperationException("Bootstrap provider is not built."))
            .GetRequiredService<MainViewModel>();

        internal void BuildProvider(Action<IServiceCollection>? change = null)
        {
            var services = new ServiceCollection();
            Program.ConfigureServices(services, storage, fileLogger, fileHealth, Ownership,
                Lifecycle, () => new WindowsPowerShellSetupService(PowerShell), () => execution);
            ReplacePlatform<IArtifactDirectoryPreferences>(services, new IsolatedArtifactDirectories());
            ReplacePlatform<ISessionController>(services, access);
            ReplacePlatform<IWindowsPrivacyObservationService>(services, access);
            ReplacePlatform<IMicrophoneAccessService>(services, access);
            ReplacePlatform<ICurrentUserNameProvider>(services, access);
            ReplacePlatform<IApplicationProcessController>(services, execution);
            ReplacePlatform<IVoiceRecognitionService>(services, Voice);
            ReplacePlatform<IActivatedVoiceRecognitionService>(services, Voice);
            ReplacePlatform<ISpeechInputProfileRuntime>(services, Voice);
            ReplacePlatform<ISpeechSetupComparisonRuntime>(services, new DeniedComparison());
            ReplacePlatform<ITextToSpeechService>(services, new FixtureSpeech());
            ReplacePlatform<ISpeechPreparationService>(services, new DeniedPreparation());
            ReplacePlatform<ICallStateService>(services, Calls);
            ReplacePlatform<ICommunicationActivitySource>(services, new DeniedCommunication());
            ReplacePlatform<ILogonStartupRegistration>(services, new DeniedLogonRegistration());
            ReplacePlatform<IPlainTextClipboardReader>(services, new FixtureClipboardReader());
            ReplacePlatform<Func<Window, IWindowsPresenceWindowInput>>(services, _ => new HeadlessPresenceInput());
            ReplacePlatform<Func<IMotionPreferenceSource>>(services, static () => new HeadlessMotion());
            ReplacePlatform<Func<nint, AvaloniaPresencePresenter.IBitmapTarget>>(services, _ => new HeadlessBitmap());
            ReplacePlatform<HttpClient>(services, CreateClient());
            var keyed = services.Single(descriptor => descriptor.ServiceType == typeof(HttpClient)
                && descriptor.IsKeyedService && descriptor.ServiceKey is "ollama-probe");
            services.Remove(keyed);
            services.AddKeyedSingleton<HttpClient>("ollama-probe", (_, _) => CreateClient());
            services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IDependencyProbe));
            services.RemoveAll<IDependencyProbe>();
            services.AddSingleton<IDependencyProbe>(Probe);
            change?.Invoke(services);
            provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        }

        internal void StartDesktop()
        {
            Kora.App.Services = provider ?? throw new InvalidOperationException("Bootstrap provider is not built.");
            Kora.App.IsManualStartup = false;
            App.ApplicationLifetime = Lifetime;
            desktopAttempted = true;
            App.Initialize();
            App.OnFrameworkInitializationCompleted();
        }

        internal async Task RunWindowStartupAsync()
        {
            var window = Lifetime.MainWindow as MainWindow
                ?? throw new InvalidOperationException("The bootstrapped main window is unavailable.");
            window.Show();
            await window.InitializationQuiescence.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        internal async Task StopDesktopAsync()
        {
            if (stopped) { return; }
            stopped = true;
            try
            {
                await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, async () =>
                {
                    var main = provider?.GetService<MainViewModel>();
                    if (desktopAttempted)
                    {
                        var initialization = Lifetime.MainWindow is MainWindow window
                            ? window.InitializationQuiescence : Task.CompletedTask;
                        App.DisposeDesktopControllers();
                        await initialization;
                        await Kora.App.DetailPreferenceQuiescence;
                    }
                    if (main is not null) { await main.DisposeAsync(); }
                    if (provider is not null) { await provider.DisposeAsync(); }
                });
            }
            finally
            {
                Kora.App.Services = previousServices;
                Kora.App.IsManualStartup = previousManualStartup;
                MarkdownDocumentRenderer.Renderer = previousRenderer;
                TrayIcon.SetIcons(Avalonia.Application.Current!, previousTrayIcons);
                Lifetime.MainWindow = null;
                App.ApplicationLifetime = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopDesktopAsync(); }
            finally { fileLogger.Dispose(); storage.Dispose(); }
        }

        private HttpClient CreateClient()
        {
            var network = new BlockedNetwork();
            networks.Add(network);
            return new(network);
        }

        private static void ReplacePlatform<T>(IServiceCollection services, T instance) where T : class
        {
            var descriptor = services.Single(item => item.ServiceType == typeof(T) && !item.IsKeyedService);
            if (descriptor.ImplementationInstance is IDisposable disposable) { disposable.Dispose(); }
            services.Replace(ServiceDescriptor.Singleton(instance));
        }
    }

    private sealed class IsolatedArtifactDirectories : IArtifactDirectoryPreferences
    {
        public ArtifactDirectorySettings Load() => new([], [], [], [], [], [], useDefaultDirectories: false);
        public void Save(ArtifactDirectorySettings settings) =>
            throw new InvalidOperationException("Bootstrap must not change artifact source preferences.");
    }

    private sealed class DeniedLifecycle : IInstanceLifecycleController, IWindowsInstallerShutdownHost
    {
        internal bool HasInstallerBinding { get; private set; }
        public void BindInstallerShutdown(Func<CancellationToken, Task<bool>>? shutdown) =>
            HasInstallerBinding = shutdown is not null;
        public void RequestRestartAfterQuiescence() =>
            throw new InvalidOperationException("Bootstrap must not restart an application.");
    }

    private sealed class DeniedPowerShell : IPowerShellProcessRunner
    {
        internal int ForbiddenCalls { get; private set; }
        public IReadOnlyList<string> GetCandidatePaths() => [];
        public Task<string> GetVersionAsync(string executable, CancellationToken cancellationToken)
        {
            ForbiddenCalls++;
            throw new InvalidOperationException("Bootstrap must not launch PowerShell.");
        }
        public Task<int> InstallAsync(CancellationToken cancellationToken)
        {
            ForbiddenCalls++;
            throw new InvalidOperationException("Bootstrap must not install PowerShell.");
        }
    }

    private sealed class BlockedNetwork : HttpMessageHandler
    {
        internal int ForbiddenCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            ForbiddenCalls++;
            throw new InvalidOperationException("Bootstrap must not send network requests.");
        }
    }

    private sealed class DeniedPreparation : ISpeechPreparationService
    {
        public Task PrepareAsync(SpeechVoice voice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bootstrap must not prepare a speech runtime.");
    }

    private sealed class UnavailableProbe : IDependencyProbe
    {
        internal int Calls { get; private set; }
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new DependencyStatus("local.inference", "Unavailable fixture runtime",
                DependencyReadiness.Blocked, "Bootstrap has no qualified provider."));
        }
    }

    private sealed class DeniedComparison : ISpeechSetupComparisonRuntime
    {
        public IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() => [];
        public Task<ISpeechSetupComparisonLease> AcquireComparisonAsync(SpeechInputGeneration generation,
            MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Bootstrap must not acquire audio comparison resources.");
    }

    private sealed class DeniedCommunication : ICommunicationActivitySource
    {
        public bool IsQualified => false;
        public bool HasUnreleasedResources => false;
        public Task ObserveAsync(Guid generation, Action<CommunicationActivityObservation> publish, CancellationToken token) =>
            throw new InvalidOperationException("Bootstrap must not start native communication observation.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DeniedLogonRegistration : ILogonStartupRegistration
    {
        public LogonStartupRegistration Inspect() =>
            new(false, null, "fixture.unavailable", "Bootstrap fixture has no installed-logon registration.");
        public void Apply(bool enabled, LogonStartupRegistration reviewed, Func<bool> admitted) =>
            throw new InvalidOperationException("Bootstrap must not change Windows logon startup.");
    }

    private sealed class CallSource : ICallStateService
    {
        private EventHandler<CallStateChangedEventArgs>? changed;
        private volatile CallState state = CallState.Unavailable;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged
        {
            add => changed += value;
            remove => changed -= value;
        }
        public CallState CurrentState => state;
        internal int SubscriberCount => changed?.GetInvocationList().Length ?? 0;
        internal void Publish(CallState value) { state = value; changed?.Invoke(this, new(value)); }
    }

    private sealed class HeadlessPresenceInput : IWindowsPresenceWindowInput
    {
        public bool InterceptsMouse => false;
        public bool IsGestureActive => false;
        public bool Refresh(bool canInteract) => false;
    }

    private sealed class HeadlessMotion : IMotionPreferenceSource
    {
        public MotionPreference Read() => MotionPreference.ReducedMotion;
        public IDisposable Subscribe(Action changed) => new Subscription();
        private sealed class Subscription : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class HeadlessBitmap : AvaloniaPresencePresenter.IBitmapTarget
    {
        public nint Handle => 0;
        public void Move(int x, int y) { }
        public void CopyFrame(int width, int height, Action<nint, int, int> copyPixels) { }
        public void Present() { }
        public void Hide() { }
        public void ReleaseDrawingResources() { }
        public void Dispose() { }
    }
}
