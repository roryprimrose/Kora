using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills.RuntimeSetup;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed class WindowsBundledRuntimeInventoryTests
{
    [Fact]
    public async Task Production_inventory_hashes_every_file_and_declared_notice_without_executing_or_extracting_scripts()
    {
        using var storage = new OwnedStorageFixture();
        Populate(storage.LocalRoot);
        var metadata = new Metadata();
        var inspector = new WindowsBundledRuntimeInstallationInspector(storage.LocalRoot, metadata);
        var first = (await inspector.InspectAsync(CancellationToken.None))!;
        first.Files.Should().HaveCount(12);
        first.Files.Single(file => file.Name is "pwsh.exe").Digest.Should().Be(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("Synthetic PE input; never executed"))));
        first.Bytes.Should().Be(first.Files.Sum(file => file.Bytes));
        first.License.Should().Be("Synthetic MIT notice");
        first.ThirdPartyNotices.Should().Be("Synthetic bundled notices");
        first.Executable.Should().Be(Path.Combine(storage.LocalRoot, "pwsh.exe"));
        metadata.Executables.Should().ContainSingle().Which.Should().Be(first.Executable);
        (await inspector.InspectAsync(CancellationToken.None))!.InventoryDigest.Should().Be(first.InventoryDigest);
        await File.WriteAllTextAsync(Path.Combine(storage.LocalRoot, "Modules", "shared.dll"), "Changed declared runtime dependency", TestContext.Current.CancellationToken);
        (await inspector.InspectAsync(CancellationToken.None))!.InventoryDigest.Should().NotBe(first.InventoryDigest);
        metadata.Protection = "Changed synthetic ACL observation";
        (await inspector.InspectAsync(CancellationToken.None))!.InventoryDigest.Should().NotBe(first.InventoryDigest);
    }

    [Theory]
    [InlineData("pwsh.exe")]
    [InlineData("pwsh.dll")]
    [InlineData("System.Management.Automation.dll")]
    [InlineData("pwsh.runtimeconfig.json")]
    [InlineData("hostfxr.dll")]
    [InlineData("hostpolicy.dll")]
    [InlineData("coreclr.dll")]
    [InlineData("System.Private.CoreLib.dll")]
    [InlineData("LICENSE.txt")]
    [InlineData("ThirdPartyNotices.txt")]
    public async Task Partial_installed_payload_is_rejected_without_fallback(string missing)
    {
        using var storage = new OwnedStorageFixture();
        Populate(storage.LocalRoot);
        File.Delete(Path.Combine(storage.LocalRoot, missing));
        var metadata = new Metadata();
        await FluentActions.Awaiting(() => new WindowsBundledRuntimeInstallationInspector(storage.LocalRoot, metadata)
            .InspectAsync(CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        metadata.Executables.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"runtimeTarget\":{\"name\":\".NETCoreApp,Version=v10.0/win-x86\"}}")]
    [InlineData("{\"runtimeTarget\":{\"name\":\".NETCoreApp,Version=v10.0/win-x64\"},\"targets\":{}}")]
    [InlineData("{\"runtimeTarget\":{\"name\":\"x/win-x64\"},\"targets\":{\"x/win-x64\":{\"test\":{\"native\":{\"missing.dll\":{}}}}}}")]
    [InlineData("{\"runtimeTarget\":{\"name\":\"x/win-x64\"},\"targets\":{\"x/win-x64\":{\"test\":{\"runtime\":{\"../pwsh.dll\":{}}}}}}")]
    public async Task Malformed_or_unknown_dependency_platform_is_not_accepted_as_exact_closure(string dependencies)
    {
        using var storage = new OwnedStorageFixture();
        Populate(storage.LocalRoot);
        await File.WriteAllTextAsync(Path.Combine(storage.LocalRoot, "pwsh.deps.json"), dependencies, TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => new WindowsBundledRuntimeInstallationInspector(storage.LocalRoot, new Metadata())
            .InspectAsync(CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("protection")]
    [InlineData("provenance")]
    public async Task Native_platform_ownership_acl_and_provenance_refusals_never_return_a_verified_inventory(string fault)
    {
        using var storage = new OwnedStorageFixture();
        Populate(storage.LocalRoot);
        var metadata = new Metadata { Fault = fault };
        await FluentActions.Awaiting(() => new WindowsBundledRuntimeInstallationInspector(storage.LocalRoot, metadata)
            .InspectAsync(CancellationToken.None)).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Missing_root_and_cancellation_remain_distinct_and_all_owned_file_handles_join()
    {
        using var storage = new OwnedStorageFixture();
        var inspector = new WindowsBundledRuntimeInstallationInspector(Path.Combine(storage.LocalRoot, "missing"), new Metadata());
        (await inspector.InspectAsync(CancellationToken.None)).Should().BeNull();
        Populate(storage.LocalRoot);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => new WindowsBundledRuntimeInstallationInspector(storage.LocalRoot, new Metadata())
            .InspectAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        // Exclusive opens after completion distinguish real handle disposal from just cancelling the wait.
        foreach (var file in Directory.EnumerateFiles(storage.LocalRoot, "*", SearchOption.AllDirectories))
        {
            using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    internal static void Populate(string root)
    {
        foreach (var name in new[] { "pwsh.exe", "pwsh.dll", "System.Management.Automation.dll",
            "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll" })
        { File.WriteAllText(Path.Combine(root, name), "Synthetic PE input; never executed", new UTF8Encoding(false)); }
        File.WriteAllText(Path.Combine(root, "pwsh.deps.json"),
            "{\"runtimeTarget\":{\"name\":\".NETCoreApp,Version=v10.0/win-x64\"},\"targets\":{\".NETCoreApp,Version=v10.0/win-x64\":{\"test\":{\"runtime\":{\"pwsh.dll\":{}},\"native\":{\"coreclr.dll\":{}}}}}}");
        File.WriteAllText(Path.Combine(root, "pwsh.runtimeconfig.json"), "{\"runtimeOptions\":{\"tfm\":\"net10.0\"}}");
        File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "Synthetic MIT notice");
        File.WriteAllText(Path.Combine(root, "ThirdPartyNotices.txt"), "Synthetic bundled notices");
        Directory.CreateDirectory(Path.Combine(root, "Modules"));
        File.WriteAllText(Path.Combine(root, "Modules", "shared.dll"), "Synthetic shared runtime dependency; never loaded");
    }

    internal sealed class Metadata : IBundledRuntimeNativeMetadata
    {
        internal List<string> Executables { get; } = [];
        internal string? Fault { get; init; }
        internal string Protection { get; set; } = "Synthetic independent protected native metadata";
        public void RequireSupportedPlatform()
        {
            if (Fault is "platform") { throw new PlatformNotSupportedException("Unknown test platform."); }
        }
        public string InspectProtection(string path, bool directory, bool ancestorOnly)
        {
            if (Fault is "protection") { throw new UnauthorizedAccessException("Synthetic bad owner/ACL/reparse observation."); }
            return Protection;
        }
        public string InspectExecutable(string path, FileStream input)
        {
            if (Fault is "provenance") { throw new InvalidDataException("Synthetic rejected signature/PE/version."); }
            Executables.Add(path);
            return "7.6.9.0";
        }
        public string DeviceDigest() => new('b', 64);
    }
}
