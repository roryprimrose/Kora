using System.Diagnostics;
using System.Security.Principal;

using Kora;
using Kora.Application;
using Kora.Application.Auditing;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Diagnostics;
using Kora.Application.Documentation;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.Maintenance;
using Kora.Application.Skills;
using Kora.Application.Tools;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Maintenance;
using Kora.Core.Skills;
using Kora.Core.Voice;
using Kora.Tools.Clipboard;
using Kora.Tools.Runtime;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using static Kora.NativeUxFixture.FixtureBoundaries;
using NativeFixturePhase = Kora.NativeUxFixture.NativeFixturePhaseScope.PhaseKind;
using NativeFixturePhaseOutcome = Kora.NativeUxFixture.NativeFixturePhaseScope.Outcome;

namespace Kora.NativeUxFixture;

internal sealed partial class NativeUxFixtureSession : IApplicationDataPaths, IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<MaintenanceViewModel> maintenance = [];
    private bool disposed;
    private bool initialized;
    private MainViewModel? main;
    private AssistantNameConfigurationService? assistantNameConfiguration;
    private SessionQueueService? queue;
    internal SessionQueueService Queue => queue ?? throw new InvalidOperationException("The fixture queue is not initialized.");
    private SessionWorkspaceService? sessions;
    private readonly EvidenceLoggerProvider? evidenceProvider;
    private readonly Serilog.Core.Logger? dailyEvidenceLogger;
    private AudioControlAdmission? captionAdmission;

    internal NativeUxFixtureSession(string scratchParent, bool silentCaption = false, bool listOverflow = false,
        bool exactGrants = false, bool allWork = false, bool bufferedEvidence = false,
        ISqliteTransactionCheckpoint? evidenceCheckpoint = null,
        IHostInteractionTransactionCheckpoint? interactionCheckpoint = null, bool syntheticVoice = false,
        TimeProvider? timeProvider = null)
    {
        Time = timeProvider ?? TimeProvider.System;
        CapturePhaseObserver();
        using var ownedStorage = BeginPhase(NativeFixturePhase.OwnedStorage);
        if ((silentCaption ? 1 : 0) + (listOverflow ? 1 : 0) + (exactGrants ? 1 : 0) + (allWork ? 1 : 0) + (syntheticVoice ? 1 : 0) > 1)
        {
            throw new ArgumentException("Synthetic caption, list-overflow, exact-grants, all-work and voice fixture modes are separate.");
        }
        if (!NativeUxFixtureHost.IsLocalScratchParent(scratchParent))
        {
            throw new ArgumentException("An existing absolute scratch parent on a fixed local drive, not a reparse point, is required.", nameof(scratchParent));
        }
        LocalRoot = Path.Combine(Path.GetFullPath(scratchParent), "kora-native-ux-" + Guid.NewGuid().ToString("N"));
        Access = new(syntheticVoice);
        Voice = new(syntheticVoice);
        SilentSpeech = silentCaption ? new SilentCaptionSpeech() : null;
        ListOverflow = listOverflow;
        ExactGrants = exactGrants;
        AllWork = allWork;
        if (Directory.Exists(LocalRoot) || File.Exists(LocalRoot))
        {
            throw new IOException("The fresh native UX scratch path already exists.");
        }
        using var identity = WindowsIdentity.GetCurrent();
        RestrictedStorageDirectory.CreateRestrictedDirectory(LocalRoot,
            identity.User ?? throw new InvalidOperationException("A loaded Windows user profile is required."));
        try
        {
            var sink = new WindowsSqliteEvidenceSink(this, null, null, evidenceCheckpoint);
            sink.Initialize();
            if (bufferedEvidence)
            {
                var health = new FileEvidenceHealth();
                dailyEvidenceLogger = DailyFileLoggerFactory.Create(this, health);
                var copy = new FileEvidenceSink(dailyEvidenceLogger, health);
                evidenceProvider = new([copy, sink], copy, bufferDiagnostics: true, requiredOrdinaryCopy: copy);
            }
            else
            {
                evidenceProvider = new([sink], new FixtureEvidenceGaps());
            }
            Loggers = LoggerFactory.Create(builder => builder.AddProvider(evidenceProvider));
            Audit = new LoggerSecurityAuditLog(Loggers.CreateLogger<LoggerSecurityAuditLog>());
            Tasks = new(this);
            Interactions = new(this, Tasks, Time, interactionCheckpoint);
            VersionQuery = new(new(Tasks), Audit, Loggers.CreateLogger<DurableVersionQuery>());
            Evidence = new(new WindowsEvidenceReader(new WindowsSqliteEvidenceReader(sink),
                new WindowsDailyEvidenceReader(this), Interactions), Access, Time, NullLogger<DurableEvidenceQuery>.Instance);
            SharedAdmission = new(Interactions, Interactions, new(Tasks));
            SharedSkills = new(new(this), new DeniedSharedSkillReader(), SharedAdmission, Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            ownedStorage.Complete();
            ownedStorage.Dispose();
            VerifyPhaseEvidence();
        }
        catch (Exception exception)
        {
            Loggers?.Dispose();
            evidenceProvider?.Dispose();
            dailyEvidenceLogger?.Dispose();
            cancellation.Dispose();
            Directory.Delete(LocalRoot, recursive: true);
            if (exception is OperationCanceledException) { ownedStorage.Cancel(); }
            throw;
        }
    }

    partial void CapturePhaseObserver();
    partial void ObservePhase(NativeFixturePhaseScope phase, NativeFixturePhaseOutcome outcome);
    partial void VerifyPhaseEvidence();

    private void RecordPhase(NativeFixturePhaseScope phase, NativeFixturePhaseOutcome outcome) => ObservePhase(phase, outcome);
    private NativeFixturePhaseScope BeginPhase(NativeFixturePhase phase) => new(phase, RecordPhase);

    public string LocalRoot { get; }
    internal TimeProvider Time { get; }
    public string RoamingRoot => Path.Combine(LocalRoot, "Roaming");
    internal FixtureAccess Access { get; }
    internal FixtureVoice Voice { get; }
    internal CancellationToken Token => cancellation.Token;
    internal ILoggerFactory Loggers { get; }
    internal LoggerSecurityAuditLog Audit { get; }
    internal WindowsSqliteHostTaskStore Tasks { get; }
    internal WindowsSqliteHostInteractionStore Interactions { get; }
    internal DurableVersionQuery VersionQuery { get; }
    internal DurableEvidenceQuery Evidence { get; }
    internal SessionWorkspaceService Sessions => sessions
        ?? throw new InvalidOperationException("The native fixture workspace has not initialized.");
    internal SharedSkillAdmission SharedAdmission { get; }
    internal SharedSkillDiscoveryService SharedSkills { get; }
    internal SilentCaptionSpeech? SilentSpeech { get; }

    private sealed class DeniedSharedSkillReader : ISharedSkillSourceReader
    {
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The native UX fixture does not admit real profile sources.");
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The native UX fixture does not read real profile packages.");
    }
    internal WorkSessionAuthorization? LifecycleTarget { get; private set; }
    internal HostQuestionRecord? RevisionQuestion { get; private set; }
    internal MainViewModel Main => main ?? throw new InvalidOperationException("The native fixture has not initialized its presentation state.");
    internal ArtifactCatalogue ArtifactCatalogue { get; private set; } = null!;
    internal IUserDocumentationProvider Documents { get; } = new EmbeddedUserDocumentationProvider();

    internal Task InitializeAsync() => InitializeAsync(null);

    internal async Task InitializeAsync(Kora.Tools.Network.WebPageGet? webPageGet,
        Kora.Application.Network.WebSummaryWorkflow? webSummary = null,
        Kora.Core.Communication.ICallStateService? callState = null,
        IUiDispatcher? dispatcher = null, ILocalModelReasoner? modelReasoner = null, ISetupDependencyProbe? modelProbe = null,
        Kora.Application.Dependencies.SpeechComponentSetupService? speechComponentSetup = null,
        OrdinaryTurnDeadlineConfigurationService? ordinaryDeadlineConfiguration = null,
        ConversationExecutionConfigurationService? conversationExecution = null,
        SpeechSetupComparison? speechSetupComparison = null,
        LogonStartupConfigurationService? logonStartupConfiguration = null,
        StartupPresentationConfigurationService? startupPresentationConfiguration = null,
        IQuestionVoiceRecognitionService? voiceRecognition = null, SpeechInputConfigurationService? speechInputConfiguration = null)
    {
        using var setup = BeginPhase(NativeFixturePhase.Setup);
        await setup.RunAsync(() => InitializeCoreAsync(webPageGet, webSummary, callState, dispatcher, modelReasoner,
            modelProbe, speechComponentSetup, ordinaryDeadlineConfiguration, conversationExecution,
            speechSetupComparison, logonStartupConfiguration, startupPresentationConfiguration,
            voiceRecognition, speechInputConfiguration));
    }

    private async Task InitializeCoreAsync(Kora.Tools.Network.WebPageGet? webPageGet,
        Kora.Application.Network.WebSummaryWorkflow? webSummary,
        Kora.Core.Communication.ICallStateService? callState,
        IUiDispatcher? dispatcher, ILocalModelReasoner? modelReasoner, ISetupDependencyProbe? modelProbe,
        Kora.Application.Dependencies.SpeechComponentSetupService? speechComponentSetup,
        OrdinaryTurnDeadlineConfigurationService? ordinaryDeadlineConfiguration,
        ConversationExecutionConfigurationService? conversationExecution,
        SpeechSetupComparison? speechSetupComparison,
        LogonStartupConfigurationService? logonStartupConfiguration,
        StartupPresentationConfigurationService? startupPresentationConfiguration,
        IQuestionVoiceRecognitionService? voiceRecognition, SpeechInputConfigurationService? speechInputConfiguration)
    {
        if (initialized) { throw new InvalidOperationException("A fixture session may be initialized only once."); }
        using (var context = BeginPhase(NativeFixturePhase.Context))
        {
            await context.RunAsync(async () =>
            {
                await Tasks.InitializeAsync(Token);
                await Interactions.InitializeAsync(Token);
            });
        }
        using var startup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Desktop, HostOperation.Startup);
        var consent = new LocalVoiceConsentPreferences(this, NullLogger<LocalVoiceConsentPreferences>.Instance);
        consent.Save(false);
        var models = new LocalModelExecutionPreferences(this);
        models.Save(new(modelReasoner is not null, false));
        var appearances = new LocalAppearancePreferences(this, NullLogger<LocalAppearancePreferences>.Instance);
        var bootstrapper = new DependencyBootstrapper(modelProbe is null ? [] : [modelProbe], NullLogger<DependencyBootstrapper>.Instance);
        var execution = new FixtureExecution();
        var catalogue = new ArtifactCatalogue([
            new("kora.fixture.inspect", ArtifactKind.Skill, "Inspect synthetic fixture", "Non-executable native UX catalogue fixture.",
                "inspect-fixture", ["inspect synthetic fixture"], "synthetic", "1.0.0", new string('0', 64),
                "Inspection fixture only. No script or effect is admitted."),
            .. ListOverflow ? Enumerable.Range(1, 12).Select(index =>
                new ArtifactDefinition($"kora.fixture.overflow{index:00}", ArtifactKind.Skill, $"Synthetic overflow {index:00}",
                    "Non-executable synthetic list overflow fixture.", $"overflow-{index:00}", [$"synthetic overflow {index:00}"], "synthetic", "1.0.0",
                    new string('0', 64), "No script, provider or effect is admitted.")) : [],
        ]);
        ArtifactCatalogue = catalogue;
        var commands = new BuiltInCommandCatalog();
        var runtime = new RecordedRuntimeObservation(bootstrapper);
        var capabilities = new ReadOnlyCapabilityRegistry(Access, new(), new(), new(new AssemblyApplicationInfo()),
            new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        queue = new(Interactions, Interactions, new(Tasks), Access, new DeterministicVersionQueueAction(capabilities),
            new(), NullLogger<SessionQueueService>.Instance, Time);
        sessions = new(Interactions, new(Tasks), Access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var clipboard = new ClipboardSnapshotBroker(new FixtureClipboardReader(), Time,
            NullLogger<ClipboardSnapshotBroker>.Instance);
        ITextToSpeechService speech = SilentSpeech is { } silent ? silent : new FixtureSpeech();
        var audioPreferences = new LocalAudioDevicePreferences(this, NullLogger<LocalAudioDevicePreferences>.Instance);
        SpeechTextConfigurationService? captionConfiguration = null;
        if (SilentSpeech is not null)
        {
            audioPreferences.SaveOutputDeviceId(SilentSpeech.GetDefaultOutputDevice().Id);
            var captionPreferences = new LocalSpeechTextPreferences(this);
            captionPreferences.Save(SpeechTextMode.CurrentUtterance);
            captionPreferences.SaveOptions(new(SpeechCaptionPlacement.TopLeft, 1));
            captionAdmission = new(Interactions, Interactions, new(Tasks));
            captionConfiguration = new(captionPreferences, captionAdmission, Audit);
        }
        assistantNameConfiguration = new(new LocalAssistantNamePreferences(this, NullLogger<LocalAssistantNamePreferences>.Instance),
            commands, Audit, NullLogger<AssistantNameConfigurationService>.Instance);
        main = new(commands, new(commands), catalogue, new(catalogue), bootstrapper,
            new(bootstrapper, execution, execution), modelReasoner ?? execution, new LocalModelApprovalPreferences(this), models, Access,
            voiceRecognition ?? Voice, speech, assistantNameConfiguration, appearances,
            new LocalOptionalSpeechOfferPreferences(this),
            audioPreferences,
            new LocalResponseOutputPreferences(this, NullLogger<LocalResponseOutputPreferences>.Instance),
            new LocalCallAwarePreferences(this, NullLogger<LocalCallAwarePreferences>.Instance),
            callState ?? new FixtureCallState(SilentSpeech is null && !Voice.SyntheticCapture
                ? Kora.Core.Communication.CallState.Unavailable : Kora.Core.Communication.CallState.Clear),
            Access, execution, dispatcher ?? new AvaloniaUiDispatcher(), Access, new AssemblyApplicationInfo(), Audit,
            NullLogger<MainViewModel>.Instance, consent, Access, VersionQuery, capabilities,
            new AppearanceConfigurationService(appearances, Audit, NullLogger<AppearanceConfigurationService>.Instance),
            new SpeechConfigurationService(new LocalTextToSpeechPreferences(this, NullLogger<LocalTextToSpeechPreferences>.Instance),
                speech, Audit, NullLogger<SpeechConfigurationService>.Instance),
            clipboard, new(new DeniedClipboardRead()), new(clipboard), new(clipboard), new(new DeniedClipboardExplanation()),
            new AuditedPreferenceWriter(Audit),
            new Kora.Application.Voice.BoundedAudioOutputCatalog(speech,
                NullLogger<Kora.Application.Voice.BoundedAudioOutputCatalog>.Instance),
            new Kora.Application.Presentation.AudioPlaybackSettings(dispatcher ?? new AvaloniaUiDispatcher(),
                NullLogger<Kora.Application.Presentation.AudioPlaybackSettings>.Instance),
            speechTextConfiguration: captionConfiguration,
            webPageGet: webPageGet, speechComponentSetup: speechComponentSetup,
            ordinaryDeadlineConfiguration: ordinaryDeadlineConfiguration,
            conversationExecution: conversationExecution,
            speechSetupComparison: speechSetupComparison, logonStartupConfiguration: logonStartupConfiguration,
            startupPresentationConfiguration: startupPresentationConfiguration,
            speechInputConfiguration: speechInputConfiguration);
        if (webSummary is not null) { Main.BindWebSummaryWorkflow(webSummary); }
        Main.BindCallOwnershipGate(() => Access.Open);
        Main.BindClipboardOwnershipGate(static () => false);
        await Main.InitializeAsync();
        for (var index = 0; index < 2; index++)
        {
            await VersionQuery.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                var created = await Interactions.CreateSessionAsync(HostActivity.RequireCurrent().Request, Token);
                if (index == 0) { LifecycleTarget = created; }
            }, Token);
        }
        var busy = HostRequest.Create(RequestOrigin.LocalUi);
        using (var activity = HostActivity.BeginRoot(busy, HostActivityLayer.Application, HostOperation.Request))
        {
            await Tasks.CommitAsync(new(busy, new(1), HostTaskState.IntentRecorded), 0, Token);
            await Interactions.CreateSessionAsync(busy, Token);
            activity.Complete(HostOperationOutcome.Completed);
        }
        initialized = true;
        if (ListOverflow) { await InitializeListOverflowAsync(); }
        if (ExactGrants) { await InitializeExactGrantsAsync(); }
        if (AllWork) { await InitializeAllWorkAsync(); }
        startup.Complete(HostOperationOutcome.Completed);
    }

    private sealed class DeniedClipboardRead : Kora.Core.Context.IClipboardReadWorkflow
    {
        public Task<Kora.Core.Context.ClipboardOutcome> ReadAsync(Func<bool> eligible, CancellationToken token) =>
            Task.FromResult(Kora.Core.Context.ClipboardOutcome.Denied);
    }

    private sealed class DeniedClipboardExplanation : Kora.Core.Context.IClipboardExplanationWorkflow
    {
        public Task<Kora.Core.Context.ClipboardExplanationResult> ExplainAsync(
            Guid? reviewedSnapshotId, Func<bool> eligible, CancellationToken token) =>
            Task.FromResult(new Kora.Core.Context.ClipboardExplanationResult(Kora.Core.Context.ClipboardExplanationOutcome.Denied));
    }

    internal async Task<WorkSessionAuthorization> AdvanceLifecycleTargetAsync()
    {
        RequireValidationAccess();
        var target = LifecycleTarget ?? throw new InvalidOperationException("The exact lifecycle fixture is unavailable.");
        var changed = await Sessions.ChangeLifecycleAsync(target.SessionId, target.Generation,
            !target.IsActive, RequestOrigin.LocalUi, Token);
        LifecycleTarget = changed;
        return changed;
    }

    internal async Task<HostQuestionRecord> CreateRevisionQuestionAsync()
    {
        RequireValidationAccess();
        if (RevisionQuestion is not null) { throw new InvalidOperationException("The revision fixture already exists."); }
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Presentation);
        await Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, Token);
        await Interactions.CreateSessionAsync(request, Token);
        await Interactions.PublishTrustedSnapshotAsync(request, new(true, false, false, true),
            proposal: null, expectedObservationRevision: 0, Token);
        var decision = await new HostQuestionService(Interactions, Time).CreateAsync(request,
            new("Synthetic stale-revision fixture: show the harmless local version?",
                QuestionKind.SingleChoice, [new("show", "Show local version")],
                purpose: "native-ux-stale-revision", sourceId: "host"),
            Time.GetUtcNow().AddMinutes(5), Token);
        if (decision.Outcome != HostInteractionOutcome.Presented || decision.Question is null)
        {
            throw new InvalidOperationException("The synthetic revision question was not admitted.");
        }
        RevisionQuestion = decision.Question;
        activity.Complete(HostOperationOutcome.Completed);
        return decision.Question;
    }

    internal async Task<HostQuestionRecord> AdvanceRevisionQuestionAsync()
    {
        RequireValidationAccess();
        var target = RevisionQuestion ?? throw new InvalidOperationException("Open the exact revision question first.");
        using var activity = HostActivity.BeginRoot(target.Key.Request, HostActivityLayer.Desktop, HostOperation.Presentation);
        var decision = await new HostQuestionService(Interactions, Time).ReviseAsync(target.Key,
            new("Synthetic replacement revision: the older displayed question must not answer this target.",
                target.Spec.Kind, target.Spec.Options, target.Spec.Minimum, target.Spec.Maximum,
                target.Spec.MaximumTextLength, target.Spec.Purpose, target.Spec.SourceId),
            Time.GetUtcNow().AddMinutes(5), Token);
        if (decision.Outcome != HostInteractionOutcome.Presented || decision.Question is null)
        {
            throw new InvalidOperationException("The exact synthetic question revision did not advance.");
        }
        RevisionQuestion = decision.Question;
        activity.Complete(HostOperationOutcome.Completed);
        return decision.Question;
    }

    internal ExplicitLinkFixture SeedExplicitLinks()
    {
        RequireValidationAccess();
        var sourceRequest = HostRequest.Create(RequestOrigin.HostSystem);
        ActivityContext sourceContext;
        TraceSnapshot source;
        using (var activity = HostActivity.BeginRoot(sourceRequest, HostActivityLayer.Application, HostOperation.Request))
        {
            sourceContext = HostActivity.RequireCurrent().Activity!.Context;
            source = TraceSnapshot.Capture(activity.Activity)
                ?? throw new InvalidOperationException("The source fixture activity was not sampled.");
            activity.Complete(HostOperationOutcome.Completed);
        }
        var missing = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var linkedRequest = HostRequest.Create(RequestOrigin.HostSystem);
        TraceSnapshot linked;
        using (var activity = HostActivity.BeginRoot(linkedRequest, HostActivityLayer.Application, HostOperation.Request,
            [new(sourceContext), new(missing)]))
        {
            linked = TraceSnapshot.Capture(activity.Activity)
                ?? throw new InvalidOperationException("The linked fixture activity was not sampled.");
            activity.Complete(HostOperationOutcome.Completed);
        }
        return new(sourceRequest, source, linkedRequest, linked, missing);
    }

    private void RequireValidationAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!initialized || !Access.CanInspect || !Access.CanControl || !Main.CanRevealPrivatePresentation)
        {
            throw new InvalidOperationException("Synthetic validation hooks require initialized private fixture ownership.");
        }
        Token.ThrowIfCancellationRequested();
    }

    internal sealed record ExplicitLinkFixture(HostRequest SourceRequest, TraceSnapshot Source,
        HostRequest LinkedRequest, TraceSnapshot Linked, ActivityContext Missing);

    internal MaintenanceViewModel CreateMaintenance()
    {
        var model = new MaintenanceViewModel(new DeniedReleaseClient(), new AssemblyApplicationInfo(), ReleaseArchitecture.X64,
            new DeniedReleaseOpener(), Time, new AvaloniaUiDispatcher(), Audit,
            NullLogger<MaintenanceViewModel>.Instance);
        model.BindGate(() => Access.Open);
        maintenance.Add(model);
        return model;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        using var disposal = BeginPhase(NativeFixturePhase.Disposal);
        disposal.Run(DisposeCore);
        disposal.Dispose();
        VerifyPhaseEvidence();
    }

    private void DisposeCore()
    {
        disposed = true;
        cancellation.Cancel();
        foreach (var model in maintenance) { model.Dispose(); }
        main?.Dispose();
        assistantNameConfiguration?.Dispose();
        // The fixture's synchronous shutdown drains only local-version work; queue disposal uses ConfigureAwait(false), with no UI continuation.
        using (var join = BeginPhase(NativeFixturePhase.Join))
        {
            join.Run(() =>
            {
#pragma warning disable VSTHRD002
                queue?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                OverflowEvents?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                captionAdmission?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                ExactGrantControl?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                SilentSpeech?.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            });
        }
        Access.Dispose();
        using (var evidenceFlush = BeginPhase(NativeFixturePhase.EvidenceFlush))
        {
            evidenceFlush.Run(() =>
            {
                Loggers.Dispose();
                evidenceProvider?.Dispose();
                dailyEvidenceLogger?.Dispose();
            });
        }
        cancellation.Dispose();
        // Delete only the uniquely created fixture child, never the supplied parent or user data.
        if (File.GetAttributes(LocalRoot).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Fixture scratch became a reparse point; automatic cleanup refused.");
        }
        Directory.Delete(LocalRoot, recursive: true);
    }

    private sealed class DeniedReleaseClient : IReleaseMetadataClient
    {
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion, ReleaseArchitecture architecture,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Native UX fixture prohibits metadata network access.");
    }

    private sealed class DeniedReleaseOpener : ICanonicalReleasePageOpener
    {
        public Task OpenAsync(ReleaseVersion version, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Native UX fixture prohibits opening a browser.");
    }
}
