using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Platform;
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
            fixture.Main.AppearanceOptions.Should().HaveCount(9);
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
