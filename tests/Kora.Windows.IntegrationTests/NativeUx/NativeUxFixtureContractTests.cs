using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Application.Presentation;
using Kora.NativeUxFixture;

using static Kora.NativeUxFixture.FixtureBoundaries;

namespace Kora.Windows.IntegrationTests.NativeUx;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeUxFixtureContractTests
{
    [Theory]
    [InlineData()]
    [InlineData("--launch-native-fixtures")]
    [InlineData("--scratch-parent", "C:\\")]
    [InlineData("--launch-native-fixtures", "--scratch-parent", ".")]
    [InlineData("--launch-native-fixtures", "--scratch-parent", "\\\\unapproved.invalid\\share")]
    [InlineData("--launch-native-fixtures", "--scratch-parent", "\\\\?\\C:\\")]
    [InlineData("--launch-native-fixtures", "--scratch-parent", "C:\\", "--extra")]
    public void Native_launch_refuses_missing_or_unrecognized_opt_in_arguments(params string[] args) =>
        NativeUxFixtureHost.TryGetScratchParent(args, out _).Should().BeFalse();

    [Fact]
    public void Explicit_launch_requires_an_existing_absolute_scratch_parent()
    {
        NativeUxFixtureHost.TryGetScratchParent(["--launch-native-fixtures", "--scratch-parent", Path.GetTempPath()],
            out var parent).Should().BeTrue();
        parent.Should().Be(Path.GetFullPath(Path.GetTempPath()));
        NativeUxFixtureHost.TryGetScratchParent(["--launch-native-fixtures", "--scratch-parent",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))], out _).Should().BeFalse();
    }

    [Fact]
    public void Silent_caption_mode_requires_the_exact_additional_launch_flag()
    {
        NativeUxFixtureHost.TryGetScratchParent(
            ["--launch-native-fixtures", "--scratch-parent", Path.GetTempPath(), "--silent-caption-fixture"], out _).Should().BeTrue();
        NativeUxFixtureHost.TryGetScratchParent(
            ["--launch-native-fixtures", "--scratch-parent", Path.GetTempPath(), "--silent-caption-fixture", "--extra"], out _).Should().BeFalse();
        NativeUxFixtureHost.TryGetScratchParent(
            ["--silent-caption-fixture", "--scratch-parent", Path.GetTempPath()], out _).Should().BeFalse();
    }

    [Fact]
    public void List_overflow_mode_requires_its_exact_separate_flag()
    {
        NativeUxFixtureHost.TryGetScratchParent(
            ["--launch-native-fixtures", "--scratch-parent", Path.GetTempPath(), "--list-overflow-fixture"], out _).Should().BeTrue();
        NativeUxFixtureHost.TryGetScratchParent(
            ["--launch-native-fixtures", "--scratch-parent", Path.GetTempPath(), "--list-overflow-fixture", "--silent-caption-fixture"], out _).Should().BeFalse();
        var mixed = () => new NativeUxFixtureSession(Path.GetTempPath(), silentCaption: true, listOverflow: true);
        mixed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task List_overflow_keeps_real_queue_event_limits_missing_links_and_nonexecutable_catalogue()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), listOverflow: true);
            await fixture.InitializeAsync();
            fixture.SilentSpeech.Should().BeNull();
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            fixture.Main.CommandText = "/overflow";
            fixture.Main.ArtifactCommandOptions.Should().HaveCount(12);
            var target = fixture.LifecycleTarget!;
            var queue = await fixture.Interactions.ReadQueueAsync(target.SessionId, fixture.Token);
            queue.Entries.Should().ContainSingle().Which.State.Should().Be(SessionQueueState.Pending);
            var viewer = new SessionsViewModel(fixture.Sessions, fixture.Evidence, fixture.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsViewModel>.Instance, fixture.OverflowEvents);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == target.SessionId));
            viewer.LocalEvents.Should().HaveCount(LocalEventSnapshot.MaximumVisible);
            viewer.LocalEventStatus.Should().Contain("2 omitted");
            await viewer.RefreshWorkAsync();
            viewer.LocalEvents.Should().HaveCount(8);
            (await fixture.Interactions.ReadQueueAsync(target.SessionId, fixture.Token)).Should().BeEquivalentTo(queue);
            var evidence = new EvidenceViewModel(fixture.Evidence, () => fixture.Access.CanInspect,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EvidenceViewModel>.Instance)
            { TraceFilter = fixture.OverflowTrace!.TraceId };
            await evidence.SearchAsync();
            var linked = evidence.Records.Single(record => record.Reference.Source == Kora.Core.Diagnostics.EvidenceSource.Span);
            linked.RelatedSegments.Should().HaveCount(12).And.OnlyContain(segment => segment.Record == null);
            linked.RelatedSegments.Select(segment => segment.TraceId).Should().BeEquivalentTo(fixture.OverflowLinks);
            evidence.Close();
            viewer.Close();
        });
    }

    [Fact]
    public async Task Small_list_scrolling_preserves_event_selection_missing_link_authority_command_draft_and_pending_queue()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), listOverflow: true);
            await fixture.InitializeAsync();
            var target = fixture.LifecycleTarget!;
            var queue = await fixture.Interactions.ReadQueueAsync(target.SessionId, fixture.Token);
            var viewer = new SessionsViewModel(fixture.Sessions, fixture.Evidence, fixture.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsViewModel>.Instance, fixture.OverflowEvents);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == target.SessionId));
            var sessionWindow = new SessionsWindow(viewer);
            sessionWindow.Show();
            try
            {
                var events = viewer.LocalEvents.ToArray();
                events.Should().HaveCount(8);
                var selected = viewer.SelectedLocalEvent;
                var status = viewer.LocalEventStatus;
                ScrollToEnd(sessionWindow.FindControl<ListBox>("EventRecords")!);
                viewer.LocalEvents.Should().Equal(events);
                viewer.SelectedLocalEvent.Should().BeSameAs(selected);
                viewer.LocalEventStatus.Should().Be(status).And.Contain("2 omitted");
            }
            finally { sessionWindow.Close(); }

            var evidence = new EvidenceViewModel(fixture.Evidence, () => fixture.Access.CanInspect,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EvidenceViewModel>.Instance)
            { TraceFilter = fixture.OverflowTrace!.TraceId };
            await evidence.SearchAsync();
            evidence.Select(evidence.Records.Single(record => record.Reference.Source == EvidenceSource.Span));
            var evidenceWindow = new EvidenceWindow(evidence);
            evidenceWindow.Show();
            try
            {
                var segments = evidence.Segments;
                var result = evidence.ResultText;
                segments.Should().HaveCount(12).And.OnlyContain(segment => segment.Record == null);
                var records = evidence.Records;
                var selected = evidenceWindow.FindControl<ListBox>("Segments")!.SelectedItem;
                ScrollToEnd(evidenceWindow.FindControl<ListBox>("Segments")!);
                evidence.Segments.Should().BeSameAs(segments);
                evidence.Records.Should().BeSameAs(records);
                evidence.ResultText.Should().Be(result);
                evidenceWindow.FindControl<ListBox>("Segments")!.SelectedItem.Should().BeSameAs(selected);
            }
            finally { evidenceWindow.Close(); }

            fixture.Main.CommandText = "/overflow";
            var responseWindow = new ResponseWindow(fixture.Main);
            responseWindow.Show();
            try
            {
                var options = fixture.Main.ArtifactCommandOptions.ToArray();
                options.Should().HaveCount(12);
                var commands = responseWindow.FindControl<ListBox>("ArtifactCommandList")!;
                var selected = commands.SelectedItem;
                ScrollToEnd(commands);
                fixture.Main.ArtifactCommandOptions.Should().Equal(options);
                fixture.Main.CommandText.Should().Be("/overflow");
                commands.SelectedItem.Should().BeSameAs(selected);
            }
            finally { responseWindow.Close(); }
            (await fixture.Interactions.ReadQueueAsync(target.SessionId, fixture.Token)).Should().BeEquivalentTo(queue);
        });
    }

    private static void ScrollToEnd(ListBox list)
    {
        list.Height = 90;
        list.UpdateLayout();
        var provider = ControlAutomationPeer.CreatePeerForElement(list)!
            .GetProvider<Avalonia.Automation.Provider.IScrollProvider>()!;
        provider.VerticallyScrollable.Should().BeTrue();
        provider.VerticalViewSize.Should().BeGreaterThan(0).And.BeLessThan(100);
        provider.SetScrollPercent(-1, 100);
        list.UpdateLayout();
        provider.VerticalScrollPercent.Should().BeApproximately(100, 0.01);
    }

    [Fact]
    public async Task Silent_caption_models_exact_playback_identity_without_an_audio_implementation()
    {
        await using var speech = new SilentCaptionSpeech();
        var unarmed = () => speech.SpeakAsync("Synthetic text.", speech.GetDefaultVoice(), speech.GetDefaultOutputDevice(),
            Guid.NewGuid(), TestContext.Current.CancellationToken);
        await unarmed.Should().ThrowAsync<InvalidOperationException>();
        var started = speech.Arm();
        var id = Guid.NewGuid();
        var playback = speech.SpeakAsync("Synthetic text.", speech.GetDefaultVoice(), speech.GetDefaultOutputDevice(),
            id, TestContext.Current.CancellationToken);
        await started;
        playback.IsCompleted.Should().BeFalse();
        speech.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
        speech.ObservePlayback();
        speech.PlaybackFrame.PlaybackId.Should().Be(id);
        speech.ExactText.Should().Be("Synthetic text.");
        var duplicate = () => speech.SpeakAsync("Other text.", speech.GetDefaultVoice(), speech.GetDefaultOutputDevice(), id);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        speech.Complete();
        await playback;
        speech.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
        speech.AdmittedRequests.Should().Be(1);
        var install = () => speech.InstallProviderAsync("any", new Progress<SpeechProviderInstallProgress>());
        await install.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Silent_caption_uses_the_ordinary_response_path_and_pin_completion_stop_retirement()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
            await fixture.QueueSyntheticCaptionAsync();
            fixture.Main.IsSpeechCaptionVisible.Should().BeFalse("queued text is not playback");
            fixture.ObserveSyntheticCaption();
            fixture.Main.IsSpeechCaptionVisible.Should().BeTrue();
            fixture.Main.SpeechCaptionText.Should().Be(fixture.SilentSpeech!.ExactText);
            await fixture.Main.ToggleSpeechCaptionPinCommand.ExecuteAsync();
            fixture.Main.IsSpeechCaptionPinned.Should().BeTrue();
            await fixture.CompleteSyntheticCaptionAsync();
            fixture.Main.IsPreviousSpeechCaption.Should().BeTrue();
            fixture.Main.IsSpeechCaptionVisible.Should().BeTrue();
            await fixture.StopSyntheticCaptionAsync();
            fixture.Main.SpeechCaptionText.Should().BeNull();
            fixture.Main.IsSpeechCaptionPinned.Should().BeFalse();
            fixture.SilentSpeech.AdmittedRequests.Should().Be(1);
        });
    }

    [Fact]
    public async Task Silent_caption_placement_uses_admitted_preferences_and_privacy_never_replays()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), silentCaption: true);
            await fixture.InitializeAsync();
            using var first = System.Text.Json.JsonDocument.Parse(fixture.SyntheticCaptionStatus("inspect"));
            using var second = System.Text.Json.JsonDocument.Parse(fixture.SyntheticCaptionStatus("inspect"));
            second.RootElement.GetProperty("snapshotRevision").GetInt64().Should()
                .BeGreaterThan(first.RootElement.GetProperty("snapshotRevision").GetInt64());
            second.RootElement.GetProperty("completedOperation").GetString().Should().Be("inspect");
            foreach (var placement in Enum.GetValues<SpeechCaptionPlacement>())
            {
                await fixture.SetSyntheticCaptionPlacementAsync(placement);
                fixture.Main.SpeechCaptionPlacement.Should().Be(placement);
            }
            await fixture.QueueSyntheticCaptionAsync();
            fixture.ObserveSyntheticCaption();
            fixture.Access.SetOpen(false);
            await fixture.StopSyntheticCaptionAsync();
            fixture.Main.SpeechCaptionText.Should().BeNull();
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
            fixture.Access.SetOpen(true);
            fixture.Main.RefreshSpeechPlaybackFrame();
            fixture.Main.IsSpeechCaptionVisible.Should().BeFalse();
            fixture.SilentSpeech!.AdmittedRequests.Should().Be(1);
        });
    }

    [Fact]
    public async Task Production_presentation_resources_load_without_production_composition()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var provider = App.Services;
            var current = Avalonia.Application.Current;
            var application = new Avalonia.Application();
            NativeUxFixtureHost.LoadPresentation(application);
            application.Styles.Should().NotBeEmpty();
            application.TryGetResource("KoraWindowBackgroundBrush", Avalonia.Styling.ThemeVariant.Light, out var brush).Should().BeTrue();
            brush.Should().NotBeNull();
            App.Services.Should().BeSameAs(provider);
            Avalonia.Application.Current.Should().BeSameAs(current);
        });
    }

    [Fact]
    public async Task Clipboard_events_are_blocked_and_guards_unregister_on_disposal()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var box = new TextBox();
            foreach (var routedEvent in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
            {
                using (var guard = new NativeUxClipboardGuard())
                {
                    var blocked = new RoutedEventArgs(routedEvent);
                    box.RaiseEvent(blocked);
                    blocked.Handled.Should().BeTrue();
                }
                var released = new RoutedEventArgs(routedEvent);
                box.RaiseEvent(released);
                released.Handled.Should().BeFalse();
            }
        });
    }

    [Fact]
    public async Task Scratch_services_initialize_real_idle_and_busy_records_without_opening_any_native_window()
    {
        string? root = null;
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            root = fixture.LocalRoot;
            fixture.LocalRoot.Should().NotBe(new ApplicationDataPaths().LocalRoot);
            fixture.LocalRoot.Should().StartWith(Path.Combine(Path.GetTempPath(), "kora-native-ux-"));
            await fixture.InitializeAsync();
            var sessions = await fixture.Interactions.ReadSessionsAsync(null, 25, CancellationToken.None);
            sessions.Records.Should().HaveCount(3);
            (await fixture.Tasks.ReadIncompleteAsync(10, CancellationToken.None)).Should().ContainSingle()
                .Which.State.Should().Be(HostTaskState.IntentRecorded);
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            fixture.Main.LocalModelsEnabled.Should().BeFalse();
            fixture.Main.HostedModelsEnabled.Should().BeFalse();
            fixture.Main.CanRevealPrivatePresentation.Should().BeTrue("only synthetic private presentation is admitted");
            fixture.Main.AppearanceOptions.Should().HaveCount(10);
            fixture.Main.ClipboardPreview.Should().BeNull();
            var maintenance = fixture.CreateMaintenance();
            maintenance.NetworkEnabled.Should().BeFalse();
            maintenance.NextCheck.Should().BeNull();
            fixture.Access.SetOpen(false);
            fixture.Access.CanInspect.Should().BeFalse();
            fixture.Access.CanControl.Should().BeFalse();
            fixture.Access.Current.SessionState.Should().Be(WindowsSessionState.Unknown);
        });
        root.Should().NotBeNull();
        Directory.Exists(root).Should().BeFalse("cleanup must remove only the newly created fixture child");
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var afterCleanup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        afterCleanup.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task Effect_and_external_boundaries_refuse_operations_instead_of_reporting_synthetic_success()
    {
        var access = new FixtureAccess();
        var lockWindows = () => access.LockCurrentSession();
        lockWindows.Should().Throw<InvalidOperationException>();
        var restart = () => new FixtureExecution().RestartCurrentApplication();
        restart.Should().Throw<InvalidOperationException>();
        var readClipboard = () => new FixtureClipboardReader().ReadAsync(CancellationToken.None);
        await readClipboard.Should().ThrowAsync<InvalidOperationException>();
        var install = () => new FixtureExecution().InstallAsync(CancellationToken.None);
        await install.Should().ThrowAsync<InvalidOperationException>();
        access.Dispose();
        access.CanInspect.Should().BeFalse();
        var reopenDisposed = () => access.SetOpen(true);
        reopenDisposed.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Valid_fixture_lifecycle_advance_leaves_displayed_generation_stale_and_refuses_done_or_resume(bool initiallyActive)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            if (!initiallyActive) { await fixture.AdvanceLifecycleTargetAsync(); }
            var displayed = fixture.LifecycleTarget!;
            var state = new SessionsViewModel(fixture.Sessions, fixture.Evidence, fixture.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsViewModel>.Instance);
            try
            {
                await state.RefreshAsync();
                await state.SelectAsync(state.Sessions.Single(entry => entry.Authority.SessionId == displayed.SessionId));
                var advanced = await fixture.AdvanceLifecycleTargetAsync();
                advanced.Generation.Value.Should().Be(displayed.Generation.Value + 1);
                await state.ChangeLifecycleAsync(!initiallyActive);
                state.Status.Should().StartWith("Sessions unavailable/denied:");
                state.Detail.Should().BeEmpty();
                state.CanDone.Should().BeFalse();
                state.CanResume.Should().BeFalse();
                var stored = (await fixture.Interactions.ReadSessionsAsync(null, 25, CancellationToken.None))
                    .Records.Single(item => item.SessionId == displayed.SessionId);
                stored.Should().Be(advanced);
            }
            finally { state.Close(); }
        });
    }

    [Fact]
    public async Task Valid_question_revision_advance_refuses_original_native_answer_without_retargeting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var original = await fixture.CreateRevisionQuestionAsync();
            var host = new NativeQuestionHost(fixture.Interactions, TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<NativeQuestionViewModel>.Instance);
            host.BindGate(() => fixture.Access.Open);
            var state = host.CreateState(original);
            state.Edit(new(["show"], null));
            var advanced = await fixture.AdvanceRevisionQuestionAsync();
            advanced.Key.QuestionId.Should().Be(original.Key.QuestionId);
            advanced.Key.Request.Should().Be(original.Key.Request);
            advanced.Key.Revision.Value.Should().Be(original.Key.Revision.Value + 1);
            await state.SubmitAsync();
            (await state.Completion).Outcome.Should().Be(HostInteractionOutcome.Conflict);
            state.Key.Should().Be(original.Key);
            state.Status.Should().Be("question-conflict");
            state.CanSubmit.Should().BeFalse();
            var stored = (await fixture.Interactions.ReadQuestionPageAsync(original.Key.Request.SessionId, null, 25, CancellationToken.None))
                .Records.Single();
            stored.Should().BeEquivalentTo(advanced);
            stored.Status.Should().Be(QuestionStatus.Pending);
            stored.Draft.Should().BeNull();
            state.Close("Fixture complete.");
        });
    }

    [Fact]
    public async Task Native_work_peer_exposes_new_pending_rows_after_retained_terminal_rows_without_window_recreation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var viewer = new SessionsViewModel(fixture.Sessions, fixture.Evidence, fixture.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsViewModel>.Instance);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(entry =>
                entry.Authority.SessionId == fixture.LifecycleTarget!.SessionId));
            await viewer.EnqueueVersionAsync();
            viewer.SelectWorkRecord(viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending));
            await viewer.RemoveQueueEntryAsync();
            await viewer.EnqueueVersionAsync();
            await viewer.DispatchQueueAsync();
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var records = window.FindControl<ListBox>("WorkRecords")!;
                records.UpdateLayout();
                var peer = ControlAutomationPeer.CreatePeerForElement(records)!;
                _ = Descendants(peer).ToArray();
                await viewer.EnqueueVersionAsync();
                records.UpdateLayout();
                var pending = viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending);
                records.ContainerFromItem(pending).Should().NotBeNull();
                Descendants(peer).Should().Contain(child => child.GetName().Contains(pending.TaskId.ToString("D"), StringComparison.Ordinal));
            }

            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Bounded_work_list_scroll_provider_realizes_new_pending_work_and_preserves_passive_focus()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var viewer = new SessionsViewModel(fixture.Sessions, fixture.Evidence, fixture.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsViewModel>.Instance);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(entry =>
                entry.Authority.SessionId == fixture.LifecycleTarget!.SessionId));
            await viewer.EnqueueVersionAsync();
            viewer.SelectWorkRecord(viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending));
            await viewer.RemoveQueueEntryAsync();
            await viewer.EnqueueVersionAsync();
            await viewer.DispatchQueueAsync();
            var window = new SessionsWindow(viewer);
            var records = window.FindControl<ListBox>("WorkRecords")!;
            records.Height = 110;
            var peer = ControlAutomationPeer.CreatePeerForElement(records)!;
            var discovered = await Task.Run(() => peer.GetProvider<Avalonia.Automation.Provider.IScrollProvider>());
            discovered.Should().NotBeNull("native provider discovery occurs off-thread and must not read the template");
            window.Show();
            try
            {
                records.UpdateLayout();
                await viewer.EnqueueVersionAsync();
                records.UpdateLayout();
                var pending = viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending);
                var provider = peer.GetProvider<Avalonia.Automation.Provider.IScrollProvider>()!;
                provider.Should().NotBeNull();
                provider.VerticallyScrollable.Should().BeTrue();
                provider.VerticalViewSize.Should().BeGreaterThan(0).And.BeLessThan(100);
                provider.SetScrollPercent(-1, 100);
                records.UpdateLayout();
                provider.VerticalScrollPercent.Should().BeApproximately(100, 0.01);
                Control? container = null;
                foreach (var percent in new double[] { 0, 25, 50, 75, 100 })
                {
                    provider.SetScrollPercent(-1, percent);
                    records.UpdateLayout();
                    container = records.ContainerFromItem(pending);
                    if (container is not null) { break; }
                }
                container.Should().NotBeNull("native scrolling must realize the previously off-viewport pending row");
                container!.Focus().Should().BeTrue();
                viewer.SelectWorkRecord(pending);
                var collection = viewer.WorkRecords;
                var offset = provider.VerticalScrollPercent;
                await viewer.RefreshWorkAsync();
                records.UpdateLayout();
                viewer.WorkRecords.Should().BeSameAs(collection);
                viewer.SelectedWorkRecord.Should().BeSameAs(pending);
                records.ContainerFromItem(pending).Should().BeSameAs(container);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(container);
                provider.VerticalScrollPercent.Should().BeApproximately(offset, 0.01);
                pending.Queue!.Entry.State.Should().Be(SessionQueueState.Pending);
                provider.SetScrollPercent(-1, 0);
                records.UpdateLayout();
                provider.VerticalScrollPercent.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer parent)
    {
        foreach (var child in parent.GetChildren() ?? [])
        {
            yield return child;
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }

    [Fact]
    public async Task Scratch_session_and_evidence_lists_expose_scrollable_overflow_without_mutating_selection_or_sources()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var viewer = new SessionsViewModel(fixture.Sessions, fixture.Evidence, fixture.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsViewModel>.Instance);
            for (var index = 0; index < 9; index++)
            {
                viewer.NameDraft = $"Synthetic scroll test {index}";
                await viewer.CreateAsync();
            }
            await viewer.RefreshAsync();
            var sessionWindow = new SessionsWindow(viewer);
            sessionWindow.Show();
            try
            {
                var records = sessionWindow.FindControl<ListBox>("Records")!;
                records.Height = 90;
                records.UpdateLayout();
                var peer = ControlAutomationPeer.CreatePeerForElement(records)!;
                var provider = peer.GetProvider<Avalonia.Automation.Provider.IScrollProvider>()!;
                provider.VerticallyScrollable.Should().BeTrue();
                var selected = viewer.SelectedSessionRecord;
                var sessions = viewer.Sessions;
                provider.SetScrollPercent(-1, 100);
                records.UpdateLayout();
                provider.VerticalScrollPercent.Should().BeApproximately(100, 0.01);
                viewer.SelectedSessionRecord.Should().BeSameAs(selected);
                viewer.Sessions.Should().Equal(sessions);
            }
            finally { sessionWindow.Close(); }

            var evidence = new EvidenceViewModel(fixture.Evidence, () => fixture.Access.CanInspect,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EvidenceViewModel>.Instance);
            await evidence.SearchAsync();
            var evidenceWindow = new EvidenceWindow(evidence);
            evidenceWindow.Show();
            try
            {
                var records = evidenceWindow.FindControl<ListBox>("Records")!;
                records.Height = 90;
                records.UpdateLayout();
                var provider = ControlAutomationPeer.CreatePeerForElement(records)!
                    .GetProvider<Avalonia.Automation.Provider.IScrollProvider>()!;
                provider.VerticallyScrollable.Should().BeTrue();
                var page = evidence.Records;
                var details = evidence.ResultText;
                provider.SetScrollPercent(-1, 100);
                records.UpdateLayout();
                provider.VerticalScrollPercent.Should().BeApproximately(100, 0.01);
                evidence.Records.Should().BeSameAs(page);
                evidence.ResultText.Should().Be(details);
                evidence.Segments.Should().BeEmpty("scrolling cannot select or navigate a trace segment");
            }
            finally { evidenceWindow.Close(); }
        });
    }

    [Fact]
    public async Task Fixture_queue_runs_only_the_admitted_local_version_and_refuses_dispatch_after_privacy_closure()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var viewer = new SessionsViewModel(fixture.Sessions, fixture.Evidence, fixture.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsViewModel>.Instance);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(entry =>
                entry.Authority.SessionId == fixture.LifecycleTarget!.SessionId));
            await viewer.EnqueueVersionAsync();
            viewer.QueueRecords.Should().ContainSingle().Which.State.Should().Be(SessionQueueState.Pending);
            await viewer.DispatchQueueAsync();
            viewer.QueueRecords.Should().BeEmpty();
            viewer.Detail.Should().Contain("\"queueDispatch\":").And.Contain("\"Succeeded\"");
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            fixture.Main.LocalModelsEnabled.Should().BeFalse();
            fixture.Main.HostedModelsEnabled.Should().BeFalse();
            await viewer.EnqueueVersionAsync();
            var pending = viewer.QueueRecords.Single();
            fixture.Access.SetOpen(false);
            await viewer.DispatchQueueAsync();
            viewer.Status.Should().Contain("Sessions unavailable/denied:");
            (await fixture.Interactions.ReadQueueEntryAsync(pending.Request.SessionId, pending.Request.TaskId,
                CancellationToken.None))!.State.Should().Be(SessionQueueState.Pending);
            viewer.Close();
        });
    }

    [Fact]
    public async Task Explicit_fixture_links_use_real_completed_spans_without_cross_session_authority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var links = fixture.SeedExplicitLinks();
            links.LinkedRequest.SessionId.Should().NotBe(links.SourceRequest.SessionId);
            using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Evidence);
            var page = await fixture.Evidence.QueryAsync(new() { TraceId = links.Linked.TraceId }, null, CancellationToken.None);
            var span = page.Records.Single(record => record.Reference.Source == EvidenceSource.Span);
            span.Host.Should().Be(links.LinkedRequest);
            span.RelatedSegments.Should().HaveCount(2);
            var retained = span.RelatedSegments.Single(segment => string.Equals(segment.TraceId, links.Source.TraceId, StringComparison.Ordinal));
            retained.Status.Should().Be(EvidenceSegmentStatus.Present);
            retained.Record.Should().NotBeNull();
            var target = await fixture.Evidence.QueryAsync(new() { Record = retained.Record }, null, CancellationToken.None);
            target.Records.Should().ContainSingle().Which.Host.Should().Be(links.SourceRequest);
            span.RelatedSegments.Single(segment => string.Equals(segment.TraceId, links.Missing.TraceId.ToHexString(), StringComparison.Ordinal))
                .Status.Should().Be(EvidenceSegmentStatus.MissingOrRemoved);
            page.Records.Count(record => record.Reference.Source == EvidenceSource.Link).Should().Be(2);
            var confined = await fixture.Evidence.QueryAsync(new()
            {
                TraceId = links.Linked.TraceId, SessionId = links.LinkedRequest.SessionId,
            }, null, CancellationToken.None);
            confined.Records.Single(record => record.Reference.Source == EvidenceSource.Span)
                .RelatedSegments.Single(segment => string.Equals(segment.TraceId, links.Source.TraceId, StringComparison.Ordinal)).Record.Should().BeNull();
            (await fixture.Interactions.ReadSessionsAsync(null, 25, CancellationToken.None)).Records.Should().HaveCount(3);
        });
    }

    [Fact]
    public async Task Validation_hooks_fail_closed_after_synthetic_privacy_closure()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            fixture.Access.SetOpen(false);
            await fixture.Awaiting(value => value.AdvanceLifecycleTargetAsync()).Should().ThrowAsync<InvalidOperationException>();
            await fixture.Awaiting(value => value.CreateRevisionQuestionAsync()).Should().ThrowAsync<InvalidOperationException>();
            await fixture.Awaiting(value => value.AdvanceRevisionQuestionAsync()).Should().ThrowAsync<InvalidOperationException>();
            fixture.Invoking(value => value.SeedExplicitLinks()).Should().Throw<InvalidOperationException>();
        });
    }

    [Fact]
    public void Deferred_immutable_detail_hook_refuses_closed_generation_and_does_not_restore_content()
    {
        var state = new DetailViewerState(new(new(new(Guid.NewGuid()), 1),
            Kora.Core.Presentation.DetailContentKind.PlainText, Kora.Core.Presentation.DetailContentOrigin.EmbeddedDocument,
            Kora.Core.Presentation.DetailSensitivity.Public, "Synthetic immutable fixture", "Fixture, not authority.", "Synthetic text"));
        var generation = state.Generation;
        state.CompleteRender(generation, "Synthetic text", "Rendered.", fallback: false).Should().BeTrue();
        state.Close();
        NativeUxFixtureWindow.VerifyDeferredRenderRefused(state, generation, "Synthetic text");
        state.ActiveText.Should().BeEmpty();
        state.Content.Should().BeNull();
        state.Invoking(value => NativeUxFixtureWindow.VerifyDeferredRenderRefused(value, value.Generation, "Synthetic text"))
            .Should().Throw<InvalidOperationException>();
    }
}
