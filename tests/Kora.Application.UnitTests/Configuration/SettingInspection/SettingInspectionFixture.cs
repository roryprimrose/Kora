using System.Diagnostics;
using Kora.Application.Configuration;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

internal sealed class SettingInspectionFixture : IAsyncDisposable, ISessionWorkspaceAccess, ISecurityAuditLog, ISettingInspectionConversation
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    internal SettingInspectionFixture()
    {
        ActivitySource.AddActivityListener(listener);
        var controls = new AudioControlTestStore();
        Admission = new(controls, controls, new HostTaskCoordinator(controls));
        Appearance = new(new LocalAppearancePreferences(Store, NullLogger<LocalAppearancePreferences>.Instance),
            this, NullLogger<AppearanceConfigurationService>.Instance);
        Speech = new(new LocalTextToSpeechPreferences(Store, NullLogger<LocalTextToSpeechPreferences>.Instance),
            Catalog, this, NullLogger<SpeechConfigurationService>.Instance);
        Volume = new(new LocalPlaybackVolumePreferences(Store), Playback, Admission, this);
        Response = new(new LocalResponseOutputPreferences(Store, NullLogger<LocalResponseOutputPreferences>.Instance),
            Admission, this, Playback);
        Source = new(Appearance, Speech, Volume, Response);
        Workflow = new(Source, this, this, this, Time, Logger);
    }

    internal CancellationToken Token => TestContext.Current.CancellationToken;
    internal StoreData Store { get; } = new();
    internal CatalogData Catalog { get; } = new();
    internal PlaybackData Playback { get; } = new();
    internal AudioControlAdmission Admission { get; }
    internal AppearanceConfigurationService Appearance { get; }
    internal SpeechConfigurationService Speech { get; }
    internal PlaybackVolumeConfigurationService Volume { get; }
    internal ResponseModeConfigurationService Response { get; }
    internal ConfirmedSettingsSource Source { get; }
    internal SettingInspectionWorkflow Workflow { get; }
    internal Clock Time { get; } = new();
    internal LoggerData Logger { get; } = new();
    public bool CanInspect { get; set; } = true;
    public bool CanControl { get; set; } = true;
    public long ControlRevision { get; set; } = 1;
    internal bool Eligible { get; set; } = true;
    internal int Calls { get; private set; }
    internal int Joins { get; private set; }
    internal ModelTurnOutcome Outcome { get; set; } = ModelTurnOutcome.Succeeded;
    internal string Body { get; set; } = "Genuine test answer.";
    internal Action? BeforeReturn { get; set; }
    internal Func<Task>? Hold { get; set; }
    internal Func<Task>? Join { get; set; }
    internal SecurityAuditOutcome? FailAudit { get; set; }
    internal string? Defect { get; set; }
    internal TaskCompletionSource JoinEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal List<SecurityAuditEvent> Events { get; } = [];

    internal void Observe()
    {
        Appearance.Reload();
        Speech.Reload();
        Volume.Observe();
        Response.Observe();
        Store.Reads.Clear();
        Catalog.Reads = 0;
        Playback.Effects = 0;
    }

    public void Write(SecurityAuditEvent auditEvent)
    {
        if (auditEvent.Outcome == FailAudit) { throw new IOException("private audit failure"); }
        Events.Add(auditEvent);
    }

    public async Task<ConversationSubmissionReceipt> SubmitAsync(SettingInspectionReview review, Func<bool> current,
        Func<CancellationToken, Task> validate, CancellationToken token)
    {
        Calls++;
        await validate(token);
        if (Hold is not null) { await Hold(); }
        BeforeReturn?.Invoke();
        var request = new HostRequest(new(Guid.NewGuid()), review.Destination.SessionId, new(Guid.NewGuid()), review.Original.Origin);
        if (Defect is "session") { request = HostRequest.Create(review.Original.Origin); }
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var executed = Outcome is ModelTurnOutcome.Succeeded or ModelTurnOutcome.Unknown;
        var message = new ConversationMessage(request, new(1), 1, executed ? 3 : 2, Time.Now,
            Defect is "body" ? "Other question" : review.Question, Outcome, ModelTurnReason.None)
        {
            Version = executed ? 2 : 1,
            Execution = executed ? provenance : null,
            ExecutionMetadataRevision = executed ? 0 : null,
            AssistantResponse = Outcome == ModelTurnOutcome.Succeeded ? new(provenance, Body) : null,
        };
        var task = new HostTaskRecord(Defect is "task" ? HostRequest.Create(RequestOrigin.LocalUi) : request,
            new(3), Defect is "terminal" ? HostTaskState.IntentRecorded
                : Defect is "state" ? HostTaskState.Denied : message.ExpectedTaskState);
        return new(message, task);
    }

    public async Task JoinAsync()
    {
        Joins++;
        JoinEntered.TrySetResult();
        if (Join is not null) { await Join(); }
    }

    public async ValueTask DisposeAsync()
    { await Admission.DisposeAsync(); listener.Dispose(); }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class LoggerData : ILogger<SettingInspectionWorkflow>
    {
        internal bool Enabled { get; set; } = true;
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    internal sealed class StoreData : IPreferenceStore
    {
        internal Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string[]> Lines { get; } = new(StringComparer.Ordinal);
        internal List<string> Reads { get; } = [];
        internal string? FailName { get; set; }
        internal Exception? Failure { get; set; }
        internal Action<string>? Reading { get; set; }
        internal int Writes { get; private set; }
        private void Read(string name)
        {
            Reads.Add(name);
            Reading?.Invoke(name);
            if (string.Equals(name, FailName, StringComparison.Ordinal) && Failure is not null) { throw Failure; }
        }
        public string? ReadText(string fileName) { Read(fileName); return Text.GetValueOrDefault(fileName); }
        public string[]? ReadLines(string fileName) { Read(fileName); return Lines.GetValueOrDefault(fileName); }
        public void WriteText(string fileName, string contents) { Writes++; Text[fileName] = contents; }
        public void WriteLines(string fileName, IEnumerable<string> contents) { Writes++; Lines[fileName] = contents.ToArray(); }
        public void Delete(string fileName) { Writes++; Text.Remove(fileName); Lines.Remove(fileName); }
    }

    internal sealed class CatalogData : ISpeechCatalog
    {
        internal int Reads { get; set; }
        public IReadOnlyList<SpeechProvider> GetProviders() { Reads++; return []; }
        public IReadOnlyList<SpeechVoice> GetVoices() { Reads++; return []; }
        public SpeechVoice? GetDefaultVoice() => throw new NotSupportedException();
    }

    internal sealed class PlaybackData : ISpeechPlaybackService, IPlaybackVolumeControl
    {
        internal int Effects { get; set; }
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() => Effects++;
        public void SetPlaybackVolume(PlaybackVolume? volume) => Effects++;
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No synthesis is permitted.");
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
