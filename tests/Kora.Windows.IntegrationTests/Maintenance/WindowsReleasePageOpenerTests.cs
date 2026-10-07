using System.ComponentModel;
using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Maintenance;
using Kora.Windows.Maintenance;

namespace Kora.Windows.IntegrationTests.Maintenance;

public sealed class WindowsReleasePageOpenerTests
{
    [Fact]
    public async Task Native_seam_accepts_only_host_constructed_canonical_release_page_without_launching_browser()
    {
        ProcessStartInfo? requested = null;
        var opener = new WindowsReleasePageOpener(info => requested = info);
        await opener.OpenAsync(ReleaseVersion.Parse("1.2.3-beta10"), TestContext.Current.CancellationToken);
        requested!.FileName.Should().Be("https://github.com/roryprimrose/Kora/releases/tag/v1.2.3-beta10");
        requested.UseShellExecute.Should().BeTrue();
        requested.Verb.Should().Be("open");
        requested.Arguments.Should().BeEmpty();
        var failure = new WindowsReleasePageOpener(_ => throw new Win32Exception(5));
        await ((Func<Task>)(() => failure.OpenAsync(ReleaseVersion.Parse("1.2.3"), TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await ((Func<Task>)(() => opener.OpenAsync(ReleaseVersion.Parse("1.2.3"), cancelled.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => opener.OpenAsync(new(-1, 2, 3, null), TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidDataException>();
    }
}
