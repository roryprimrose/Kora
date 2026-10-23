using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Presentation;

internal sealed class AudioPlaybackSettingsFixture : IAsyncDisposable
{
    public AudioPlaybackSettingsFixture()
    {
        Admission = new(Store, Store, new HostTaskCoordinator(Store));
        Policy = new(Call);
        VolumePreferences = new(Preferences);
        RatePreferences = new(Preferences);
        Speech = new(new LocalTextToSpeechPreferences(Preferences, NullLogger<LocalTextToSpeechPreferences>.Instance),
            Playback, Audit, NullLogger<SpeechConfigurationService>.Instance);
        Volume = new(VolumePreferences, Playback, Admission, Audit);
        Rate = new(RatePreferences, Playback, Speech, Admission, Audit, NullLogger<WindowsSpeechRateConfigurationService>.Instance);
    }

    public MemoryPreferences Preferences { get; } = new();
    public QueuedDispatcher Dispatcher { get; } = new();
    public PlaybackAdapter Playback { get; } = new();
    public AuditLog Audit { get; } = new();
    public CallStateSource Call { get; } = new();
    public AudioControlTestStore Store { get; } = new();
    public AudioControlAdmission Admission { get; }
    public CallCommunicationPolicy Policy { get; }
    public LocalPlaybackVolumePreferences VolumePreferences { get; }
    public LocalWindowsSpeechRatePreferences RatePreferences { get; }
    public SpeechConfigurationService Speech { get; }
    public PlaybackVolumeConfigurationService Volume { get; }
    public WindowsSpeechRateConfigurationService Rate { get; }
    public AudioPlaybackSettings CreateSettings() => new(Dispatcher, NullLogger<AudioPlaybackSettings>.Instance, Volume, Rate);

    public async ValueTask DisposeAsync()
    {
        Policy.Dispose();
        await Admission.DisposeAsync();
    }

    internal sealed class MemoryPreferences : IPreferenceStore
    {
        private readonly Dictionary<string, string[]> lines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> text = new(StringComparer.Ordinal);
        public Exception? ReadFailure { get; set; }
        public Exception? WriteFailure { get; set; }
        public string? ReadText(string fileName) => text.GetValueOrDefault(fileName);
        public string[]? ReadLines(string fileName) => ReadFailure is { } failure ? throw failure : lines.GetValueOrDefault(fileName);
        public void WriteText(string fileName, string contents) => text[fileName] = contents;
        public void WriteLines(string fileName, IEnumerable<string> contents)
        {
            if (WriteFailure is { } failure) { throw failure; }
            lines[fileName] = contents.ToArray();
        }
        public void Delete(string fileName) { lines.Remove(fileName); text.Remove(fileName); }
    }

    internal sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> pending = new();
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => pending.Enqueue(action);
        public void Drain()
        {
            while (pending.TryDequeue(out var action)) { action(); }
        }
    }

    internal sealed class AuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            BeforeWrite?.Invoke(auditEvent);
            Events.Add(auditEvent);
        }
    }

    internal sealed class CallStateSource : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public void Change(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
    }

    internal sealed class PlaybackAdapter : ISpeechPlaybackService, ISpeechCatalog, IPlaybackVolumeControl, IWindowsSpeechRateControl
    {
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public PlaybackVolume? Volume { get; private set; }
        public WindowsSpeechRate? Rate { get; private set; }
        public Exception? InvalidationFailure { get; set; }
        public void InvalidateOutput()
        {
            if (InvalidationFailure is { } failure) { InvalidationFailure = null; throw failure; }
        }
        public void SetPlaybackVolume(PlaybackVolume? volume) => Volume = volume;
        public void SetWindowsSpeechRate(WindowsSpeechRate? rate) => Rate = rate;
        public IReadOnlyList<SpeechProvider> GetProviders() =>
            [new(SpeechProviderIds.Windows, "Windows", "Native", true, true, null, "default") { RateSupport = SpeechRateSupport.WindowsNative }];
        public IReadOnlyList<SpeechVoice> GetVoices() => [new("default", "Windows", "en-US", SpeechVoiceGender.Female)];
        public SpeechVoice? GetDefaultVoice() => GetVoices()[0];
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Settings must not synthesize or replay output.");
        public Task StopAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The host owns response playback cleanup.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
