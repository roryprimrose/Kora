using System.Collections.Immutable;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Memory;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Platform;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static (SessionCommandStore Store, MemoryManagementService Service) BindMemories(Fixture fixture)
    {
        var store = new SessionCommandStore(fixture) { MemoryEnabled = true };
        var sessions = new SessionWorkspaceService(store, new(fixture.HostStore), store,
            NullLogger<SessionWorkspaceService>.Instance);
        var service = new MemoryManagementService(sessions, store, store, store, store,
            fixture.Audit, NullLogger<MemoryAdmissionService>.Instance, TimeProvider.System);
        fixture.ViewModel.BindMemoryCommands(service);
        return (store, service);
    }

    [Fact]
    public async Task TypedAndActivatedMemoryCommandsShareVisualOnlyOriginalUserControl()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var (store, service) = BindMemories(fixture);
        await using var owned = service;
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("memory help");
        fixture.ViewModel.ResponseTitle.Should().Be("Memory command observed.", fixture.ViewModel.ResponseBody);
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RunAsync("memory list " + id);
        await fixture.RaiseActivatedTranscriptAsync("Kora, memory list " + id, 1);
        store.Origins.Should().Equal(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        fixture.ViewModel.ResponseTitle.Should().Be("Memory command observed.");
        fixture.ViewModel.Transcript.Should().Be("Original-user memory management; no model turn.");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId);
        fixture.ViewModel.ResponseBody.Should().StartWith("Session presentation revoked");
        PublishVoiceSession(fixture, WindowsSessionState.Locked);
        await fixture.ViewModel.PrivacyClosureTask;
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("late-disposed")]
    [InlineData("late-locked")]
    [InlineData("late-failed-disposed")]
    public async Task MemoryEntryRetiresLatePresentationWithoutClaimingRollback(string scenario)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var (store, service) = BindMemories(fixture);
        await using var owned = service;
        if (scenario is "disposed") { fixture.ViewModel.Dispose(); }
        else if (scenario is "late-disposed") { store.BeforeTaskRead = fixture.ViewModel.Dispose; }
        else if (scenario is "late-locked") { store.BeforeTaskRead = () => fixture.Session.IsUnlocked = false; }
        else
        {
            store.BeforeMemoryRead = () =>
            {
                fixture.ViewModel.Dispose();
                throw new IOException("Observation unavailable.");
            };
        }
        await fixture.ViewModel.ExecuteMemoryCommandAsync(new(MemoryCommandOperation.List, store.Session.Authority.SessionId.Value),
            SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ResponseBody.Should().NotContain("Exact active-session metadata only");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    public async Task MemoryEntryMapsExplicitInitiatorsAndDeniesUnavailableVoiceOrModelAuthority(SecurityAuditInitiator initiator)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var (store, service) = BindMemories(fixture);
        await using var owned = service;
        await fixture.ViewModel.ExecuteMemoryCommandAsync(new(MemoryCommandOperation.List, store.Session.Authority.SessionId.Value), initiator);
        fixture.ViewModel.ResponseTitle.Should().Be(initiator == SecurityAuditInitiator.LocalUser
            ? "Memory command observed." : "Memory command not confirmed.", fixture.ViewModel.ResponseBody);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    private sealed partial class SessionCommandStore : IMemoryStore, ICapabilityHostAccess
    {
        internal bool MemoryEnabled { get; init; }
        internal Action? BeforeMemoryRead { get; set; }
        private readonly HostId<DeviceProfileIdentity> memoryProfile = new(Guid.NewGuid());
        public bool IsCurrentHost => true;
        public ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token) =>
            ValueTask.FromResult(new MemoryBoundary(memoryProfile, request.SessionId, Session.Authority.Generation,
                null, null, null, controlRevision, true, true, true));
        public ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
            Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token)
        {
            BeforeMemoryRead?.Invoke();
            admitted().Should().BeTrue();
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(transition([]).Result);
        }
    }
}
