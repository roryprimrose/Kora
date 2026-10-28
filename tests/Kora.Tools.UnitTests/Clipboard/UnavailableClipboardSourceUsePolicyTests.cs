using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

namespace Kora.Tools.UnitTests.Clipboard;

public sealed class UnavailableClipboardSourceUsePolicyTests
{
    [Theory]
    [InlineData("Apparently ordinary text")]
    [InlineData("password=example-value")]
    [InlineData("Synthetic private information with no recognised credential pattern")]
    public async Task UnavailablePermissionNeverInfersAdmissionFromText(string text)
    {
        var snapshot = new ClipboardSnapshot(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            text, 3, DateTimeOffset.UnixEpoch);
        (await new UnavailableClipboardSourceUsePolicy().EvaluateAsync(snapshot, () => true, TestContext.Current.CancellationToken))
            .Should().Be(ClipboardSourceUseOutcome.Unavailable);
    }

    [Fact]
    public async Task MissingSourceAndCancelledEvaluationFailExplicitly()
    {
        var policy = new UnavailableClipboardSourceUsePolicy();
        var missing = () => policy.EvaluateAsync(null!, () => true, TestContext.Current.CancellationToken);
        await missing.Should().ThrowAsync<ArgumentNullException>();
        var snapshot = new ClipboardSnapshot(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            "source", 3, DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => policy.EvaluateAsync(snapshot, () => true, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var validate = () => policy.ValidateAsync(snapshot, TestContext.Current.CancellationToken);
        await validate.Should().ThrowAsync<InvalidOperationException>();
    }
}
