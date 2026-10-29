using System.Diagnostics;
using System.Security.AccessControl;

using AwesomeAssertions;

using Kora.Core.Customizations;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsCustomizationStoreTests
{
    [Fact]
    public async Task McpPreparationDoesNotCreateStorageAndAtomicSaveReadbackSurvivesReopen()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        Directory.Exists(root).Should().BeFalse();
        var saved = await Save(store, source, null, Primary(source));
        File.ReadAllText(source.Location).Should().Be("{\n  \"servers\": {\n\"example\": " + Primary(source).Text + "}\n}\n");
        Directory.Exists(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeFalse("one file has no package journal");
        var reopened = Create();
        var selected = (await reopened.List(TestContext.Current.CancellationToken)).Single();
        (await reopened.Read(selected, TestContext.Current.CancellationToken)).Revision.Should().Be(saved.Revision);
        selected.Location.Should().Be(Path.Combine(root, "roaming", "Mcp", "mcp.json"));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("final")]
    [InlineData("occupied")]
    [InlineData("cancel")]
    [InlineData("eligibility")]
    public async Task McpChangedBaseOccupancyAndRevocationNeverOverwrite(string failure)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var before = await Save(store, source, null, Primary(source));
        var replacement = new CustomizationFile(source.PrimaryFile, "{\"url\":\"https://changed.invalid/mcp\"}"u8);
        var originalBytes = File.ReadAllBytes(source.Location);
        var eligible = true;
        using var cancellation = new CancellationTokenSource();
        if (failure is "stale") { File.AppendAllText(source.Location, "\n"); }
        if (failure is "occupied")
        {
            source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "another", TestContext.Current.CancellationToken);
            before = null!;
            File.AppendAllText(source.Location, "\n");
        }
        if (failure is "final")
        {
            store.BeforeMcpFinalComparison = () =>
            {
                var other = Path.Combine(Path.GetDirectoryName(source.Location)!, "external.json");
                File.WriteAllBytes(other, [.. originalBytes, (byte)' ']);
                File.Move(other, source.Location, overwrite: true);
            };
        }
        if (failure is "cancel") { store.BeforeMcpFinalComparison = () => cancellation.Cancel(); }
        if (failure is "eligibility") { store.BeforeMcpFinalComparison = () => eligible = false; }
        Func<Task> save = () => store.Save(source, before, [replacement], () => eligible,
            operation => Task.FromResult(operation()), cancellation.Token);
        await save.Should().ThrowAsync<Exception>();
        var actual = File.ReadAllText(source.Location);
        actual.Should().NotContain("https://changed.invalid");
        actual.Should().Contain("https://example.invalid");
        Directory.EnumerateFiles(Path.GetDirectoryName(source.Location)!, ".customization-*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task McpMissingRootAppearingAfterPreparationIsNotAdopted()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(source.Location)!);
        Func<Task> save = () => Save(store, source, null, Primary(source));
        await save.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(source.Location).Should().BeFalse();
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("duplicate")]
    [InlineData("unknown-root")]
    [InlineData("malformed")]
    [InlineData("sensitive")]
    [InlineData("oversize")]
    public async Task McpUnsupportedSourcesAreNotExposedDowngradedOrTruncated(string failure)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        await Save(store, source, null, Primary(source));
        var content = failure switch
        {
            "unsupported" => "{\"servers\":{\"example\":{\"command\":\"private-process\"}}}",
            "duplicate" => "{\"servers\":{\"example\":{\"url\":\"https://private.invalid\",\"url\":\"https://second.invalid\"}}}",
            "unknown-root" => "{\"secret-root\":\"private\",\"servers\":{\"example\":{\"url\":\"https://private.invalid\"}}}",
            "sensitive" => "{\"servers\":{\"example\":{\"url\":\"https://private.invalid\",\"headers\":{\"Authorization\":\"private\"}}}}",
            "oversize" => new string(' ', 256 * 1024 + 1),
            _ => "{ invalid private",
        };
        File.WriteAllText(source.Location, content);
        Func<Task> read = () => store.Read(source, TestContext.Current.CancellationToken);
        var error = await read.Should().ThrowAsync<InvalidDataException>();
        error.Which.Message.Should().NotContain("private");
        File.ReadAllText(source.Location).Should().Be(content);
    }

    [Theory]
    [InlineData("readback")]
    [InlineData("readback-replacement")]
    [InlineData("post-commit-cancel")]
    [InlineData("readback-metadata")]
    public async Task McpUncertainReadbackNeverClaimsRollbackOrRetries(string failure)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var publications = 0;
        store.AfterComponentPublished = _ =>
        {
            publications++;
            if (failure is "post-commit-cancel") { cancellation.Cancel(); }
            else if (failure is "readback-metadata") { File.SetAttributes(source.Location, FileAttributes.NotContentIndexed); }
            else if (failure is "readback") { File.AppendAllText(source.Location, "\n"); }
            else
            {
                var copy = source.Location + ".external";
                File.Copy(source.Location, copy);
                File.Move(copy, source.Location, overwrite: true);
            }
        };
        Func<Task> save = () => store.Save(source, null, [Primary(source)], () => true,
            operation => Task.FromResult(operation()), cancellation.Token);
        (await save.Should().ThrowAsync<IOException>()).Which.Message.Should().Contain("unconfirmed").And.Contain("No rollback");
        publications.Should().Be(1);
        File.Exists(source.Location).Should().BeTrue();
        Func<Task> recovery = () => store.ReadRecovery(source, TestContext.Current.CancellationToken);
        await recovery.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("user")]
    [InlineData("name")]
    [InlineData("forged-source")]
    public async Task McpHostSelectsCanonicalDestinationAndRejectsOtherAuthority(string failure)
    {
        var store = Create();
        if (failure is "user" or "name")
        {
            Func<Task> prepare = () => store.Create(CustomizationKind.McpServer,
                failure is "user" ? CustomizationScope.User : CustomizationScope.Kora,
                failure is "name" ? "..\\escape" : "example", TestContext.Current.CancellationToken);
            await prepare.Should().ThrowAsync<InvalidDataException>();
            Directory.Exists(root).Should().BeFalse();
            return;
        }
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var forged = source with { Location = Path.Combine(root, "arbitrary.json") };
        Func<Task> save = () => Save(store, forged, null, Primary(forged));
        await save.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(forged.Location).Should().BeFalse();
    }

    [Theory]
    [InlineData("hardlink")]
    [InlineData("case-file")]
    [InlineData("case-parent")]
    [InlineData("junction")]
    [InlineData("stream")]
    public async Task McpAliasesReparseParentsAndUnsupportedStreamsNeverRetargetWrites(string state)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var before = await Save(store, source, null, Primary(source));
        var original = File.ReadAllBytes(source.Location);
        var directory = Path.GetDirectoryName(source.Location)!;
        if (state is "hardlink") { CreateHardLink(source.Location + ".alias", source.Location, nint.Zero).Should().BeTrue(); }
        else if (state is "stream") { File.WriteAllText(source.Location + ":private", "Unrelated"); }
        else if (state is "case-file")
        {
            File.Move(source.Location, source.Location + ".move");
            File.Move(source.Location + ".move", Path.Combine(directory, "MCP.json"));
        }
        else
        {
            Directory.Move(directory, directory + ".retired");
            if (state is "case-parent") { Directory.Move(directory + ".retired", Path.Combine(Path.GetDirectoryName(directory)!, "mcp")); }
            else
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe")
                {
                    Arguments = "/c mklink /J \"" + directory + "\" \"" + directory + ".retired\"",
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                })!;
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                process.ExitCode.Should().Be(0);
            }
        }
        try
        {
            Func<Task> read = () => store.Read(source, TestContext.Current.CancellationToken);
            await read.Should().ThrowAsync<InvalidDataException>();
            Func<Task> save = () => Save(store, source, before, Primary(source));
            await save.Should().ThrowAsync<InvalidDataException>();
            File.ReadAllBytes(source.Location).Should().Equal(original);
        }
        finally { if (state is "junction") { Directory.Delete(directory); } }
    }

    [Fact]
    public async Task McpSameBytesRestorationAndMissingBoundParentDoNotReviveTheReviewedBase()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var before = await Save(store, source, null, Primary(source));
        File.SetLastWriteTimeUtc(source.Location, File.GetLastWriteTimeUtc(source.Location).AddSeconds(2));
        Func<Task> stale = () => Save(store, source, before, Primary(source));
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var current = await store.Read(source, TestContext.Current.CancellationToken);
        var directory = Path.GetDirectoryName(source.Location)!;
        Directory.Move(directory, directory + ".retired");
        Func<Task> missing = () => Save(store, source, current, Primary(source));
        await missing.Should().ThrowAsync<InvalidOperationException>();
        Directory.Exists(directory).Should().BeFalse("a previously bound missing root must not be recreated");
    }

    [Theory]
    [InlineData("servers")]
    [InlineData("mcpServers")]
    public async Task McpNativePublicationPreservesBomUnrelatedSourceAndSecurityMetadata(string collection)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        await Save(store, source, null, Primary(source));
        const string entry = "{\"url\":\"https://old.invalid/mcp\"}";
        var text = "{/*keep*/\"" + collection + "\":{\"example\":" + entry
            + ", \"unrelated\":{\"command\":\"unchanged\",\"env\":{\"PRIVATE\":\"undisclosed\"}},}}\r\n";
        File.WriteAllBytes(source.Location, [.. System.Text.Encoding.UTF8.Preamble, .. System.Text.Encoding.UTF8.GetBytes(text)]);
        File.SetAttributes(source.Location, FileAttributes.Archive | FileAttributes.NotContentIndexed);
        var attributes = File.GetAttributes(source.Location);
        var sections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;
        var security = new FileInfo(source.Location).GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections);
        var before = await store.Read(source, TestContext.Current.CancellationToken);
        const string changed = "{\"url\":\"https://changed.invalid/mcp\",\"type\":\"http\"}";
        await Save(store, source, before, new CustomizationFile(source.PrimaryFile, System.Text.Encoding.UTF8.GetBytes(changed)));
        File.ReadAllBytes(source.Location).Should().Equal(
            [.. System.Text.Encoding.UTF8.Preamble, .. System.Text.Encoding.UTF8.GetBytes(text.Replace(entry, changed, StringComparison.Ordinal))]);
        File.GetAttributes(source.Location).Should().Be(attributes);
        new FileInfo(source.Location).GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections).Should().Be(security);
    }

    [Fact]
    public async Task McpFinalCreateRaceNeverOverwritesAnOccupant()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        const string occupant = "{\"servers\":{\"other\":{\"url\":\"https://external.invalid\"}}}";
        store.AfterMcpFinalComparison = () => File.WriteAllText(source.Location, occupant);
        Func<Task> save = () => Save(store, source, null, Primary(source));
        await save.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllText(source.Location).Should().Be(occupant);
    }

    [Fact]
    public async Task McpAcceptedOptimisticSameNameRaceRemainsWholeFilePublicationNotCompareAndSwap()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var before = await Save(store, source, null, Primary(source));
        var original = File.ReadAllBytes(source.Location);
        store.AfterMcpFinalComparison = () =>
        {
            File.Move(source.Location, source.Location + ".retired");
            File.WriteAllText(source.Location, "{\"servers\":{\"raced\":{\"url\":\"https://external.invalid\"}}}");
        };
        var proposal = new CustomizationFile(source.PrimaryFile, "{\"url\":\"https://changed.invalid\"}"u8);
        (await Save(store, source, before, proposal)).Files.Single().CopyBytes().Should().Equal(proposal.CopyBytes());
        File.ReadAllBytes(source.Location + ".retired").Should().Equal(original);
    }
}
