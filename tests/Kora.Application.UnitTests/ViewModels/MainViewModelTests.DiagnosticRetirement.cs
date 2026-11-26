using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, "session diagnostics plan 12345678-1234-1234-abcd-123456789012 version,runtimes")]
    [InlineData(true, "session diagnostics plan 12345678-1234-1234-abcd-123456789012 version,runtimes")]
    [InlineData(false, "queue enqueue 12345678-1234-1234-abcd-123456789012 1 0 12345678-1234-1234-abcd-123456789013 12345678-1234-1234-abcd-123456789014 version")]
    [InlineData(true, "queue dispatch 12345678-1234-1234-abcd-123456789012 1 0")]
    [InlineData(false, "show fixed read queue settings")]
    [InlineData(true, "set fixed read pending lifetime to 60 minutes")]
    public async Task RetiredDiagnosticProductInputNeverReachesModelSpeechOrQueueAdmission(bool voice, string input)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + input, 1); }
        else { await f.RunAsync(input); }

        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.ResponseTitle.Should().NotBe("Session diagnostics plan prepared.");
    }
}
