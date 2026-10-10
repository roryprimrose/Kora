using System.ComponentModel;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;

using Kora;
using Kora.Application;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Controls;
using Kora.Core.Configuration;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.NativeUxFixture;

internal sealed class NativeUxFixtureWindow : Window, IDisposable
{
    private readonly NativeUxFixtureSession session;
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly NamedTextBlock status = new() { Text = "Initializing synthetic scratch state.", TextWrapping = TextWrapping.Wrap };
    private readonly HashSet<Task> pending = [];
    private readonly List<IDisposable> controllers = [];
    private readonly List<AsyncCommand> commands = [];
    private DetailWindowController? details;
    private NativeQuestionHost? questions;
    private DetailViewerState? retainedDetail;
    private bool ready;
    private bool stopping;
    private bool canClose;
    private bool failed;
    private long overflowSnapshotRevision;

    internal NativeUxFixtureWindow(NativeUxFixtureSession session, IClassicDesktopStyleApplicationLifetime desktop)
    {
        this.session = session;
        this.desktop = desktop;
        Title = "Kora SYNTHETIC native UX fixture - not the production host";
        Width = 740;
        Height = 760;
        AutomationProperties.SetName(this, "Kora synthetic native UX fixture launcher");
        AutomationProperties.SetName(status, "Native fixture status");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "SYNTHETIC FIXTURES ONLY",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Real Kora windows and styles; synthetic privacy/ownership, scratch stores and preferences. "
                + "No production instance, tray, microphone, speech, model, network, browser, clipboard or script execution. "
                + "Fixture passes cannot qualify actual R03 ownership/OS-transition timing.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(status);
        Content = new ScrollViewer { Content = content };
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => Dispose();

        AddAction(content, "Review local version (scratch native question)", async () =>
        {
            var question = controllers.OfType<QuestionWindowController>().Single();
            await question.ReviewVersionAsync(this);
        });
        AddAction(content, "Sessions (scratch IDs, guarded Done/resume)", () =>
        {
            controllers.OfType<SessionsWindowController>().Single().Open();
            return Task.CompletedTask;
        });
        AddAction(content, "Evidence (scratch records, read-only)", () =>
        {
            controllers.OfType<EvidenceWindowController>().Single().Open();
            return Task.CompletedTask;
        });
        AddAction(content, "Guide and owned immutable details", () =>
        {
            session.Main.ShowDocumentation();
            return Task.CompletedTask;
        });
        AddAction(content, "Skill packages (bundled inspection only)", () =>
        {
            controllers.OfType<SkillPackagesWindowController>().Single().Open();
            return Task.CompletedTask;
        });
        AddAction(content, "Appearance (scratch settings only)", () =>
        {
            session.Main.ShowSettings();
            return Task.CompletedTask;
        });
        AddAction(content, "Typed slash dropdown (synthetic catalogue only)", () =>
        {
            var window = new ResponseWindow(session.Main);
            window.ShowResponse();
            return Task.CompletedTask;
        });
        if (session.ListOverflow)
        {
            AddAction(content, "Inspect synthetic list overflow state", async () =>
            {
                var viewer = desktop.Windows.OfType<SessionsWindow>().SingleOrDefault()?.DataContext as SessionsViewModel;
                var snapshot = await session.Interactions.ReadQueueAsync(session.LifecycleTarget!.SessionId, session.Token);
                status.Text = System.Text.Json.JsonSerializer.Serialize(new
                {
                    listOverflow = true,
                    revision = checked(++overflowSnapshotRevision),
                    sessionId = session.LifecycleTarget!.SessionId.Value,
                    queueCount = snapshot.Entries.Count,
                    pendingTaskIds = snapshot.Entries.Select(item => item.Request.TaskId.Value).ToArray(),
                    eventIds = viewer?.LocalEvents.Select(item => item.Event.Id).ToArray() ?? [],
                    eventStatus = viewer?.LocalEventStatus,
                    traceId = session.OverflowTrace!.TraceId,
                    linkedTraceIds = session.OverflowLinks,
                    slashCommands = Enumerable.Range(1, 12).Select(index => $"/overflow-{index:00}").ToArray(),
                    realAudioOperations = 0,
                });
            });
        }
        if (session.SilentSpeech is not null)
        {
            AddAction(content, "Inspect silent synthetic caption state", () =>
            {
                status.Text = session.SyntheticCaptionStatus("inspect");
                return Task.CompletedTask;
            });
            AddAction(content, "Queue silent synthetic caption response", async () =>
            {
                await session.QueueSyntheticCaptionAsync();
                status.Text = session.SyntheticCaptionStatus("queued");
            });
            AddAction(content, "Observe silent synthetic caption playback", () =>
            {
                session.ObserveSyntheticCaption();
                status.Text = session.SyntheticCaptionStatus("observed");
                return Task.CompletedTask;
            });
            AddAction(content, "Complete silent synthetic caption normally", async () =>
            {
                await session.CompleteSyntheticCaptionAsync();
                status.Text = session.SyntheticCaptionStatus("completed");
            });
            AddAction(content, "Stop and retire silent synthetic caption", async () =>
            {
                await session.StopSyntheticCaptionAsync();
                status.Text = session.SyntheticCaptionStatus("stopped");
            });
            foreach (var placement in Enum.GetValues<SpeechCaptionPlacement>())
            {
                AddAction(content, $"Set silent caption placement {placement}", async () =>
                {
                    await session.SetSyntheticCaptionPlacementAsync(placement);
                    status.Text = session.SyntheticCaptionStatus("placement-saved");
                });
            }
        }
        AddAction(content, "Notify-only maintenance (network disabled)", () =>
        {
            var window = new MaintenanceWindow(session.CreateMaintenance());
            // Consent cannot be enabled in this run, even accidentally through UI Automation.
            var consent = window.GetLogicalDescendants().OfType<CheckBox>().Single();
            consent.IsEnabled = false;
            window.Show(this);
            window.Activate();
            return Task.CompletedTask;
        });
        AddAction(content, "Identify exact synthetic lifecycle target", () =>
        {
            var target = session.LifecycleTarget ?? throw new InvalidOperationException("The lifecycle fixture is unavailable.");
            status.Text = $"Lifecycle fixture: session {target.SessionId.Value:D}, generation {target.Generation.Value}, Active {target.IsActive}. "
                + "Select this exact ID in Sessions; this action does not refresh or change its displayed selection.";
            return Task.CompletedTask;
        });
        AddAction(content, "Advance exact synthetic lifecycle target without UI refresh", async () =>
        {
            var changed = await session.AdvanceLifecycleTargetAsync();
            status.Text = $"Advanced exact scratch session {changed.SessionId.Value:D} to generation {changed.Generation.Value}, Active {changed.IsActive}. "
                + "The existing Sessions selection was not refreshed; its old-generation action must be refused.";
        });
        AddAction(content, "Open synthetic stale-revision question", async () =>
        {
            var record = await session.CreateRevisionQuestionAsync();
            var host = questions ?? throw new InvalidOperationException("The native question fixture is unavailable.");
            var window = new QuestionWindow(host.CreateState(record), new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
            window.Show(this);
            window.Activate();
            status.Text = $"Opened exact synthetic question {record.Key.QuestionId.Value:D}, revision {record.Key.Revision.Value}. "
                + "This fixture tests refusal only; it never dispatches the version query.";
        });
        AddAction(content, "Advance exact synthetic question revision without retargeting", async () =>
        {
            var changed = await session.AdvanceRevisionQuestionAsync();
            status.Text = $"Advanced exact synthetic question {changed.Key.QuestionId.Value:D} to revision {changed.Key.Revision.Value}. "
                + "The original native window was not retargeted; old-revision review/save/submit must be refused.";
        });
        AddAction(content, "Seed retained and missing explicit evidence links", () =>
        {
            var links = session.SeedExplicitLinks();
            status.Text = $"Seeded linked trace {links.Linked.TraceId}, span {links.Linked.SpanId}; "
                + $"retained target trace {links.Source.TraceId}, span {links.Source.SpanId}; "
                + $"missing target trace {links.Missing.TraceId.ToHexString()}, span {links.Missing.SpanId.ToHexString()}. "
                + "Different host sessions; links confer no session, approval or execution authority.";
            return Task.CompletedTask;
        });
        AddAction(content, "Close immutable detail and refuse deferred stale render", () =>
        {
            var state = retainedDetail ?? throw new InvalidOperationException("Open an immutable detail fixture first.");
            if (state.Content is null) { throw new InvalidOperationException("The retained detail fixture is already closed."); }
            var generation = state.Generation;
            var semanticText = state.ActiveText;
            (details ?? throw new InvalidOperationException("The detail controller is unavailable.")).ClearForPrivacy();
            VerifyDeferredRenderRefused(state, generation, semanticText);
            status.Text = $"Refused deferred render for immutable item {state.Reference.ItemId.Value:D}, revision {state.Reference.Revision}, "
                + $"captured generation {generation}, closed generation {state.Generation}. Private detail content remains cleared; no copy was attempted.";
            return Task.CompletedTask;
        });
        AddAction(content, "Close synthetic privacy/ownership gate", () =>
        {
            session.Access.SetOpen(false);
            ClearPrivateWindows();
            status.Text = "Synthetic gate closed. Private fixture windows cleared; no real Windows transition was performed.";
            NotifyCommands();
            return Task.CompletedTask;
        });
        AddAction(content, "Reopen synthetic gate (no audio recovery)", async () =>
        {
            session.Access.SetOpen(true);
            await session.Main.RefreshCommand.ExecuteAsync();
            status.Text = "Synthetic gate reopened. Use a fresh explicit window/request; no old answer or audio is replayed.";
            NotifyCommands();
        }, requiresOpenGate: false);
        var stop = new Button { Content = "Stop fixture and clean up scratch state", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(stop, "Stop native fixture and clean up scratch state");
        stop.Command = new AsyncCommand(StopAsync, ReportFailure);
        content.Children.Add(stop);
        content.Children.Add(new TextBlock
        {
            Text = "Clipboard preview is excluded pending separate exact-fixture approval. "
                + "Narrator audio and Windows display changes require a separate operator decision; the fixture never starts or changes them. "
                + "All screenshots/automation must target this fixture PID, never the whole desktop.",
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private void AddAction(StackPanel panel, string text, Func<Task> action, bool requiresOpenGate = true)
    {
        var command = new AsyncCommand(() => TrackAsync(action), ReportFailure,
            () => ready && !stopping && (!requiresOpenGate || session.Access.Open));
        commands.Add(command);
        var button = new Button { Content = text, Command = command, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, text);
        panel.Children.Add(button);
    }

    private async Task TrackAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Add(completion.Task);
        try { await action(); }
        finally
        {
            pending.Remove(completion.Task);
            completion.SetResult();
        }
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            await TrackAsync(session.InitializeAsync);
            if (stopping) { return; }
            details = new(session.Documents, () => session.Access.Open && session.Main.CanRevealPrivatePresentation,
                new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance), new DeniedDetailClipboard(),
                (state, result, copy) =>
                {
                    retainedDetail = state;
                    return new DetailWindow(state, result, copy);
                },
                NullLogger<DetailWindowController>.Instance);
            controllers.Add(details);
            controllers.Add(new SettingsWindowController(session.Main, NullLogger<SettingsWindowController>.Instance));
            controllers.Add(new DocumentationWindowController(session.Documents, session.Main,
                NullLogger<DocumentationWindowController>.Instance, details));
            questions = new NativeQuestionHost(session.Interactions, TimeProvider.System, NullLogger<NativeQuestionViewModel>.Instance);
            questions.BindWorkspace(session.Sessions);
            controllers.Add(new QuestionWindowController(session.Main, questions, session.VersionQuery,
                new AssemblyApplicationInfo(), new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance),
                () => session.Access.Open, NullLogger<QuestionWindowController>.Instance));
            controllers.Add(new SkillPackagesWindowController(session.Main, () => session.Access.Open,
                NullLogger<SkillPackagesWindowController>.Instance, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance),
                session.SharedSkills, NullLogger<SharedSkillSourcesWindowController>.Instance));
            var evidence = new EvidenceWindowController(session.Main, session.Evidence, session.Access, NullLogger<EvidenceViewModel>.Instance);
            evidence.Bind();
            controllers.Add(evidence);
            var sessions = new SessionsWindowController(session.Main, session.Sessions, session.Evidence,
                session.Access, NullLogger<SessionsViewModel>.Instance, session.OverflowEvents);
            sessions.Bind();
            controllers.Add(sessions);
            if (session.SilentSpeech is not null) { controllers.Add(new SpeechCaptionWindowController(session.Main)); }
            session.Main.PrivacyClosureRequested += OnPrivacyClosure;
            session.Main.PropertyChanged += OnMainChanged;
            ready = true;
            ApplyTheme();
            status.Text = $"READY: synthetic native fixture PID {Environment.ProcessId}. Two idle sessions and one busy session; "
                + "typed query/audit/trace evidence in disposable scratch stores. No native acceptance result is claimed yet.";
            Console.WriteLine("READY: native fixture initialized.");
            NotifyCommands();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            await StopAsync();
        }
    }

    private void NotifyCommands()
    {
        foreach (var command in commands) { command.NotifyCanExecuteChanged(); }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => ClearPrivateWindows();

    private void ClearPrivateWindows()
    {
        details?.ClearForPrivacy();
        foreach (var window in desktop.Windows.ToArray())
        {
            if (!ReferenceEquals(window, this) && window is not SpeechCaptionWindow) { window.Close(); }
        }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(MainViewModel.ThemeMode), StringComparison.Ordinal)) { ApplyTheme(); }
    }

    private void ApplyTheme()
    {
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = session.Main.ThemeMode switch
            {
                ApplicationThemeMode.System => ThemeVariant.Default,
                ApplicationThemeMode.Light => ThemeVariant.Light,
                ApplicationThemeMode.Dark => ThemeVariant.Dark,
                _ => throw new InvalidDataException("Fixture theme is invalid."),
            };
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (canClose) { return; }
        args.Cancel = true;
        try { await StopAsync(); }
        catch (Exception exception)
        {
            ReportFailure(exception);
            canClose = true;
            desktop.Shutdown(1);
        }
    }

    private async Task StopAsync()
    {
        if (stopping) { return; }
        stopping = true;
        NotifyCommands();
        status.Text = "Stopping fixture: closing private windows and awaiting pending operations.";
        await session.StopSyntheticCaptionAsync();
        ClearPrivateWindows();
        await Task.WhenAll(pending.ToArray());
        await session.SharedAdmission.DisposeAsync();
        Dispose();
        canClose = true;
        desktop.Shutdown(failed ? 1 : 0);
    }

    public void Dispose()
    {
        if (ready)
        {
            session.Main.PrivacyClosureRequested -= OnPrivacyClosure;
            session.Main.PropertyChanged -= OnMainChanged;
            ready = false;
        }
        foreach (var controller in controllers) { controller.Dispose(); }
        controllers.Clear();
        details = null;
        questions = null;
        retainedDetail = null;
    }

    internal static void VerifyDeferredRenderRefused(DetailViewerState state, long generation, string semanticText)
    {
        if (state.Content is not null || state.Generation == generation
            || state.CompleteRender(generation, semanticText, "Synthetic deferred render must be refused.", fallback: false)
            || state.ActiveText.Length != 0)
        {
            throw new InvalidOperationException("Closed immutable detail accepted or retained a stale render.");
        }
    }

    private void ReportFailure(Exception exception)
    {
        failed = true;
        status.Text = "FAILED: native fixture operation did not complete. " + exception.Message;
        Console.Error.WriteLine(exception);
    }

    private sealed class DeniedDetailClipboard : IDetailClipboard
    {
        public Task WritePlainTextAsync(IDetailView view, string source) =>
            throw new InvalidOperationException("Native UX fixture cannot copy to the shared clipboard.");
    }
}
