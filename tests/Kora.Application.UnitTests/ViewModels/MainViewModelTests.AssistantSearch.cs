using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OriginalTypedAndRenamedActivatedVoiceAssistantSearchPresentsCompleteAnswersWithoutModelOrSpeech(
        bool voice, bool assistant)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        var store = BindConversationSearch(fixture);
        const string answer = "  complete café e\u0301\r\n<script>inert</script>  ";
        store.SeedAssistantSearchAnswers(answer);
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        var command = "conversation search " + session + (assistant ? " --role ASSISTANT" : "") + " --   CAFE\u0301  ";
        Task Send(string text) => voice ? fixture.RaiseActivatedTranscriptAsync("Nova, " + text, 1) : fixture.RunAsync(text);
        await Send(command);
        fixture.ViewModel.ResponseTitle.Should().Be("Exact conversation search.");
        using (var page = JsonDocument.Parse(fixture.ViewModel.ResponseBody))
        {
            var messages = page.RootElement.GetProperty("Messages");
            messages.GetArrayLength().Should().Be(assistant ? 25 : 0);
            if (assistant)
            {
                messages[0].GetProperty("AssistantResponse").GetProperty("Body").GetString().Should().Be(answer);
                page.RootElement.GetProperty("Role").GetInt32().Should().Be((int)ConversationSearchRole.ASSISTANT);
            }
        }
        if (assistant)
        {
            await Send("conversation search next " + session);
            using var page = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
            page.RootElement.GetProperty("Messages").GetArrayLength().Should().Be(5);
            page.RootElement.GetProperty("Messages")[0].GetProperty("AssistantResponse").GetProperty("Body")
                .GetString().Should().Be(answer);
            page.RootElement.GetProperty("Role").GetInt32().Should().Be((int)ConversationSearchRole.ASSISTANT);
        }
        store.SearchOrigins.Should().OnlyContain(origin => origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.ViewModel.Transcript.Should().NotContain("CAFE").And.NotContain(answer);
    }

    private sealed partial class SessionCommandStore
    {
        internal void SeedAssistantSearchAnswers(string body)
        {
            SearchMessageCount = 30;
            searchMessages = Enumerable.Range(1, SearchMessageCount).Select(index =>
            {
                var request = new HostRequest(new(Guid.NewGuid()), Session.Authority.SessionId,
                    new(Guid.NewGuid()), RequestOrigin.LocalUi);
                var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
                return new ConversationMessage(request, new(1), index, 3, DateTimeOffset.UnixEpoch, "original USER",
                    ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
                {
                    Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
                    AssistantResponse = new(provenance, body)
                };
            }).ToImmutableArray();
        }
    }
}
