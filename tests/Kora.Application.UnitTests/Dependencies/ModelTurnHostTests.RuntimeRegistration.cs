using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("current", ModelTurnOutcome.Succeeded)]
    [InlineData("missing", ModelTurnOutcome.Unavailable)]
    [InlineData("foreign-selection", ModelTurnOutcome.Unavailable)]
    [InlineData("unqualified", ModelTurnOutcome.Unavailable)]
    [InlineData("revoked", ModelTurnOutcome.Unavailable)]
    [InlineData("owner-lost", ModelTurnOutcome.Denied)]
    [InlineData("cancelled", ModelTurnOutcome.Cancelled)]
    [InlineData("context-expired", ModelTurnOutcome.Denied)]
    [InlineData("task-changed", ModelTurnOutcome.Denied)]
    public async Task DynamicRegistrationRequiresTheExactProfileAndIndependentCurrentQualification(string state, ModelTurnOutcome expected)
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var source = new RegistrationSource
        {
            Resolve = (selection, profile, _) =>
            {
                profile.Should().Be(Profile());
                selection.Should().Be(ModelProviderSelection.OllamaCandidate);
                if (state is "owner-lost") { f.Current = false; }
                if (state is "cancelled") { cancellation.Cancel(); }
                if (state is "context-expired") { f.Time.Now = f.Time.Now.AddMinutes(1); }
                if (state is "task-changed") { f.Task = f.Task! with { Generation = new(2) }; }
                return Task.FromResult(state is "missing" ? null : new ModelProviderRegistration(
                    state is "foreign-selection" ? selection with { Revision = new(2) } : selection,
                    state is "unqualified" ? ModelQualificationGate.None : Fixture.Gates(selection),
                    f.Time.Now.AddMinutes(1), f.Adapter, () => state is not "revoked"));
            },
        };
        var host = f.ConfiguredProfiles(registrationSource: source);
        var admission = await host.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, cancellation.Token);
        admission.Result.Outcome.Should().Be(expected);
        source.Calls.Should().Be(1);
        if (expected == ModelTurnOutcome.Succeeded)
        { (await host.RunAsync(admission.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded); }
        else { admission.Turn.Should().BeNull(); }
        f.Adapter.Calls.Should().Be(expected == ModelTurnOutcome.Succeeded ? 1 : 0);
    }

    [Fact]
    public async Task QualificationRevocationAfterAdmissionSuppressesDispatchAndRetainedOutput()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var current = true;
        var source = new RegistrationSource
        {
            Resolve = (selection, _, _) => Task.FromResult<ModelProviderRegistration?>(new(selection,
                Fixture.Gates(selection), f.Time.Now.AddMinutes(1), f.Adapter, () => current)),
        };
        var host = f.ConfiguredProfiles(registrationSource: source);
        var admitted = await host.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token);
        current = false;
        (await host.RunAsync(admitted.Turn!, Token)).Reason.Should().Be(ModelTurnReason.QualificationPending);
        f.Adapter.Calls.Should().Be(0);
        current = true;
        admitted = await host.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token);
        f.Adapter.OnRun = () => current = false;
        var result = await host.RunAsync(admitted.Turn!, Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RegistrationSourceFailuresAreExplicitAndCannotFallBackToStaticCandidate()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var source = new RegistrationSource { Resolve = (_, _, _) => throw new IOException("private ownership failure") };
        var host = f.ConfiguredProfiles(registrationSource: source);
        await host.Invoking(value => value.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token)).Should().ThrowAsync<IOException>();
        f.Adapter.Calls.Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains("private ownership", StringComparison.Ordinal));
    }

    private sealed class RegistrationSource : IModelProviderRegistrationSource
    {
        internal int Calls;
        internal required Func<ModelProviderSelection, ProviderProfileTuple, CancellationToken, Task<ModelProviderRegistration?>> Resolve;
        public Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Resolve(selection, profile, cancellationToken);
        }
    }
}
