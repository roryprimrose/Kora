using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Tools.Providers;

namespace Kora.Tools.UnitTests.Providers;

public sealed class ProviderSettingsActionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ProviderCatalogueObservation Observe(LocalProviderInstallation installation = LocalProviderInstallation.Installed,
        ProviderCatalogueStatus status = ProviderCatalogueStatus.Current) => new(installation,
        Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service =>
            new ProviderCapabilityCatalogue(service, status, status == ProviderCatalogueStatus.Current ? new("account") : null,
                new(1), Now.AddMinutes(-1), Now.AddMinutes(1), status == ProviderCatalogueStatus.Current
                    ? Enumerable.Range(0, 7).Select(index => new ProviderCatalogueModel(new("model-" + index), "Exact model " + index,
                        new(service switch
                        {
                            ProviderService.Local => ModelProviderIdentity.Ollama,
                            ProviderService.Copilot => ModelProviderIdentity.Copilot,
                            ProviderService.Claude => ModelProviderIdentity.Claude,
                            _ => ModelProviderIdentity.Codex,
                        }, new(new Guid(index + 1, 0, 0, new byte[8])), new(1)),
                        [new(new("default"), "Provider managed", true)], [new(new("default"), "Provider managed", true)])).ToArray() : [])).ToArray());
    private static ProviderProfileTuple Tuple(ProviderService service = ProviderService.Local) =>
        new(service, new("account"), new(1), new("model-0"), new("default"), new("default"));

    [Theory]
    [InlineData(LocalProviderInstallation.Installed, ProviderService.Local)]
    [InlineData(LocalProviderInstallation.Missing, ProviderService.Unset)]
    [InlineData(LocalProviderInstallation.Unknown, ProviderService.Unset)]
    public void InspectionPreservesInitialPolicyAndDoesNotResolveMissingLocalInstallation(LocalProviderInstallation installation, ProviderService desired)
    {
        var action = new ProviderSettingsGet();
        var observation = Observe(installation);
        action.Execute(null, observation, Now).Desired.Service.Should().Be(desired);
        var saved = new ProviderProfilePreferences(ProviderService.Local, [Tuple()]);
        var result = action.Execute(saved, observation, Now);
        result.Desired.Should().BeSameAs(saved);
        result.IsSaved.Should().BeTrue();
        result.Effective.Should().Be(installation == LocalProviderInstallation.Installed ? Tuple() : null);
        action.Execute(saved, observation, Now.AddMinutes(1)).Effective.Should().BeNull();
        action.Execute(new(ProviderService.Claude, [Tuple(ProviderService.Claude)]), observation, Now)
            .Effective.Should().Be(Tuple(ProviderService.Claude));
    }

    [Fact]
    public void PagingAndExactModelDetailsNeverSubstituteOrInventEntries()
    {
        var observation = Observe();
        var first = new ProviderModelsList().Execute(observation, new(ProviderService.Claude), Now);
        first.Records.Should().HaveCount(6);
        first.NextOffset.Should().Be(6);
        var last = new ProviderModelsList().Execute(observation, new(ProviderService.Claude, 6), Now);
        last.Records.Should().ContainSingle().Which.Id.Value.Should().Be("model-6");
        last.NextOffset.Should().BeNull();
        new ProviderModelsList().Execute(observation, new(ProviderService.Claude), Now.AddMinutes(1)).Records.Should().BeEmpty();
        new ProviderModelsList().Execute(Observe(status: ProviderCatalogueStatus.AuthenticationRequired), new(ProviderService.Claude), Now)
            .Service.Status.Should().Be(ProviderCatalogueStatus.AuthenticationRequired);
        foreach (var input in new[] { new ProviderSettingsInput(ProviderService.Claude, -1), new(ProviderService.Claude, 7),
            new(ProviderService.Claude, 8), new(ProviderService.Claude, Count: 0), new(ProviderService.Claude, Count: 7) })
        { FluentActions.Invoking(() => new ProviderModelsList().Execute(observation, input, Now)).Should().Throw<InvalidDataException>(); }
        new ProviderModelGet().Execute(observation, new(ProviderService.Claude, Model: "model-0"), Now).Model.Id.Value.Should().Be("model-0");
        FluentActions.Invoking(() => new ProviderModelGet().Execute(observation, new(ProviderService.Claude, Model: "missing"), Now))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new ProviderModelGet().Execute(observation, new(ProviderService.Claude, Model: "model-0"), Now.AddMinutes(1)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProposalsPreserveOtherTuplesAndSupportOnlyExplicitCurrentChoicesOrResets()
    {
        var observation = Observe();
        var saved = new ProviderProfilePreferences(ProviderService.Local, [Tuple(), Tuple(ProviderService.Claude)]);
        var action = new ProviderSettingsPropose();
        var proposal = action.Execute(saved, observation, new(ProviderService.Claude, ClearModel: true), Now);
        proposal.For(ProviderService.Claude).Should().BeNull();
        proposal.For(ProviderService.Local).Should().Be(Tuple());
        saved.For(ProviderService.Claude).Should().NotBeNull();
        action.Execute(saved, observation, new(ProviderService.Unset), Now).Tuples.Should().Equal(saved.Tuples);
        action.Execute(saved, observation, new(ProviderService.Claude, Tuple: Tuple(ProviderService.Claude)), Now)
            .For(ProviderService.Claude).Should().Be(Tuple(ProviderService.Claude));
        action.Execute(saved, observation, new(ProviderService.Local, Tuple: Tuple()), Now).For(ProviderService.Local).Should().Be(Tuple());
        foreach (var input in new[] { new ProviderSettingsInput(ProviderService.Claude, Tuple: Tuple()),
            new(ProviderService.Claude, Tuple: Tuple(ProviderService.Claude), ClearModel: true) })
        { FluentActions.Invoking(() => action.Execute(saved, observation, input, Now)).Should().Throw<InvalidOperationException>(); }
        FluentActions.Invoking(() => action.Execute(saved, Observe(LocalProviderInstallation.Missing),
            new(ProviderService.Local, Tuple: Tuple()), Now)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("unsupported")]
    public async Task RefreshChecksAdmissionAndNeverPretendsAnUnsupportedCatalogueWasRefreshed(string fault)
    {
        var live = fault is not "before";
        var catalogue = new RefreshCatalogue { Observed = () => live = false };
        IProviderCapabilityCatalogue source = fault is "unsupported" ? new ObserveOnlyCatalogue() : catalogue;
        await FluentActions.Awaiting(() => new ProviderRefresh().ExecuteAsync(source, ProviderService.Claude,
            () => live, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        catalogue.Refreshes.Should().Be(fault is "after" ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshRevalidatesAdmissionAfterAnAsynchronousCatalogueCompletes(bool revoked)
    {
        var release = new TaskCompletionSource<ProviderCatalogueObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogue = new RefreshCatalogue { Pending = release.Task };
        var live = true;
        var operation = new ProviderRefresh().ExecuteAsync(catalogue, ProviderService.Claude, () => live, TestContext.Current.CancellationToken);
        catalogue.Refreshes.Should().Be(1);
        operation.IsCompleted.Should().BeFalse();
        live = !revoked;
        release.SetResult(Observe());
        if (revoked) { await FluentActions.Awaiting(() => operation).Should().ThrowAsync<InvalidOperationException>(); }
        else { (await operation).For(ProviderService.Claude).Models.Should().HaveCount(7); }
    }

    private sealed class ObserveOnlyCatalogue : IProviderCapabilityCatalogue
    {
        public Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken) => Task.FromResult(Observe());
    }

    private sealed class RefreshCatalogue : IProviderCapabilityCatalogue, IProviderCapabilityRefresh
    {
        internal int Refreshes { get; private set; }
        internal Action? Observed { get; set; }
        internal Task<ProviderCatalogueObservation>? Pending { get; set; }
        public Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken) => Task.FromResult(Observe());
        public Task<ProviderCatalogueObservation> RefreshAsync(ProviderService service, Func<bool> admitted, CancellationToken token)
        {
            Refreshes++;
            Observed?.Invoke();
            return Pending ?? Task.FromResult(Observe());
        }
    }
}
