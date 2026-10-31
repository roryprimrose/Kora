using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsCustomizationStoreTests
{
    [Theory]
    [InlineData("held-source")]
    [InlineData("late-inventory")]
    [InlineData("late-destination")]
    [InlineData("stale")]
    public async Task SourceBoundSkillCopyPinsTheOriginalAndRechecksCompleteInventoryAtAtomicCreate(string state)
    {
        var token = TestContext.Current.CancellationToken;
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "original", token);
        var originalBytes = ArtifactFileDocument.Encode("---\nname: original\nversion: 1.0.0\ndescription: Review prose\n---\nOriginal credit and plain guidance.", false);
        var original = await Save(store, source, null, new CustomizationFile("SKILL.md", originalBytes));
        var destination = await store.Create(CustomizationKind.Skill, CustomizationScope.Kora, "adapted", token);
        var proposal = DeclarativeSkillAdaptation.Create(original, destination);
        var finalChecks = 0;
        store.BeforeCopyPublication = () =>
        {
            finalChecks++;
            if (state is "held-source")
            {
                ((Action)(() => File.AppendAllText(source.Location, "Must not change under publication pin.")))
                    .Should().Throw<IOException>();
                ((Action)(() => File.Move(source.Location, source.Location + ".replaced")))
                    .Should().Throw<IOException>();
            }
            if (state is "late-inventory")
            { File.WriteAllText(Path.Combine(Path.GetDirectoryName(source.Location)!, "notes.md"), "New unsupported requirement."); }
            if (state is "late-destination") { File.WriteAllText(destination.Location, "Unrelated destination, never overwrite."); }
        };
        if (state is "stale") { File.AppendAllText(source.Location, "External change before native admission."); }
        var publish = () => store.Save(destination, null, [proposal.Proposed], () => true,
            operation => Task.FromResult(operation()), token, copiedFrom: original);
        if (state is "held-source")
        {
            var saved = await publish();
            finalChecks.Should().Be(1);
            saved.Files.Single().Text.Should().Be(proposal.Proposed.Text);
            (await store.Read(destination, token)).Revision.Should().Be(saved.Revision);
            File.AppendAllText(source.Location, "Explicit external edit after the source pin was disposed.");
            File.ReadAllText(source.Location).Should().EndWith("Explicit external edit after the source pin was disposed.");
        }
        else
        {
            await publish.Should().ThrowAsync<Exception>();
            finalChecks.Should().Be(state is "stale" ? 0 : 1);
            if (state is "late-destination")
            { File.ReadAllText(destination.Location).Should().Be("Unrelated destination, never overwrite."); }
            else { File.Exists(destination.Location).Should().BeFalse(); }
        }
        if (state is not ("held-source" or "stale"))
        { File.ReadAllBytes(source.Location).Should().Equal(originalBytes); }
    }

    [Theory]
    [InlineData("overwrite")]
    [InlineData("destination")]
    [InlineData("kind")]
    [InlineData("files")]
    [InlineData("source-scope")]
    [InlineData("source-kind")]
    [InlineData("source-files")]
    [InlineData("source-primary")]
    [InlineData("recovery")]
    [InlineData("same")]
    public async Task NativeSourceBoundCopyCannotBeRetargetedToAnotherMutation(string state)
    {
        var token = TestContext.Current.CancellationToken;
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "original", token);
        var original = await Save(store, source, null, Primary(source));
        var destination = await store.Create(CustomizationKind.Skill, CustomizationScope.Kora, "adapted", token);
        var originalSource = original.Source;
        IReadOnlyList<CustomizationFile> originalFiles = original.Files;
        CustomizationSnapshot? expected = null;
        CustomizationFile[] files = [Primary(destination)];
        switch (state)
        {
            case "overwrite": expected = original; break;
            case "destination": destination = destination with { Scope = CustomizationScope.User }; break;
            case "kind": destination = destination with { Kind = CustomizationKind.Agent }; break;
            case "files": files = [.. files, new("notes.md", "Never copy"u8)]; break;
            case "source-scope": originalSource = originalSource with { Scope = CustomizationScope.Kora }; break;
            case "source-kind": originalSource = originalSource with { Kind = CustomizationKind.Agent }; break;
            case "source-files": originalFiles = [.. originalFiles, new("notes.md", "Never copy"u8)]; break;
            case "source-primary": originalSource = originalSource with { PrimaryFile = "skill.md" }; originalFiles = [new("skill.md", originalFiles[0].CopyBytes())]; break;
            case "same": originalSource = originalSource with { Name = destination.Name }; break;
        }
        var copiedFrom = new CustomizationSnapshot(originalSource, original.Revision, originalFiles, state is "recovery" ? "Pending" : null);
        var publish = () => store.Save(destination, expected, files, () => true,
            operation => Task.FromResult(operation()), token, copiedFrom);
        await publish.Should().ThrowAsync<Exception>();
        File.Exists(destination.Location).Should().BeFalse();
    }
}
