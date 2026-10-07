using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Host tracing")]
public sealed class WindowsSqliteAudioControlAdmissionTests
{
    [Fact]
    public async Task Real_audio_admission_persists_active_host_session_exact_generation_and_no_questions_or_grants()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        await using var admission = new AudioControlAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        var request = await admission.RunAsync(RequestOrigin.LocalUi, static () => true,
            static (request, authority) => { authority.IsActive.Should().BeTrue(); return request; }, fixture.Token);
        fixture.Request = request;
        var metadata = await fixture.RunAsync(() => fixture.Store.ReadMetadataAsync(request.SessionId, fixture.Token));
        metadata.Authority.IsActive.Should().BeTrue();
        fixture.Count("work_sessions").Should().Be(1);
        fixture.Count("host_questions").Should().Be(0);
        fixture.Count("scoped_grants").Should().Be(0);
        fixture.Reopen();
        var reopened = await fixture.RunAsync(() => fixture.Store.ReadMetadataAsync(request.SessionId, fixture.Token));
        reopened.Authority.Should().Be(metadata.Authority);
        fixture.Request = InteractionStorageFixture.NewRequest(request.SessionId);
        var intent = await fixture.RunAsync(() => fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        var value = await fixture.RunAsync(() => fixture.Store.WithAudioControlSessionAsync(fixture.Request,
            metadata.Authority.Generation, static () => 42, fixture.Token));
        value.Should().Be(42);
        await fixture.RunAsync(async () => await new HostTaskCoordinator(fixture.Tasks)
            .RecordOutcomeAsync(intent, HostTaskState.Succeeded, fixture.Token));
        fixture.Request = InteractionStorageFixture.NewRequest(request.SessionId);
        await fixture.RunAsync(async () => await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        await fixture.RunAsync(() => fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, metadata.Authority.Generation,
            false, static () => true, fixture.Token));
        var stale = () => fixture.RunAsync(() => fixture.Store.WithAudioControlSessionAsync(fixture.Request,
            metadata.Authority.Generation, static () => 0, fixture.Token));
        await stale.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Missing_intent_unknown_host_and_denied_original_owner_do_not_create_audio_session()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        var uncorrelated = () => fixture.Store.CreateAudioControlSessionAsync(fixture.Request, static () => true, fixture.Token).AsTask();
        await uncorrelated.Should().ThrowAsync<InvalidOperationException>();
        var noIntent = () => fixture.RunAsync(() => fixture.Store.CreateAudioControlSessionAsync(fixture.Request, static () => true, fixture.Token));
        await noIntent.Should().ThrowAsync<InvalidDataException>();
        await fixture.RunAsync(async () => await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        var denied = () => fixture.RunAsync(() => fixture.Store.CreateAudioControlSessionAsync(fixture.Request, static () => false, fixture.Token));
        await denied.Should().ThrowAsync<InvalidOperationException>();
        fixture.Count("work_sessions").Should().Be(0);
    }
}
