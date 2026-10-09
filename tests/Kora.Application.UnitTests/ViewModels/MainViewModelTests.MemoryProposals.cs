using AwesomeAssertions;
using Kora.Core.Platform;
using Kora.Application.ViewModels;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task StorageExceptionsCannotLeakUserProposalValuesIntoDiagnosticMessagesOrExceptionPayloads()
    {
        var log = new MemoryPrivacyLogger();
        var fixture = new Fixture(logger: log);
        await fixture.ViewModel.InitializeAsync();
        var (store, service) = BindMemories(fixture);
        await using var owned = service;
        const string value = "private candidate included by a failing storage boundary";
        store.BeforeMemoryRead = () => throw new IOException(value);
        await fixture.RunAsync("memory propose " + store.Session.Authority.SessionId.Value.ToString("D")
            + " Decision \"" + value + "\"");
        fixture.ViewModel.ResponseTitle.Should().Be("Memory command not confirmed.");
        log.Messages.Should().NotContain(message => message.Contains(value, StringComparison.Ordinal));
        log.Events.Should().Contain("MemoryCommandFailed");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.CommandText.Should().BeEmpty();
    }

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

    private sealed class MemoryPrivacyLogger : ILogger<MainViewModel>
    {
        internal List<string> Messages { get; } = [];
        internal List<string?> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception) + exception?.ToString());
            Events.Add(eventId.Name);
        }
    }
}
