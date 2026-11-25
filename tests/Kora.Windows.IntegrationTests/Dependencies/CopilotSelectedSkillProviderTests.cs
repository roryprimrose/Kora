using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

#pragma warning disable MCPSDK001 // Native callback contract, with no process, filesystem or model activity.
[Collection("Host tracing")]
public sealed class CopilotSelectedSkillProviderTests
{
    [Fact]
    public async Task OnlyExactSelectedNameCanReadAndRevocationClosesAllCallbacks()
    {
        var current = true;
        var fixture = new CopilotSdkTurnRuntimeTests.Fixture();
        using var root = fixture.Root();
        var provider = new CopilotSelectedSkillProvider(new(true, "source", "revision", "selected",
            "Description", "Exact source bytes", "Exact source bytes"),
            _ => ValueTask.FromResult(current), () => new Lease(),
            HostActivity.CaptureContinuation(HostActivityLayer.Windows, HostOperation.Runtime), TestContext.Current.CancellationToken);
        var token = TestContext.Current.CancellationToken;
        (await provider.ListSkillsAsync(token)).Should().ContainSingle(item => item.Name == "selected" && item.Directory == null);
        (await provider.ReadSkillAsync("foreign", token)).Should().BeNull();
        (await provider.ReadSkillAsync("SELECTED", token)).Should().BeNull();
        provider.WasRead.Should().BeFalse();
        (await provider.ReadSkillAsync("selected", token)).Should().Be("Exact source bytes");
        current = false;
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.ReadSkillAsync("selected", token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.ListSkillsAsync(token));
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose() { }
    }
}
#pragma warning restore MCPSDK001
