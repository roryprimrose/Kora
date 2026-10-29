using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Core.Voice;
using Kora.SettingInspection;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class PresenceSettingInspectionTests
{
    [Theory]
    [InlineData(null, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    public async Task ExactBooleanReviewSeparatelyAuthorizesOneRealLocalConversationAnswer(bool? saved, RequestOrigin origin)
    {
        await using var f = new Fixture(saved);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync("What is my current appearance.presence-display setting?", () => true, f.Token);
        var observation = session.Review.Observations.Single(item => item.Setting == RegisteredSetting.PresenceDisplay);
        session.Review.Observations.Should().HaveCount(7);
        observation.IsSaved.Should().Be(saved.HasValue);
        observation.Value.Should().Be(saved is false ? 0 : 1);
        session.Review.Source.Should().Contain($"appearance.presence-display = {(saved is false ? "false" : "true")} boolean");
        session.Review.Text.Should().Contain(session.Review.Source).And.Contain(session.Review.Question)
            .And.Contain("Confirm", "the inert recommendation policy retains the separate setting-write route");
        f.Adapter.Calls.Should().Be(0);
        f.Storage.Count("work_sessions").Should().Be(0);
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Succeeded);
        result.Receipt.Should().NotBeNull();
        result.Receipt!.Message.Request.Origin.Should().Be(origin);
        result.Receipt.Message.AssistantResponse!.Body.Should().Be(f.Adapter.Answer);
        f.Adapter.Calls.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(session.Review.Question);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(session.Review.Source);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        f.Storage.Reopen();
        await f.Storage.Store.InitializeAsync(f.Token);
        var retained = await f.Storage.Store.ReadConversationMessageAsync(result.Receipt.Message.Request.SessionId,
            result.Receipt.Message.Request.RequestId, f.Token);
        retained.Should().Be(result.Receipt.Message);
        retained.Body.Should().Be(session.Review.Question).And.NotContain("confirmed saved");
        retained.AssistantResponse!.Body.Should().Be(f.Adapter.Answer);
        f.Storage.Count("conversation_messages").Should().Be(1);
        f.Storage.Count("scoped_grants").Should().Be(0);
        (await f.Workflow.AnswerAsync(session, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        f.Adapter.Calls.Should().Be(1);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }

    [Theory]
    [InlineData(false, SettingInspectionOutcome.Unavailable)]
    [InlineData(true, SettingInspectionOutcome.Unconfirmed)]
    public async Task ChangedPresenceBeforeOrAcrossActualModelWorkCannotPublishAnAnswer(bool duringModel,
        SettingInspectionOutcome expected)
    {
        await using var f = new Fixture(false);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync("Is presence display enabled?", () => true, f.Token);
        void Change() => f.Preferences.Text["presence-display-enabled.txt"] = "True";
        if (duringModel) { f.Adapter.BeforeReturn = Change; }
        else { Change(); }
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().Be(expected);
        result.Receipt.Should().BeNull();
        f.Adapter.Calls.Should().Be(duringModel ? 1 : 0);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Controls.Audits.Where(item => string.Equals(item.ActionId, "setting.inspection.local", StringComparison.Ordinal))
            .Should().NotContain(item => item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DependencyBootstrapper dependencies = new([], NullLogger<DependencyBootstrapper>.Instance);
        private readonly AudioControlAdmission audio;
        private readonly SessionConversationService conversations;

        internal Fixture(bool? saved)
        {
            if (saved.HasValue) { Preferences.Text["presence-display-enabled.txt"] = saved.Value ? "True" : "False"; }
            Adapter = new(saved ?? PresenceSettings.DefaultDisplayEnabled);
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(Controls, new(), new(), new(Controls), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            Host = new(Storage.Store, Controls, Controls, registry, Controls, NullLogger<ModelTurnHost>.Instance,
                Storage.Time, [new(ModelProviderSelection.OllamaCandidate,
                    ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    Storage.Time.Now.AddHours(1), Adapter)]);
            var sessions = new SessionWorkspaceService(Storage.Store, new(Storage.Tasks), Controls, NullLogger<SessionWorkspaceService>.Instance);
            conversations = new(Storage.Store, Storage.Store, sessions, Controls, new AdmissionData(Host),
                Storage.Time, NullLogger<SessionConversationService>.Instance, Host);
            audio = new(Storage.Store, Storage.Store, new(Storage.Tasks));
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
            Workflow = new(new(appearance, speech, volume, response), new SettingInspectionConversation(sessions, conversations, Host),
                Controls, Controls, Storage.Time, NullLogger<SettingInspectionWorkflow>.Instance);
        }

        internal InteractionStorageFixture Storage { get; } = new();
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal SettingInspectionWorkflowTests.ControlsData Controls { get; } = new();
        internal SettingInspectionWorkflowTests.PreferencesData Preferences { get; } = new();
        internal SettingInspectionWorkflowTests.PlaybackData Playback { get; } = new();
        internal ProviderData Adapter { get; }
        internal ModelTurnHost Host { get; }
        internal SettingInspectionWorkflow Workflow { get; }

        internal async Task InitializeAsync()
        {
            await Storage.Tasks.InitializeAsync(Token);
            await Storage.Store.InitializeAsync(Token);
        }

        public async ValueTask DisposeAsync()
        {
            await conversations.DisposeAsync();
            await Host.DisposeAsync();
            await audio.DisposeAsync();
            dependencies.Dispose();
            Storage.Dispose();
        }
    }

    private sealed class AdmissionData(ModelTurnHost host) : IModelTurnAdmission
    {
        public bool IsQuiescent => host.IsQuiescent;

        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context, ModelHandoffReview? review,
            CancellationToken token)
        {
            choice.Should().Be(ModelTurnChoice.Local);
            context.ToolMode.Should().Be(ModelToolMode.None);
            context.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly);
            return host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, token);
        }
    }

    private sealed class ProviderData(bool enabled) : IModelTurnAdapter
    {
        internal string Answer { get; } = $"Your confirmed presence display preference is {(enabled ? "true" : "false")}.";
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        internal Action? BeforeReturn { get; set; }

        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Wire = context.ToArray();
            BeforeReturn?.Invoke();
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(Answer, null)));
        }
    }

    private sealed class CatalogData : ISpeechCatalog
    {
        public IReadOnlyList<SpeechProvider> GetProviders() => [];
        public IReadOnlyList<SpeechVoice> GetVoices() => [];
        public SpeechVoice? GetDefaultVoice() => throw new NotSupportedException();
    }
}
