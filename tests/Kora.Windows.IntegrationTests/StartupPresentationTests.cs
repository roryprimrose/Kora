using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Controls;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class StartupPresentationTests
{
    private static string ScratchParent()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, ".git")) && !Directory.Exists(Path.Combine(root.FullName, ".git")))
        { root = root.Parent ?? throw new InvalidOperationException("The native startup fixture requires its repository scratch root."); }
        var path = Path.Combine(root.FullName, ".scratch", "autonomous-delivery", "manual-startup-presentation-20261030", "native");
        Directory.CreateDirectory(path);
        return path;
    }

    [Theory]
    [InlineData(StartupPresentation.Workspace)]
    [InlineData(StartupPresentation.Tray)]
    [InlineData(null)]
    public async Task NativeReviewSaveReadbackIsConsumedByNextOwnedManualLifetimeWithoutSourceReads(StartupPresentation? choice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var original = new NativeUxFixtureSession(ScratchParent());
            await using var admission = new LogonStartupAdmission(original.Interactions, original.Interactions, new(original.Tasks));
            var preferences = new LocalStartupPresentationPreferences(original);
            var configuration = new StartupPresentationConfigurationService(preferences, admission, original.Audit, TimeProvider.System);
            await original.InitializeAsync(null, callState: new Call(), startupPresentationConfiguration: configuration);
            original.Main.StartupPresentationStatus.Should().Be(configuration.Status);
            using var settingsController = new SettingsWindowController(original.Main, NullLogger<SettingsWindowController>.Instance);
            SettingsWindow? settings = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SettingsWindow>((window, _) => settings = window);
            original.Main.ShowSettings();
            settings.Should().NotBeNull();
            settings!.FindControl<Button>("ReviewWorkspaceStartup")!.Command.Should().BeSameAs(original.Main.ReviewWorkspaceStartupCommand);
            settings.FindControl<Button>("ConfirmStartupPresentation")!.Command.Should().BeSameAs(original.Main.ConfirmStartupPresentationCommand);
            settings.FindControl<SettingActionsButton>("StartupPresentationActions")!.ResetCommand.Should().BeSameAs(original.Main.ResetStartupPresentationCommand);
            await original.Main.ReviewWorkspaceStartupCommand.ExecuteAsync();
            preferences.Load().Should().BeNull();
            settings.Hide();
            original.Main.ShowSettings();
            await original.Main.ConfirmStartupPresentationCommand.ExecuteAsync();
            preferences.Load().Should().BeNull("hiding retires the original native review");
            if (choice is StartupPresentation.Workspace) { await original.Main.ReviewWorkspaceStartupCommand.ExecuteAsync(); }
            else if (choice is StartupPresentation.Tray) { await original.Main.ReviewTrayStartupCommand.ExecuteAsync(); }
            else { await original.Main.ResetStartupPresentationCommand.ExecuteAsync(); }
            await original.Main.ConfirmStartupPresentationCommand.ExecuteAsync();
            new LocalStartupPresentationPreferences(original).Load().Should().Be(choice);
            original.Main.StartupPresentationStatus.Should().Contain("Last confirmed");
            original.Main.StartupPresentationStatus.Should().Be(configuration.Status);

            using var next = new NativeUxFixtureSession(ScratchParent());
            await next.InitializeAsync(null, callState: new Call());
            var reconstructed = new StartupPresentationConfigurationService(new LocalStartupPresentationPreferences(original),
                admission, original.Audit, TimeProvider.System);
            using var responses = new ResponseWindowController(next.Main, NullLogger<ResponseWindowController>.Instance);
            using var sessions = new SessionsWindowController(next.Main, next.Sessions, next.Evidence, next.Access,
                NullLogger<SessionsViewModel>.Instance);
            sessions.Bind();
            ResponseWindow? workspace = null;
            SessionsViewModel? state = null;
            sessions.CreateInteraction = (model, owner) =>
            {
                state = model;
                workspace = responses.PrepareSessions(model, owner, sessions.Open, static () => { });
                return workspace;
            };
            var before = await next.Interactions.ReadMetadataPageAsync(null, 50, next.Token);
            var startup = new ManualStartupPresentationController(reconstructed, next.Main, sessions.ShowStartupWorkspace);
            await startup.ApplyAsync(ManualStartupPresentationController.IsManualLaunch(["--manual-startup"]), next.Token);
            if (choice is StartupPresentation.Workspace)
            {
                workspace!.IsVisible.Should().BeTrue();
                workspace.IsExpanded.Should().BeTrue();
                workspace.ShowActivated.Should().BeFalse();
                state!.SelectedSessionRecord.Should().BeNull();
                state.Sessions.Should().BeEmpty();
                state.ConversationMessages.Should().BeEmpty();
                state.ComposerBody.Should().BeEmpty();
                state.HasSessionListPage.Should().BeFalse();
            }
            else { workspace.Should().BeNull("absence and tray preserve baseline; neither creates a workspace"); }
            (await next.Interactions.ReadMetadataPageAsync(null, 50, next.Token)).Records.Should().Equal(before.Records);
            next.Voice.StartCalls.Should().Be(0);
            next.Voice.ReplyCalls.Should().Be(0);
            next.Main.HasVoiceConsent.Should().BeFalse();
            next.Main.IsVoiceEnabled.Should().BeFalse();
            original.Main.HasVoiceConsent.Should().BeFalse();
            new LocalLogonStartupPreferences(original).Load().Should().BeNull();
            await startup.Invoking(x => x.ApplyAsync(true, next.Token)).Should().ThrowAsync<InvalidOperationException>();
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("--logon")]
    [InlineData("--manual-startup extra")]
    [InlineData("--Manual-startup")]
    public async Task LegacyLogonAndUnknownArgumentsNeverReadOrConsumeManualIntent(string argument)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(ScratchParent());
            await f.InitializeAsync(null, callState: new Call());
            await using var admission = new LogonStartupAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var preferences = new Preferences { Failure = new InvalidDataException("must not read logon intent") };
            var service = new StartupPresentationConfigurationService(preferences, admission, f.Audit, TimeProvider.System);
            var shows = 0;
            var startup = new ManualStartupPresentationController(service, f.Main, _ => shows++);
            var arguments = argument.Length == 0 ? [] : argument.Split(' ');
            await startup.ApplyAsync(ManualStartupPresentationController.IsManualLaunch(arguments), f.Token);
            preferences.Reads.Should().Be(0);
            preferences.Writes.Should().Be(0);
            shows.Should().Be(0);
            f.Voice.StartCalls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("configuration")]
    [InlineData("saved")]
    [InlineData("late-privacy")]
    [InlineData("malformed")]
    [InlineData("cancel")]
    public async Task StartupHoldsNeverReplayOnUnlockOrCallEndAndNeverCreateAWindow(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(ScratchParent());
            var call = new Call();
            await f.InitializeAsync(null, callState: call);
            await using var admission = new LogonStartupAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var preferences = new Preferences();
            var configuration = new StartupPresentationConfigurationService(preferences, admission, f.Audit, TimeProvider.System);
            using var token = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
            preferences.OnRead = count =>
            {
                if (count == 1)
                {
                    if (change is "privacy") { f.Access.SetOpen(false); }
                    if (change is "owner") { f.Main.BindCallOwnershipGate(static () => false); }
                    if (change is "call") { call.Set(Kora.Core.Communication.CallState.Active); }
                    if (change is "input") { f.Main.BindCallOwnershipGate(static () => true); }
                    if (change is "configuration") { configuration.Cancel(); }
                    if (change is "saved") { preferences.Value = StartupPresentation.Tray; }
                    if (change is "cancel") { token.Cancel(); }
                }
                if (count == 2 && change is "late-privacy") { f.Access.SetOpen(false); }
            };
            if (change is "malformed") { preferences.Failure = new InvalidDataException("malformed startup preference"); }
            var shows = 0;
            var startup = new ManualStartupPresentationController(configuration, f.Main, _ => shows++);
            await startup.Invoking(x => x.ApplyAsync(true, token.Token)).Should().ThrowAsync<Exception>();
            f.Access.SetOpen(true);
            call.Set(Kora.Core.Communication.CallState.Clear);
            await startup.Invoking(x => x.ApplyAsync(true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
            shows.Should().Be(0);
            preferences.Writes.Should().Be(0);
            f.Voice.StartCalls.Should().Be(0);
        });
    }

    private sealed class Preferences : IStartupPresentationPreferences
    {
        internal StartupPresentation? Value { get; set; } = StartupPresentation.Workspace;
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }
        internal Action<int>? OnRead { get; set; }
        internal Exception? Failure { get; set; }
        public StartupPresentation? Load()
        {
            var value = Value;
            Reads++; OnRead?.Invoke(Reads);
            return Failure is { } error ? throw error : value;
        }
        public StartupPresentation? ReadBack() => Load();
        public void BeginWrite() => Writes++;
        public void Save(StartupPresentation? value) { Value = value; Writes++; }
        public void ConfirmWrite() => Writes++;
    }
    private sealed class Call : Kora.Core.Communication.ICallStateService
    {
        public Kora.Core.Communication.CallState CurrentState { get; private set; } = Kora.Core.Communication.CallState.Clear;
        public event EventHandler<Kora.Core.Communication.CallStateChangedEventArgs>? StateChanged;
        internal void Set(Kora.Core.Communication.CallState value)
        {
            CurrentState = value;
            StateChanged?.Invoke(this, new(value));
        }
    }
}
