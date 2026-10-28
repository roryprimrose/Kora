using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Core.Voice;
using Kora.SettingInspection;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class SettingInspectionWorkflowTests
{
    [Theory]
    [InlineData("success", SettingInspectionOutcome.Succeeded)]
    [InlineData("unsupported", SettingInspectionOutcome.Unsupported)]
    [InlineData("unqualified", SettingInspectionOutcome.Unavailable)]
    [InlineData("source", SettingInspectionOutcome.Unavailable)]
    [InlineData("late-source", SettingInspectionOutcome.Unconfirmed)]
    public async Task ActualConfirmedScalarsAndCanonicalHostRetainOnlyOriginalQuestionAndGenuineLocalFinal(
        string mode, SettingInspectionOutcome expected)
    {
        await using var f = new Fixture(mode);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("What is my reading text size?", () => true, f.Token);
        f.Storage.Count("work_sessions").Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        if (mode is "source") { f.Preferences.Text["reader-text-scale-percent.txt"] = "150"; }
        if (mode is "late-source") { f.Adapter.BeforeReturn = () => f.Preferences.Text["reader-text-scale-percent.txt"] = "150"; }
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().Be(expected, "mode {0}, reason {1}", mode, result.Receipt?.Message.Reason);
        f.Adapter.Calls.Should().Be(mode is "unqualified" or "source" ? 0 : 1);
        f.Storage.Count("work_sessions").Should().Be(mode is "source" ? 0 : 1);
        f.Storage.Count("conversation_messages").Should().Be(mode is "source" ? 0 : 1);
        f.Storage.Count("scoped_grants").Should().Be(0);
        f.Storage.Count("host_questions").Should().Be(0);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        if (f.Adapter.Wire is { } wire)
        {
            using var json = JsonDocument.Parse(wire);
            json.RootElement.GetProperty("UserRequest").GetString().Should().Be(review.Review.Question);
            json.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
            json.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(review.Review.Source);
            json.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.Length.Should().BeLessThanOrEqualTo(ModelContextEnvelope.MaximumInputUtf8Bytes);
        }
        if (result.Receipt is { } receipt)
        {
            f.Storage.Reopen();
            await f.Storage.Store.InitializeAsync(f.Token);
            var actual = await f.Storage.Store.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
                receipt.Message.Request.RequestId, f.Token);
            actual.Should().Be(receipt.Message);
            actual.Body.Should().Be(review.Review.Question);
            actual.AssistantResponse?.Body.Should().Be(expected == SettingInspectionOutcome.Succeeded ? AdapterData.Answer
                : expected == SettingInspectionOutcome.Unsupported ? SettingInspectionReview.Unsupported : null);
            actual.Body.Should().NotContain("confirmed saved");
        }
        if (expected is not (SettingInspectionOutcome.Succeeded or SettingInspectionOutcome.Unsupported))
        { f.Controls.Audits.Where(item => string.Equals(item.ActionId, "setting.inspection.local", StringComparison.Ordinal))
            .Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded); }
        (await f.Workflow.AnswerAsync(review, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        string.Join(' ', f.Controls.Audits.Select(item => item.ActionId + item.TargetId + item.ReasonCode))
            .Should().NotContain(review.Review.Question).And.NotContain(review.Review.Source);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly DependencyBootstrapper dependencies = new([], NullLogger<DependencyBootstrapper>.Instance);
        private readonly AudioControlAdmission audio;
        internal Fixture(string mode = "success")
        {
            Controls = new();
            Adapter = new() { Mode = mode };
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(Controls, new(), new(), new(Controls), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            var gates = mode is "unqualified" ? ModelQualificationGate.None
                : ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost;
            Host = new(Storage.Store, Controls, Controls, registry, Controls, NullLogger<ModelTurnHost>.Instance,
                Storage.Time, [new(ModelProviderSelection.OllamaCandidate, gates, Storage.Time.Now.AddHours(1), Adapter)]);
            var sessions = new SessionWorkspaceService(Storage.Store, new(Storage.Tasks), Controls, NullLogger<SessionWorkspaceService>.Instance);
            Conversation = new(Storage.Store, Storage.Store, sessions, Controls, new AdmissionData(Host),
                Storage.Time, NullLogger<SessionConversationService>.Instance, Host);
            audio = new(Storage.Store, Storage.Store, new(Storage.Tasks));
            Preferences.Text["appearance-theme.txt"] = "Dark";
            Preferences.Text["reader-text-scale-percent.txt"] = "125";
            var appearance = new AppearanceConfigurationService(
                new LocalAppearancePreferences(Preferences, NullLogger<LocalAppearancePreferences>.Instance),
                Controls, NullLogger<AppearanceConfigurationService>.Instance);
            var speech = new SpeechConfigurationService(
                new LocalTextToSpeechPreferences(Preferences, NullLogger<LocalTextToSpeechPreferences>.Instance),
                new CatalogData(), Controls, NullLogger<SpeechConfigurationService>.Instance);
            var volume = new PlaybackVolumeConfigurationService(new LocalPlaybackVolumePreferences(Preferences), Playback, audio, Controls);
            var response = new ResponseModeConfigurationService(
                new LocalResponseOutputPreferences(Preferences, NullLogger<LocalResponseOutputPreferences>.Instance), audio, Controls, Playback);
            appearance.Reload();
            speech.Reload();
            volume.Observe();
            response.Observe();
            Playback.Effects = 0;
            Preferences.Reads = 0;
            Workflow = new(new(appearance, speech, volume, response), new SettingInspectionConversation(sessions, Conversation, Host),
                Controls, Controls, Storage.Time, NullLogger<SettingInspectionWorkflow>.Instance);
        }

        internal InteractionStorageFixture Storage { get; } = new();
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal ControlsData Controls { get; }
        internal AdapterData Adapter { get; }
        internal PreferencesData Preferences { get; } = new();
        internal PlaybackData Playback { get; } = new();
        internal ModelTurnHost Host { get; }
        internal SessionConversationService Conversation { get; }
        internal SettingInspectionWorkflow Workflow { get; }
        internal async Task InitializeAsync()
        {
            await Storage.Tasks.InitializeAsync(Token);
            await Storage.Store.InitializeAsync(Token);
        }
        public async ValueTask DisposeAsync()
        {
            await Conversation.DisposeAsync();
            await Host.DisposeAsync();
            await audio.DisposeAsync();
            dependencies.Dispose();
            Storage.Dispose();
        }
    }

    internal sealed class ControlsData : ISessionWorkspaceAccess, ICapabilityHostAccess, ISecurityAuditLog, IApplicationInfo
    {
        public bool CanInspect { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; } = 1;
        public bool IsOwningHost => true;
        public bool IsCurrentHost => true;
        public bool IsUnlocked => true;
        public string Name => "Kora";
        public string Version => "1.0";
        internal List<SecurityAuditEvent> Audits { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Audits.Add(auditEvent);
    }

    private sealed class AdmissionData(ModelTurnHost host) : IModelTurnAdmission
    {
        public bool IsQuiescent => host.IsQuiescent;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context, ModelHandoffReview? review,
            CancellationToken token)
        {
            choice.Should().Be(ModelTurnChoice.Local);
            context.ToolMode.Should().Be(ModelToolMode.None);
            return host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, token);
        }
    }

    internal sealed class AdapterData : IModelTurnAdapter
    {
        internal const string Answer = "The confirmed reading text scale is 125 percent.";
        internal string Mode { get; init; } = "success";
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        internal Action? BeforeReturn { get; set; }
        internal Func<Task>? Hold { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Wire = context.ToArray();
            Entered.TrySetResult();
            if (Hold is not null) { await Hold(); }
            BeforeReturn?.Invoke();
            return new(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(Mode is "unsupported" ? SettingInspectionReview.Unsupported : Answer, null));
        }
    }

    internal sealed class PreferencesData : IPreferenceStore
    {
        internal Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        internal int Reads { get; set; }
        internal int Writes { get; private set; }
        public string? ReadText(string fileName) { Reads++; return Text.GetValueOrDefault(fileName); }
        public string[]? ReadLines(string fileName) { Reads++; return null; }
        public void WriteText(string fileName, string contents) { Writes++; throw new NotSupportedException(); }
        public void WriteLines(string fileName, IEnumerable<string> contents) { Writes++; throw new NotSupportedException(); }
        public void Delete(string fileName) { Writes++; throw new NotSupportedException(); }
    }

    private sealed class CatalogData : ISpeechCatalog
    {
        public IReadOnlyList<SpeechProvider> GetProviders() => [];
        public IReadOnlyList<SpeechVoice> GetVoices() => [];
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
            throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
