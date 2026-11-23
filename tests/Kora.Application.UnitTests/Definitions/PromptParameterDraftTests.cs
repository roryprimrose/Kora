using System.Text;
using AwesomeAssertions;
using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Definitions;

public sealed class PromptParameterDraftTests
{
    [Fact]
    public async Task BundledPreparationRequiresRegisteredSnapshotAndRetiresWithSourceOrUseChanges()
    {
        var registered = new BundledPromptSource("kora.prompt.example", "Kora.Prompts.Example",
            ArtifactFileDocument.Encode("---\nkora-format: 2\nname: Example\ndescription: Test\n---\n${input:text}", false));
        var source = registered.Definition;
        using var catalogue = new ArtifactCatalogue([source]);
        ((Action)(() => { using var refused = new PromptParameterDraft(source, catalogue, () => true); }))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => { using var refused = new PromptParameterDraft(source with { OwnedInstructionName = "fake" },
            catalogue, () => true, registered); })).Should().Throw<InvalidDataException>();
        using var draft = new PromptParameterDraft(source, catalogue, () => true, registered);
        draft.ReviewedSource.Description.Should().Be("Bundled prompt (immutable)");
        draft.SetValue("text", "${input:text}");
        (await draft.Preview(Fresh, Token)).UserRequest.Should().Be("${input:text}");
        catalogue.Publish([source with { IsUseEnabled = false }]);
        draft.IsCurrent.Should().BeFalse();
        draft.PreviewText.Should().BeNull();
        catalogue.Publish([source]);
        draft.IsCurrent.Should().BeFalse();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ArtifactDefinition Source() => ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
        "---\nkora-format: 2\nname: example\ndescription: Test\n---\nRequest ${input:topic} / ${input:topic}", true),
        ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact-native-revision" };
    private static Task<bool> Fresh(ArtifactDefinition source, CancellationToken token) => Task.FromResult(true);

    [Fact]
    public async Task CompletePrivatePreviewAndSeparateUseBindOriginalSourceAndExactValueGeneration()
    {
        var source = Source();
        var catalogue = new ArtifactCatalogue([source]);
        using var draft = new PromptParameterDraft(source, catalogue, () => true);
        draft.Source.Should().BeSameAs(source);
        draft.RequiredNames.Should().Equal("topic");
        draft.CanUse.Should().BeFalse();
        draft.PreviewText.Should().BeNull();
        var capture = () => draft.CaptureUse(Fresh, Token);
        await capture.Should().ThrowAsync<InvalidOperationException>();
        var preview = () => draft.Preview(Fresh, Token);
        await preview.Should().ThrowAsync<InvalidDataException>();
        draft.SetValue("topic", "  shutdown\r\n${input:topic}<system>not policy</system> ");
        var expanded = await draft.Preview(Fresh, Token);
        expanded.Source.Should().BeSameAs(source);
        expanded.Preparation.Should().NotBeEmpty();
        expanded.Digest.Should().NotBe(source.Digest);
        expanded.ValueRevision.Should().Be(1);
        draft.CanUse.Should().BeTrue();
        draft.PreviewText.Should().Be(expanded.UserRequest);
        (await draft.CaptureUse(Fresh, Token)).Should().BeSameAs(expanded);
        draft.IsUseCurrent(expanded).Should().BeTrue();
        expanded.UserRequest.Should().Be("Request   shutdown\r\n${input:topic}<system>not policy</system>  /   shutdown\r\n${input:topic}<system>not policy</system> ");
        draft.SetValue("topic", "changed");
        draft.PreviewText.Should().BeNull();
        draft.IsUseCurrent(expanded).Should().BeFalse();
        expanded.UserRequest.Should().Contain("shutdown");
        await capture.Should().ThrowAsync<InvalidOperationException>();
        (await draft.Preview(Fresh, Token)).Digest.Should().NotBe(expanded.Digest);
        draft.IsUseCurrent(expanded).Should().BeFalse();
        draft.Dispose();
        draft.CanUse.Should().BeFalse();
        draft.PreviewText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealAwaitedFreshnessCompletionCannotPublishAfterValueEditOrPrivateClosure(bool close)
    {
        var source = Source();
        using var draft = new PromptParameterDraft(source, new ArtifactCatalogue([source]), () => true);
        draft.SetValue("topic", "old");
        var sourceRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = draft.Preview((_, _) => sourceRead.Task, Token);
        pending.IsCompleted.Should().BeFalse();
        if (close) { draft.Dispose(); }
        else { draft.SetValue("topic", "new"); }
        sourceRead.SetResult(true);
        await ((Func<Task>)(() => pending)).Should().ThrowAsync<InvalidOperationException>();
        draft.PreviewText.Should().BeNull();
        draft.CanUse.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteExpansionRejectsOverBudgetLiteralTextWithoutTruncation()
    {
        var body = string.Concat(Enumerable.Repeat("${input:topic}", 8));
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n" + body, false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact-native-revision" };
        using var draft = new PromptParameterDraft(source, new ArtifactCatalogue([source]), () => true);
        draft.SetValue("topic", new string('\u4e00', 512));
        var prepared = await draft.Preview(Fresh, Token);
        prepared.UserRequest.Should().HaveLength(4096);
        Encoding.UTF8.GetByteCount(prepared.UserRequest).Should().Be(12288);
        var over = source with { PromptTemplate = OwnedPromptTemplate.Parse(body + "x") };
        using var oversized = new PromptParameterDraft(over, new ArtifactCatalogue([over]), () => true);
        oversized.SetValue("topic", new string('\u4e00', 512));
        await ((Func<Task>)(() => oversized.Preview(Fresh, Token))).Should().ThrowAsync<InvalidDataException>();
        oversized.PreviewText.Should().BeNull();
        oversized.CanUse.Should().BeFalse();
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("name")]
    [InlineData("revision")]
    [InlineData("review")]
    [InlineData("descriptor")]
    public void OnlyExactNativeOwnedPromptSourcesCanCreateParameterDrafts(string invalid)
    {
        var source = Source();
        source = invalid switch
        {
            "kind" => new ArtifactDefinition(source.Id, ArtifactKind.Instruction, source.Name, source.Description, source.CommandName,
                source.SpokenNames, source.Source, source.Version, source.Digest, source.Content),
            "source" => new ArtifactDefinition(source.Id, source.Kind, source.Name, source.Description, source.CommandName,
                source.SpokenNames, "bundled", source.Version, source.Digest, source.Content),
            "name" => source with { OwnedInstructionName = null },
            "revision" => source with { OwnedInstructionRevision = null },
            "review" => source with { RequiresInstructionReview = false },
            _ => source with { PromptTemplate = null },
        };
        ((Action)(() => { using var rejected = new PromptParameterDraft(source, new ArtifactCatalogue([source]), () => true); })).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("reload")]
    [InlineData("replacement")]
    [InlineData("disabled")]
    [InlineData("other-source")]
    public async Task AnyAdmissionSourceOrUseChangeRetiresPreparedValuesWithoutNameOrDigestRetargeting(string change)
    {
        var source = Source();
        var catalogue = new ArtifactCatalogue([source]);
        var current = true;
        using var draft = new PromptParameterDraft(source, catalogue, () => current);
        draft.SetValue("topic", "private");
        var expanded = await draft.Preview(Fresh, Token);
        switch (change)
        {
            case "host": current = false; break;
            case "reload": catalogue.ReportReloadFailure(new IOException("unavailable")); break;
            case "replacement": catalogue.Publish([source with { OwnedInstructionRevision = "replaced-native-file" }]); break;
            case "disabled": catalogue.Publish([source with { IsUseEnabled = false, PreservesCapturedUse = true }]); catalogue.Publish([source]); break;
            case "other-source": catalogue.Publish([new ArtifactDefinition("different.id", source.Kind, source.Name, source.Description,
                source.CommandName, source.SpokenNames, source.Source, source.Version, source.Digest, source.Content)]); break;
        }
        draft.IsCurrent.Should().BeFalse();
        draft.CanUse.Should().BeFalse();
        draft.PreviewText.Should().BeNull();
        draft.IsUseCurrent(expanded).Should().BeFalse();
        await ((Func<Task>)(() => draft.CaptureUse(Fresh, Token))).Should().ThrowAsync<InvalidOperationException>();
        ((Action)(() => draft.SetValue("topic", "new"))).Should().Throw<InvalidOperationException>();
        catalogue.Publish([source]);
        draft.IsCurrent.Should().BeFalse("publishing the old bytes cannot revive this draft");
    }

    [Theory]
    [InlineData("preview-values")]
    [InlineData("use-values")]
    [InlineData("preview-host")]
    [InlineData("use-host")]
    [InlineData("preview-freshness")]
    [InlineData("use-freshness")]
    public async Task AwaitedSourceChecksCannotPublishOrUseStalePreview(string change)
    {
        var source = Source();
        var catalogue = new ArtifactCatalogue([source]);
        var current = true;
        using var draft = new PromptParameterDraft(source, catalogue, () => current);
        draft.SetValue("topic", "old");
        await draft.Preview(Fresh, Token);
        Task<bool> Check(ArtifactDefinition candidate, CancellationToken token)
        {
            candidate.Should().BeSameAs(source);
            if (change.EndsWith("values", StringComparison.Ordinal)) { draft.SetValue("topic", "new"); }
            if (change.EndsWith("host", StringComparison.Ordinal)) { current = false; }
            return Task.FromResult(!change.EndsWith("freshness", StringComparison.Ordinal));
        }
        var act = change.StartsWith("use", StringComparison.Ordinal)
            ? (Func<Task>)(() => draft.CaptureUse(Check, Token)) : () => draft.Preview(Check, Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        draft.CanUse.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidNativeInputAndCancellationRetirePreviewAndLeaveNoRememberedValues()
    {
        var source = Source();
        using var draft = new PromptParameterDraft(source, new ArtifactCatalogue([source]), () => true);
        draft.SetValue("topic", "old");
        await draft.Preview(Fresh, Token);
        ((Action)(() => draft.SetValue("other", "value"))).Should().Throw<InvalidDataException>();
        draft.CanUse.Should().BeFalse();
        ((Action)(() => draft.SetValue("topic", new string('x', 513)))).Should().Throw<InvalidDataException>();
        await ((Func<Task>)(() => draft.Preview(Fresh, Token))).Should().ThrowAsync<InvalidDataException>();
        draft.SetValue("topic", "new");
        await ((Func<Task>)(() => draft.Preview(Fresh, new CancellationToken(true)))).Should().ThrowAsync<OperationCanceledException>();
        draft.Dispose();
        await ((Func<Task>)(() => draft.Preview(Fresh, Token))).Should().ThrowAsync<InvalidOperationException>();
    }
}
