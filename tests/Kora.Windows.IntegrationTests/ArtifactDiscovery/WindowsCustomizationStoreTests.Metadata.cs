using System.Text;

using AwesomeAssertions;

using Kora.Core.Customizations;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsCustomizationStoreTests
{
    [Theory]
    [InlineData(CustomizationKind.Skill, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.Agent, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.Instruction, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.Skill, CustomizationScope.User)]
    [InlineData(CustomizationKind.Agent, CustomizationScope.User)]
    [InlineData(CustomizationKind.Instruction, CustomizationScope.User)]
    public async Task MetadataReadsOnlyChosenPrimaryAndPinsCurrentNativeIdentityWithoutPackageComponents(
        CustomizationKind kind, CustomizationScope scope)
    {
        var token = TestContext.Current.CancellationToken;
        var store = Create();
        CustomizationSource source;
        if (kind == CustomizationKind.Instruction && scope == CustomizationScope.User)
        {
            await store.Invoking(value => value.Create(kind, scope, "example", token))
                .Should().ThrowAsync<InvalidOperationException>();
            source = await Original(store, Encoding.UTF8.GetBytes(
                CustomizationLayout.Template(kind, "example") + "\nBODY-SECRET"));
            store.IsInstructionRegistered(source).Should().BeFalse();
        }
        else
        {
            source = await store.Create(kind, scope, "example", token);
            await Save(store, source, null, Primary(source, "\nBODY-SECRET"));
        }
        if (kind == CustomizationKind.Skill)
        {
            var component = Path.Combine(Path.GetDirectoryName(source.Location)!, "never-read.bin");
            await File.WriteAllBytesAsync(component, [0, 255, 0], token);
        }
        var mcp = Path.Combine(root, "roaming", "Mcp");
        Directory.CreateDirectory(mcp);
        await File.WriteAllTextAsync(Path.Combine(mcp, "mcp.json"), "{not-valid-MCP-config", token);
        var before = await File.ReadAllBytesAsync(source.Location, token);
        var listed = await store.ListMetadataSources(token);
        listed.Should().ContainSingle();
        listed[0].Should().Be(source);
        var first = await store.ReadMetadata(listed[0], token);
        first.Name.Should().Be("example");
        first.Description.Should().NotContain("BODY-SECRET");
        first.Source.Should().Be(source);
        (await store.ReadMetadata(source, token)).Should().Be(first);
        if (kind == CustomizationKind.Instruction && scope == CustomizationScope.User)
        {
            store.IsInstructionRegistered(source).Should().BeFalse("metadata review cannot grant edit authority");
            await store.Invoking(value => value.Read(source, token)).Should().ThrowAsync<InvalidOperationException>();
            Directory.Exists(Path.Combine(root, "roaming", "Instructions")).Should().BeFalse();
            Directory.Exists(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeFalse();
        }
        await File.WriteAllTextAsync(source.Location, Encoding.UTF8.GetString(before) + "\nSource changed.", token);
        var changed = await store.ReadMetadata(source, token);
        changed.Revision.Should().NotBe(first.Revision);
        changed.Digest.Should().NotBe(first.Digest);
        changed.Name.Should().Be(first.Name);
        await File.WriteAllBytesAsync(source.Location, before, token);
        File.SetLastWriteTimeUtc(source.Location, DateTime.UtcNow.AddSeconds(2));
        var restored = await store.ReadMetadata(source, token);
        restored.Digest.Should().Be(first.Digest);
        restored.Revision.Should().NotBe(first.Revision);
        File.Delete(source.Location);
        await store.Invoking(value => value.ReadMetadata(source, token)).Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task UnknownUnsupportedMalformedAndInterruptedSourcesCannotBecomeSuccessfulCurrentMetadata()
    {
        var token = TestContext.Current.CancellationToken;
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.Kora, "example", token);
        await Save(store, source, null, Primary(source));
        await store.Invoking(value => value.ReadMetadata(source with { Kind = CustomizationKind.McpServer }, token))
            .Should().ThrowAsync<InvalidDataException>();
        await store.Invoking(value => value.ReadMetadata(source with { UnavailableReason = "revoked" }, token))
            .Should().ThrowAsync<InvalidDataException>();
        await store.Invoking(value => value.ReadMetadata(source with { Location = "Q:\\forged" }, token))
            .Should().ThrowAsync<InvalidOperationException>();
        await File.WriteAllTextAsync(source.Location, "Not scalar metadata", token);
        await store.Invoking(value => value.ReadMetadata(source, token)).Should().ThrowAsync<InvalidDataException>();
        await File.WriteAllBytesAsync(source.Location, Primary(source).CopyBytes(), token);
        var original = await store.Read(source, token);
        store.AfterComponentPublished = _ => throw new IOException("interrupted");
        await ((Func<Task>)(() => Save(store, source, original, Primary(source, "changed")))).Should().ThrowAsync<IOException>();
        await store.Invoking(value => value.ReadMetadata(source, token)).Should().ThrowAsync<InvalidOperationException>();
    }
}
