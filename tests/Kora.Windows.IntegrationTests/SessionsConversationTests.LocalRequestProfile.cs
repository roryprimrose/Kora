using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("input")]
    [InlineData("tuple")]
    [InlineData("proposal")]
    [InlineData("hidden")]
    public async Task NativeLocalRequestProfileReviewsCompleteTupleAndUnchangedInputBeforeExactSeparateDecision(string decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new LocalProfileAdapter();
            await using var composed = new Composition(f, adapter: adapter,
                localRequestCatalogue: new SessionsProviderProfileTests.Catalogue(f.Time));
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.RefreshProviders();
                var previous = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
                model.ComposerBody = "  Original e\u0301🙂\r\nend  ";
                model.CanReviewLocalRequestProfile.Should().BeFalse();
                model.LocalRequestModel = model.LocalRequestModels[0];
                model.LocalRequestReasoning = model.LocalRequestReasonings[^1];
                model.LocalRequestContext = model.LocalRequestContexts[^1];
                model.CanReviewLocalRequestProfile.Should().BeTrue();
                window.FindControl<Button>("ReviewLocalRequestProfile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.CanConfirmLocalRequestProfile.Should().BeTrue(model.ConversationStatus);
                model.LocalRequestProfileText.Should().Contain("This request only").And.Contain("effort-next")
                    .And.Contain("window-next").And.Contain(model.ComposerBody);
                adapter.Profiles.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(0);
                var captured = model.CaptureLocalRequestProfileDecision(true);
                if (decision is "input") { model.ComposerBody = model.ComposerBody; }
                if (decision is "tuple") { model.LocalRequestReasoning = model.LocalRequestReasonings[0]; }
                if (decision is "hidden") { model.SetProviderSurfaceOpen(false); }
                if (decision is "proposal")
                {
                    await model.ReviewLocalRequestProfileAsync();
                    await captured();
                    adapter.Profiles.Should().BeEmpty();
                    model.ConversationStatus.Should().Contain("changed");
                }
                if (decision is "cancel") { model.CancelLocalRequestProfile(); }
                if (decision is "confirm")
                {
                    window.FindControl<Button>("ConfirmLocalRequestProfile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.ConversationOperation;
                    model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be("Genuine chosen local answer");
                    adapter.Profiles.Single()!.Reasoning.Value.Should().Be("effort-next");
                    adapter.Profiles.Single()!.Context.Value.Should().Be("window-next");
                    model.ComposerBody.Should().BeEmpty();
                    model.ConversationStatus.Should().Contain("Succeeded");
                }
                else if (decision is not "proposal") { await captured(); }
                if (decision is not "confirm")
                {
                    f.Count("conversation_messages").Should().Be(0);
                    adapter.Profiles.Should().BeEmpty();
                    model.ComposerBody.Should().NotBeEmpty();
                }
                var after = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
                after.Revision.Should().Be(previous.Revision);
                after.Desired.Serialize().Should().Be(previous.Desired.Serialize());
                new Kora.Application.Configuration.LocalProviderProfilePreferences(f.Paths).Load().Should().BeNull();
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TypedAndActivatedOnceUseSameOriginalRequestReviewWithoutRelabellingOrPreferenceWrite(bool voice, bool revokeVoice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new LocalProfileAdapter();
            await using var composed = new Composition(f, adapter: adapter,
                localRequestCatalogue: new SessionsProviderProfileTests.Catalogue(f.Time));
            composed.Audit.ExpectedOrigin = voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: true);
            await native.InitializeAsync();
            native.Main.BindSessionCommands(composed.Workspace,
                new(composed.Workspace, composed.Conversation, composed.Access, f.Time));
            const string body = "Original once request";
            var input = "session request once " + f.Request.SessionId.Value.ToString("D")
                + " 1 0 account 1 model effort-next window-next -- " + body;
            if (voice)
            {
                native.Main.BindVoiceOwnershipGate(() => true);
                await native.Main.SetVoiceConsentAsync(true);
                var reviewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                native.Main.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName is nameof(native.Main.ResponseBody) && native.Main.ResponseBody.Contains("localRequestProfile", StringComparison.Ordinal))
                    { reviewed.TrySetResult(); }
                };
                await native.Main.BeginPushToTalkCommand.ExecuteAsync();
                await native.Main.EndPushToTalkCommand.ExecuteAsync();
                native.Voice.PublishTranscript("Kora, " + input);
                await reviewed.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            }
            else
            {
                native.Main.CommandText = input;
                await native.Main.RunTypedCommand.ExecuteAsync();
            }
            native.Main.ResponseBody.Should().Contain("localRequestProfile").And.Contain("This request only");
            adapter.Profiles.Should().BeEmpty();
            if (revokeVoice) { await native.Main.SetVoiceConsentAsync(false); }
            native.Main.CommandText = "session request confirm";
            await native.Main.RunTypedCommand.ExecuteAsync();
            if (revokeVoice)
            {
                adapter.Profiles.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(0);
                return;
            }
            adapter.Profiles.Single()!.Context.Value.Should().Be("window-next");
            var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
            message.Body.Should().Be(body);
            message.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            message.AssistantResponse!.Body.Should().Be("Genuine chosen local answer");
            new Kora.Application.Configuration.LocalProviderProfilePreferences(f.Paths).Load().Should().BeNull();
        });
    }

    private sealed class LocalProfileAdapter : IModelTurnAdapter
    {
        internal List<ProviderProfileTuple?> Profiles { get; } = [];
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<Kora.Core.Tools.CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Profiles.Add(profile);
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new("Genuine chosen local answer", null)));
        }
    }
}
