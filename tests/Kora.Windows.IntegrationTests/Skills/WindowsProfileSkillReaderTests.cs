using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Skills;
using Kora.Windows.Skills;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed partial class WindowsProfileSkillReaderTests : IDisposable
{
    private const string Text = "---\nname: profile-example\nversion: 1.0.0\ndescription: Read only example\n---\n# Instructions\nExplain without tools.";
    private readonly string root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "shared-profile-test-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(root, "Profile");
    private string SourceRoot => Path.Combine(Profile, ".agents", "skills");
    private string SkillFile => Path.Combine(SourceRoot, "example", "SKILL.md");

    public WindowsProfileSkillReaderTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SkillFile)!);
        File.WriteAllText(SkillFile, Text, new UTF8Encoding(false));
    }

    [WindowsFact]
    public async Task Selected_exact_profile_root_is_source_qualified_and_snapshot_reads_never_modify_sources()
    {
        var reader = new WindowsProfileSkillReader(Profile);
        var before = File.ReadAllBytes(SkillFile);
        var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        source.ProfileRelativeRoot.Should().Be(".agents\\skills");
        source.DirectoryIdentity.Should().HaveLength(48);
        var catalogue = await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
        catalogue.Packages.Single().Text.Should().Be(Text);
        catalogue.Packages.Single().Bytes.ToArray().Should().Equal(before);
        catalogue.Packages.Single().IsInstructionCompatible.Should().BeTrue();
        File.ReadAllBytes(SkillFile).Should().Equal(before);
        Directory.GetFiles(SourceRoot, "*", SearchOption.AllDirectories).Should().ContainSingle().Which.Should().Be(SkillFile);
        File.WriteAllText(SkillFile, Text + "\nchanged");
        var changed = await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
        changed.Packages.Single().RevisionDigest.Should().NotBe(catalogue.Packages.Single().RevisionDigest);
        catalogue.Packages.Single().Text.Should().Be(Text);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("profile-parent")]
    [InlineData("escape")]
    [InlineData("traversal")]
    [InlineData("slash")]
    [InlineData("device")]
    [InlineData("network")]
    [InlineData("stream")]
    public async Task Whole_profile_external_device_and_aliased_paths_are_rejected_before_discovery(string kind)
    {
        var path = kind switch
        {
            "profile" => Profile,
            "profile-parent" => Path.Combine(Profile, ".agents"),
            "escape" => root,
            "traversal" => Path.Combine(SourceRoot, "..", "skills"),
            "slash" => SourceRoot.Replace('\\', '/'),
            "device" => "\\\\?\\" + SourceRoot,
            "network" => "\\\\example.invalid\\share",
            _ => SourceRoot + ":stream",
        };
        var action = async () => await new WindowsProfileSkillReader(Profile).SelectAsync(path, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Scripts_and_other_entries_are_disclosed_but_never_read_or_executed()
    {
        var script = Path.Combine(Path.GetDirectoryName(SkillFile)!, "run.ps1");
        File.WriteAllBytes(script, [0xff, 0xfe, 0, 0]);
        var reader = new WindowsProfileSkillReader(Profile);
        var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        var catalogue = await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
        var snapshot = catalogue.Packages.Single();
        snapshot.UnavailableReasons.Should().Contain("additional-package-files-not-supported");
        snapshot.UninspectedFiles.Should().Contain("example\\run.ps1");
        snapshot.IsInstructionCompatible.Should().BeFalse();
        File.ReadAllBytes(script).Should().Equal(0xff, 0xfe, 0, 0);
    }

    [WindowsFact]
    public async Task Invalid_utf8_and_noncanonical_case_remain_visible_unavailable_rows()
    {
        File.Delete(SkillFile);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(SkillFile)!, "skill.md"), [0xff]);
        var reader = new WindowsProfileSkillReader(Profile);
        var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        var snapshot = (await reader.DiscoverAsync(source, TestContext.Current.CancellationToken)).Packages.Single();
        snapshot.UnavailableReasons.Should().Contain("invalid-utf8").And.Contain("noncanonical-skill-file-name");
        snapshot.Text.Should().BeNull();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("bytes")]
    [InlineData("entries")]
    [InlineData("depth")]
    [InlineData("packages")]
    [InlineData("total-bytes")]
    public async Task Source_limits_fail_closed_without_a_partial_or_successful_catalogue(string limit)
    {
        var reader = new WindowsProfileSkillReader(Profile);
        var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        if (limit is "empty") { File.WriteAllBytes(SkillFile, []); }
        if (limit is "bytes") { File.WriteAllBytes(SkillFile, new byte[SharedSkillSnapshot.MaximumBytes + 1]); }
        if (limit is "entries")
        {
            for (var index = 0; index < SharedSkillCatalogue.MaximumEntries; index++)
            { File.WriteAllText(Path.Combine(SourceRoot, $"file-{index}.txt"), "ignored"); }
        }
        if (limit is "depth") { Directory.CreateDirectory(Path.Combine(SourceRoot, "a", "b", "c", "d", "e")); }
        if (limit is "packages")
        {
            for (var index = 0; index < SharedSkillCatalogue.MaximumPackages; index++)
            {
                var folder = Path.Combine(SourceRoot, $"skill-{index}");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SKILL.md"), Text);
            }
        }
        if (limit is "total-bytes")
        {
            for (var index = 0; index < 17; index++)
            {
                var folder = Path.Combine(SourceRoot, $"skill-{index}");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "SKILL.md"), new byte[SharedSkillSnapshot.MaximumBytes]);
            }
        }
        var action = async () => await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<SharedSkillUnavailableException>();
    }

    [WindowsFact]
    public async Task Removed_replaced_and_busy_sources_are_not_silently_rebound()
    {
        var reader = new WindowsProfileSkillReader(Profile);
        var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        Directory.Move(SourceRoot, SourceRoot + "-old");
        var missing = async () => await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
        (await missing.Should().ThrowAsync<SharedSkillUnavailableException>()).Which.ReasonCode.Should().Be("source-missing");
        Directory.CreateDirectory(SourceRoot);
        var replaced = async () => await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
        (await replaced.Should().ThrowAsync<SharedSkillUnavailableException>()).Which.ReasonCode.Should().Be("registered-source-replaced");
        Directory.Delete(SourceRoot);
        Directory.Move(SourceRoot + "-old", SourceRoot);
        using var writing = new FileStream(SkillFile, FileMode.Open, FileAccess.Write, FileShare.Read);
        var busy = async () => await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
        (await busy.Should().ThrowAsync<SharedSkillUnavailableException>()).Which.ReasonCode.Should().Be("source-unreadable-or-busy");
    }

    [WindowsFact]
    public async Task Pinned_ancestor_and_file_handles_deny_replacement_and_mutation_races()
    {
        var reader = new WindowsProfileSkillReader(Profile, () =>
        {
            ((Action)(() => Directory.Move(SourceRoot, SourceRoot + "-moved"))).Should().Throw<IOException>();
            ((Action)(() => File.Move(SkillFile, SkillFile + ".moved"))).Should().Throw<IOException>();
            ((Action)(() => File.WriteAllText(SkillFile, "replacement"))).Should().Throw<IOException>();
        });
        var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        var snapshot = (await reader.DiscoverAsync(source, TestContext.Current.CancellationToken)).Packages.Single();
        snapshot.Text.Should().Be(Text);
        File.Move(SkillFile, SkillFile + ".moved");
        File.Move(SkillFile + ".moved", SkillFile);
    }

    [WindowsFact]
    public async Task Cancellation_before_or_after_inventory_releases_all_pinned_resources()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new WindowsProfileSkillReader(Profile, cancellation.Cancel);
        var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        var action = async () => await reader.DiscoverAsync(source, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        var selection = async () => await reader.SelectAsync(SourceRoot, cancellation.Token);
        await selection.Should().ThrowAsync<OperationCanceledException>();
        Directory.Move(SourceRoot, SourceRoot + "-moved");
        Directory.Move(SourceRoot + "-moved", SourceRoot);
    }

    [WindowsFact]
    public async Task Junction_and_hardlink_packages_are_never_followed()
    {
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "SKILL.md"), "external-content-must-not-be-read");
        var junction = Path.Combine(SourceRoot, "linked");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/d /c mklink /J \"{junction}\" \"{outside}\"",
        };
        using (var process = Process.Start(start)!)
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(0);
        }
        try
        {
            var reader = new WindowsProfileSkillReader(Profile);
            var source = await reader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
            var action = async () => await reader.DiscoverAsync(source, TestContext.Current.CancellationToken);
            (await action.Should().ThrowAsync<SharedSkillUnavailableException>()).Which.ReasonCode
                .Should().Be("link-reparse-alias-or-entry-type");
        }
        finally { Directory.Delete(junction); }
        var hardlink = Path.Combine(root, "hardlink.md");
        CreateHardLink(hardlink, SkillFile, IntPtr.Zero).Should().BeTrue();
        var hardReader = new WindowsProfileSkillReader(Profile);
        var hardSource = await hardReader.SelectAsync(SourceRoot, TestContext.Current.CancellationToken);
        var hardAction = async () => await hardReader.DiscoverAsync(hardSource, TestContext.Current.CancellationToken);
        (await hardAction.Should().ThrowAsync<SharedSkillUnavailableException>()).Which.ReasonCode
            .Should().Be("link-reparse-alias-or-entry-type");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string path, string existing, IntPtr security);
}
