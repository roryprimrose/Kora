using AwesomeAssertions;
using Kora.Core.Memory;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ExactTypedAndActivatedUserProposalsAreVisualMetadataOnlyAndPrivacyClosureDiscardsThem()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var (store, service) = BindMemories(fixture);
        await using var owned = service;
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        const string value = "private original user proposal";
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("memory propose " + session + " Decision \"" + value + "\"");
        fixture.ViewModel.CommandText.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be("Memory command Succeeded.", fixture.ViewModel.ResponseBody);
        fixture.ViewModel.ResponseBody.Should().Contain("Proposed").And.Contain("Pending").And.NotContain(value);
        await fixture.RaiseActivatedTranscriptAsync("Kora, memory propose " + session + " ExplicitFact \"" + value + "\"", 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Memory command Succeeded.", fixture.ViewModel.ResponseBody);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.Transcript.Should().NotContain(value);
        await fixture.RunAsync("memory list " + session);
        fixture.ViewModel.ResponseBody.Should().NotContain(value);
        fixture.ViewModel.CommandText = "memory propose " + session + " Decision \"discard draft\"";
        PublishVoiceSession(fixture, WindowsSessionState.Locked);
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.CommandText.Should().BeEmpty();
        PublishVoiceSession(fixture, WindowsSessionState.Unlocked);
        var listed = await service.ExecuteAsync(new(Kora.Core.Commands.MemoryCommandOperation.List,
            store.Session.Authority.SessionId.Value), Kora.Core.Hosting.RequestOrigin.LocalUi, () => true, CancellationToken.None);
        listed.Memories.Should().BeEmpty();
        listed.Inspected.Should().BeNull();
    }
}
