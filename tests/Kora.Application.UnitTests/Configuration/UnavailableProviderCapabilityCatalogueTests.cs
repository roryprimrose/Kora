using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

public sealed class UnavailableProviderCapabilityCatalogueTests
{
    [Theory]
    [InlineData(LocalProviderInstallation.Installed)]
    [InlineData(LocalProviderInstallation.Missing)]
    [InlineData(LocalProviderInstallation.Unknown)]
    public async Task ProductionCatalogueReportsNoInventedModelsOrAuthentication(LocalProviderInstallation installation)
    {
        var service = new UnavailableProviderCapabilityCatalogue(new Installation(installation), new ProviderProfileConfigurationFixture.ProfileTime());
        var result = await service.ObserveAsync(TestContext.Current.CancellationToken);
        result.Installation.Should().Be(installation);
        result.Catalogues.Should().HaveCount(4).And.OnlyContain(catalogue => catalogue.Models.Count == 0
            && catalogue.Status == ProviderCatalogueStatus.Unavailable && catalogue.Account == null);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await service.Invoking(item => item.ObserveAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class Installation(LocalProviderInstallation state) : ILocalProviderInstallationObservation
    {
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken) => Task.FromResult(state);
    }
}
