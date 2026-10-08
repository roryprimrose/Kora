using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Host tracing")]
public sealed class WindowsSqliteSharedSkillAdmissionTests
{
    [Fact]
    public async Task Native_shared_skill_admission_persists_exact_session_without_questions_or_grants_and_rejects_stale_generation()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        await using var admission = new SharedSkillAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        var request = await admission.RunAsync(static () => true,
            static (request, authority, _) =>
            {
                authority.IsActive.Should().BeTrue();
                request.Origin.Should().Be(RequestOrigin.LocalUi);
                return Task.FromResult(request);
            }, fixture.Token);
        fixture.Request = request;
        var metadata = await fixture.RunAsync(() => fixture.Store.ReadMetadataAsync(request.SessionId, fixture.Token));
        fixture.Count("work_sessions").Should().Be(1);
        fixture.Count("host_questions").Should().Be(0);
        fixture.Count("scoped_grants").Should().Be(0);
        fixture.Reopen();
        (await fixture.RunAsync(() => fixture.Store.ReadMetadataAsync(request.SessionId, fixture.Token)))
            .Authority.Should().Be(metadata.Authority);
        fixture.Request = InteractionStorageFixture.NewRequest(request.SessionId);
        var intent = await fixture.RunAsync(() => fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        var value = await fixture.RunAsync(() => fixture.Store.WithSharedSkillSessionAsync(fixture.Request,
            metadata.Authority.Generation, static () => 42, fixture.Token));
        value.Should().Be(42);
        await fixture.RunAsync(async () => await new HostTaskCoordinator(fixture.Tasks)
            .RecordOutcomeAsync(intent, HostTaskState.Succeeded, fixture.Token));
        fixture.Request = InteractionStorageFixture.NewRequest(request.SessionId);
        await fixture.RunAsync(async () => await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        await fixture.RunAsync(() => fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, metadata.Authority.Generation,
            false, static () => true, fixture.Token));
        var stale = () => fixture.RunAsync(() => fixture.Store.WithSharedSkillSessionAsync(fixture.Request,
            metadata.Authority.Generation, static () => 0, fixture.Token));
        await stale.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Unknown_context_missing_intent_and_denied_owner_cannot_create_shared_skill_session()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        var uncorrelated = () => fixture.Store.CreateSharedSkillSessionAsync(fixture.Request, static () => true, fixture.Token).AsTask();
        await uncorrelated.Should().ThrowAsync<InvalidOperationException>();
        var noIntent = () => fixture.RunAsync(() => fixture.Store.CreateSharedSkillSessionAsync(fixture.Request, static () => true, fixture.Token));
        await noIntent.Should().ThrowAsync<InvalidDataException>();
        await fixture.RunAsync(async () => await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        var denied = () => fixture.RunAsync(() => fixture.Store.CreateSharedSkillSessionAsync(fixture.Request, static () => false, fixture.Token));
        await denied.Should().ThrowAsync<InvalidOperationException>();
        fixture.Count("work_sessions").Should().Be(0);
        fixture.Count("scoped_grants").Should().Be(0);
    }
}
