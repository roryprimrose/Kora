using AwesomeAssertions;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsOllamaInstallationObservationTests
{
    [Theory]
    [InlineData(FileAttributes.Normal, LocalProviderInstallation.Installed)]
    [InlineData(FileAttributes.ReadOnly, LocalProviderInstallation.Installed)]
    [InlineData(FileAttributes.Directory, LocalProviderInstallation.Unknown)]
    [InlineData(FileAttributes.ReparsePoint, LocalProviderInstallation.Unknown)]
    public async Task OnlySupportedRegularExecutableMetadataCountsAsInstalled(FileAttributes attributes, LocalProviderInstallation expected)
    {
        string? observed = null;
        var service = new WindowsOllamaInstallationObservation(path => { observed = path; return attributes; });
        (await service.ObserveAsync(TestContext.Current.CancellationToken)).Should().Be(expected);
        observed.Should().Be(WindowsOllamaSetupService.InstalledExecutablePath);
    }

    [Theory]
    [InlineData("file", LocalProviderInstallation.Missing)]
    [InlineData("directory", LocalProviderInstallation.Missing)]
    [InlineData("io", LocalProviderInstallation.Unknown)]
    [InlineData("access", LocalProviderInstallation.Unknown)]
    public async Task MissingAndUnobservableInstallationsAreDistinctAndNoEndpointIsProbed(string failure, LocalProviderInstallation expected)
    {
        var service = new WindowsOllamaInstallationObservation(_ => throw failure switch
        {
            "file" => new FileNotFoundException(),
            "directory" => new DirectoryNotFoundException(),
            "io" => new IOException(),
            _ => new UnauthorizedAccessException(),
        });
        (await service.ObserveAsync(TestContext.Current.CancellationToken)).Should().Be(expected);
    }

    [Fact]
    public async Task CancelledObservationDoesNotReadEvenInstallationMetadata()
    {
        var calls = 0;
        var service = new WindowsOllamaInstallationObservation(_ => { calls++; return FileAttributes.Normal; });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await service.Invoking(item => item.ObserveAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(0);
        using var later = new CancellationTokenSource();
        service = new WindowsOllamaInstallationObservation(_ => { later.Cancel(); return FileAttributes.Normal; });
        await service.Invoking(item => item.ObserveAsync(later.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
