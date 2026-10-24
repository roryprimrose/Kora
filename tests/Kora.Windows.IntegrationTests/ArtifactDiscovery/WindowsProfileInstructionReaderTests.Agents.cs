using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Artifacts;
using Kora.Windows.Context;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsProfileInstructionReaderTests
{
    [Fact]
    public async Task AgentNativeSelectionPinsUserOwnedExactSourceAndReleasesOnlyOnDispose()
    {
        CreateSource();
        var path = Path.ChangeExtension(Source, null) + ".agent.md";
        File.Move(Source, path);
        var reader = new WindowsAgentProfileReader(new WindowsLocalFileInspector(new Paths(root)));
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        using (var selection = await reader.OpenAsync(path, TestContext.Current.CancellationToken))
        {
            selection.Metadata.CanonicalPath.Should().Be(path);
            (await selection.ReadAsync(TestContext.Current.CancellationToken)).Should().Equal(Bytes);
            Action write = () => File.WriteAllBytes(path, Bytes);
            write.Should().Throw<IOException>();
            Action replace = () => File.Move(path, path + ".replacement");
            replace.Should().Throw<IOException>();
        }
        File.WriteAllBytes(path, Bytes);
        File.Move(path, path + ".replacement");
    }

    [Theory]
    [InlineData(".instructions.md")]
    [InlineData(".prompt.md")]
    [InlineData(".Agent.md")]
    public async Task AgentNativeSelectionRejectsNoncanonicalKindsBeforeOpening(string extension)
    {
        var reader = new WindowsAgentProfileReader(new WindowsLocalFileInspector(new Paths(root)));
        await reader.Invoking(value => value.OpenAsync(Path.Combine(root, "source" + extension),
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
    }
}
