using System.Diagnostics;
using System.Security.Cryptography;
using AwesomeAssertions;
using Kora.Core.Maintenance;
using Kora.Windows.Maintenance;

namespace Kora.Windows.IntegrationTests.Maintenance;

public sealed class WindowsReleaseInstallerLauncherTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "installer-launch-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Bytes = "fixture installer bytes, never executed"u8.ToArray();
    private static ReleaseAsset Asset => new(42, "Kora-Setup-1.2.3-win-x64.exe", Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)));

    [Fact]
    public async Task Verified_installer_requests_an_exact_ordinary_process_with_no_arguments_or_shell()
    {
        var path = await Prepare();
        var starts = new List<ProcessStartInfo>();
        await new WindowsReleaseInstallerLauncher(() => true, starts.Add)
            .LaunchAsync(path, Asset, () => true, TestContext.Current.CancellationToken);
        var start = starts.Should().ContainSingle().Which;
        start.FileName.Should().Be(path);
        start.UseShellExecute.Should().BeFalse();
        start.ArgumentList.Should().BeEmpty();
        start.Arguments.Should().BeEmpty();
        start.WorkingDirectory.Should().Be(root);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("size")]
    [InlineData("elevated")]
    [InlineData("revoked")]
    [InlineData("changed-context")]
    [InlineData("cancel")]
    public async Task Changed_identity_or_authority_never_starts_a_process(string failure)
    {
        var path = await Prepare();
        var calls = 0;
        var contextReads = 0;
        var launcher = new WindowsReleaseInstallerLauncher(
            () => failure is not "elevated" && (failure is not "changed-context" || ++contextReads == 1), _ => calls++);
        var asset = failure switch
        {
            "identity" => Asset with { Sha256 = new string('b', 64) },
            "size" => Asset with { Bytes = 1 },
            _ => Asset,
        };
        using var cancellation = new CancellationTokenSource();
        bool Admitted()
        {
            if (failure is "cancel") { cancellation.Cancel(); }
            return failure is not "revoked";
        }
        var action = () => launcher.LaunchAsync(path, asset, Admitted, cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Windows_start_failure_is_explicit_and_never_reported_as_installation_success()
    {
        var path = await Prepare();
        var launcher = new WindowsReleaseInstallerLauncher(() => true,
            _ => throw new System.ComponentModel.Win32Exception(5));
        var action = () => launcher.LaunchAsync(path, Asset, () => true, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<IOException>().WithMessage("*could not start*");
    }

    [Theory]
    [InlineData("relative.exe")]
    [InlineData("foreign.exe")]
    public async Task Relative_and_foreign_paths_are_not_launch_targets(string name)
    {
        var calls = 0;
        var launcher = new WindowsReleaseInstallerLauncher(() => true, _ => calls++);
        var path = name is "relative.exe" ? name : Path.Combine(root, name);
        var action = () => launcher.LaunchAsync(path, Asset, () => true, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidDataException>();
        calls.Should().Be(0);
    }

    private async Task<string> Prepare()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Asset.Name);
        await File.WriteAllBytesAsync(path, Bytes, TestContext.Current.CancellationToken);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }
}
