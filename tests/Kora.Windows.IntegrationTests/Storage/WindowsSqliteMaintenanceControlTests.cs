using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Host tracing")]
public sealed class WindowsSqliteMaintenanceControlTests
{
    [Fact]
    public async Task Maintenance_uses_dedicated_original_intent_and_shared_current_session_generation_without_audio_or_prompts()
    {
        using var f = new InteractionStorageFixture();
        await f.Tasks.InitializeAsync(f.Token);
        await f.Store.InitializeAsync(f.Token);
        var creation = await f.RunAsync(() => f.Store.RecordControlIntentAsync(f.Request, f.Token));
        var authority = await f.RunAsync(() => f.Store.CreateMaintenanceControlSessionAsync(f.Request, static () => true, f.Token));
        await f.RunAsync(async () => await new HostTaskCoordinator(f.Tasks).RecordOutcomeAsync(creation, HostTaskState.Succeeded, f.Token));
        f.Count("work_sessions").Should().Be(1);
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
        f.Reopen();
        var metadata = await f.RunAsync(() => f.Store.ReadMetadataAsync(authority.SessionId, f.Token));
        metadata.Authority.Should().Be(authority);
        f.Request = InteractionStorageFixture.NewRequest(authority.SessionId);
        var intent = await f.RunAsync(() => f.Store.RecordControlIntentAsync(f.Request, f.Token));
        var count = 0;
        var observed = await f.RunAsync(() => f.Store.WithMaintenanceControlSessionAsync(f.Request, authority.Generation, () => ++count, f.Token));
        observed.Should().Be(1);
        count.Should().Be(1);
        var wrongGeneration = () => f.RunAsync(() => f.Store.WithMaintenanceControlSessionAsync(f.Request,
            new(authority.Generation.Value + 1), () => ++count, f.Token));
        await wrongGeneration.Should().ThrowAsync<InvalidOperationException>();
        count.Should().Be(1);
        await f.RunAsync(async () => await new HostTaskCoordinator(f.Tasks).RecordOutcomeAsync(intent, HostTaskState.Succeeded, f.Token));
        f.Request = InteractionStorageFixture.NewRequest(authority.SessionId);
        var lifecycle = await f.RunAsync(() => f.Store.RecordControlIntentAsync(f.Request, f.Token));
        await f.RunAsync(() => f.Store.ChangeIdleLifecycleAsync(f.Request, authority.Generation, false, static () => true, f.Token));
        var ended = () => f.RunAsync(() => f.Store.WithMaintenanceControlSessionAsync(f.Request, authority.Generation, () => ++count, f.Token));
        await ended.Should().ThrowAsync<InvalidOperationException>();
        await f.RunAsync(async () => await new HostTaskCoordinator(f.Tasks).RecordOutcomeAsync(lifecycle, HostTaskState.Succeeded, f.Token));
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }

    [Fact]
    public async Task Missing_original_context_intent_or_current_admission_never_creates_maintenance_authority()
    {
        using var f = new InteractionStorageFixture();
        await f.Tasks.InitializeAsync(f.Token);
        await f.Store.InitializeAsync(f.Token);
        var uncorrelated = () => f.Store.CreateMaintenanceControlSessionAsync(f.Request, static () => true, f.Token).AsTask();
        await uncorrelated.Should().ThrowAsync<InvalidOperationException>();
        var noIntent = () => f.RunAsync(() => f.Store.CreateMaintenanceControlSessionAsync(f.Request, static () => true, f.Token));
        await noIntent.Should().ThrowAsync<InvalidDataException>();
        await f.RunAsync(async () => await f.Store.RecordControlIntentAsync(f.Request, f.Token));
        var denied = () => f.RunAsync(() => f.Store.CreateMaintenanceControlSessionAsync(f.Request, static () => false, f.Token));
        await denied.Should().ThrowAsync<InvalidOperationException>();
        f.Count("work_sessions").Should().Be(0);
    }
}
