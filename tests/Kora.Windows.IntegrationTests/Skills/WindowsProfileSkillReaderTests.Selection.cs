using System.Text;
using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.Skills;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed partial class WindowsProfileSkillReaderTests
{
    [Fact]
    public async Task SelectedBindingReadsNoGuidanceAndExactReadNeverInspectsOtherPackageBodies()
    {
        var token = TestContext.Current.CancellationToken;
        var other = Path.Combine(SourceRoot, "unrelated", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllBytes(other, new byte[SharedSkillSnapshot.MaximumBytes + 1]);
        var reader = new WindowsProfileSkillReader(Profile);
        var source = await reader.SelectAsync(SourceRoot, token);
        File.WriteAllBytes(SkillFile, [0xff]);
        var invalidUtf8 = await reader.BindFileAsync([source], SkillFile, token);
        invalidUtf8.Source.Should().Be(source);
        invalidUtf8.RelativeFile.Should().Be("example\\SKILL.md");
        (await reader.ReadFileAsync(invalidUtf8, token)).IsInstructionCompatible.Should().BeFalse();
        File.WriteAllText(SkillFile, Text, new UTF8Encoding(false));
        var bound = await reader.BindFileAsync([source], SkillFile, token);
        var snapshot = await reader.ReadFileAsync(bound, token);
        snapshot.Text.Should().Be(Text);
        snapshot.PackageDirectoryIdentity.Should().Be(bound.PackageIdentity);
        snapshot.IsInstructionCompatible.Should().BeTrue();
        File.ReadAllText(SkillFile).Should().Be(Text);
        new FileInfo(other).Length.Should().Be(SharedSkillSnapshot.MaximumBytes + 1);
    }

    [Theory]
    [InlineData("unregistered")]
    [InlineData("ambiguous")]
    [InlineData("noncanonical")]
    [InlineData("nested")]
    [InlineData("other-file")]
    [InlineData("source")]
    [InlineData("cancel")]
    public async Task NativeSelectedBindingRejectsForeignAliasedOrNonSingleFileSources(string fault)
    {
        var reader = new WindowsProfileSkillReader(Profile);
        var token = TestContext.Current.CancellationToken;
        var source = await reader.SelectAsync(SourceRoot, token);
        IReadOnlyList<SharedSkillSource> registered = fault is "unregistered" ? [] : fault is "ambiguous"
            ? [source, source with { }] : [source];
        var file = SkillFile;
        if (fault is "noncanonical")
        {
            File.Move(SkillFile, Path.Combine(Path.GetDirectoryName(SkillFile)!, "skill.md"));
            file = Path.Combine(Path.GetDirectoryName(SkillFile)!, "skill.md");
        }
        if (fault is "nested")
        {
            file = Path.Combine(Path.GetDirectoryName(SkillFile)!, "nested", "SKILL.md");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Text);
        }
        if (fault is "other-file") { File.WriteAllText(Path.Combine(Path.GetDirectoryName(SkillFile)!, "helper.ps1"), "inert"); }
        if (fault is "source") { source = new(source.Id, source.ProfileRelativeRoot, new string('b', 48)); registered = [source]; }
        using var cancellation = new CancellationTokenSource();
        if (fault is "cancel") { cancellation.Cancel(); }
        await ((Func<Task>)(async () => await reader.BindFileAsync(registered, file, cancellation.Token))).Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("restored")]
    [InlineData("replacement")]
    [InlineData("package")]
    [InlineData("source")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("cancel")]
    public async Task ExactSelectedReadRejectsRestoredBytesAndChangedNativeRevisionOrInventory(string fault)
    {
        var reader = new WindowsProfileSkillReader(Profile);
        var token = TestContext.Current.CancellationToken;
        var source = await reader.SelectAsync(SourceRoot, token);
        var binding = await reader.BindFileAsync([source], SkillFile, token);
        if (fault is "bytes") { File.AppendAllText(SkillFile, "\nchanged"); }
        if (fault is "restored")
        {
            File.WriteAllText(SkillFile, Text);
            File.SetLastWriteTimeUtc(SkillFile, new DateTime(binding.LastWriteTicks, DateTimeKind.Utc).AddSeconds(1));
        }
        if (fault is "replacement") { File.Move(SkillFile, SkillFile + ".old"); File.WriteAllText(SkillFile, Text); File.Delete(SkillFile + ".old"); }
        if (fault is "package")
        {
            Directory.Move(Path.GetDirectoryName(SkillFile)!, Path.GetDirectoryName(SkillFile)! + "-old");
            Directory.CreateDirectory(Path.GetDirectoryName(SkillFile)!);
            File.WriteAllText(SkillFile, Text);
        }
        if (fault is "source") { Directory.Move(SourceRoot, SourceRoot + "-old"); Directory.CreateDirectory(SourceRoot); }
        if (fault is "extra") { Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(SkillFile)!, "references")); }
        if (fault is "empty" or "oversized")
        {
            File.WriteAllBytes(SkillFile, new byte[fault is "empty" ? 0 : SharedSkillSnapshot.MaximumBytes + 1]);
            binding = await reader.BindFileAsync([source], SkillFile, token);
        }
        using var cancellation = new CancellationTokenSource();
        if (fault is "cancel") { cancellation.Cancel(); }
        await ((Func<Task>)(async () => await reader.ReadFileAsync(binding, cancellation.Token))).Should().ThrowAsync<Exception>();
    }
}
