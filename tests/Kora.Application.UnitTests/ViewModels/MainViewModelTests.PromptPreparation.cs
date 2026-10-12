using AwesomeAssertions;
using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("/prompt example")]
    [InlineData("/example")]
    [InlineData("Kora, use example")]
    public async Task ActualOwnedPromptArtifactRoutesOpenNativePreparationWithoutLegacyInference(string command)
    {
        var f = await Fixture.CreateInitializedAsync();
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\nRequest ${input:topic}", true),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact-native-revision" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        PromptParameterDraft? draft = null;
        f.ViewModel.PromptPreparationRequested += proposed => { draft = proposed; return Task.CompletedTask; };
        await f.RunAsync(command);
        draft.Should().NotBeNull();
        using (draft)
        {
            draft!.Source.Should().BeSameAs(source);
            draft.RequiredNames.Should().Equal("topic");
            f.Reasoner.Requests.Should().BeEmpty();
            draft.SetValue("topic", "shutdown");
            var expanded = await draft.Preview(f.ViewModel.IsPromptSourceCurrent, TestContext.Current.CancellationToken);
            expanded.UserRequest.Should().Be("Request shutdown");
            f.ViewModel.CommandText = "changed";
            draft.CanUse.Should().BeFalse();
        }
        f.ViewModel.ReportPromptUse(new(ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending));
        f.ViewModel.ResponseBody.Should().Contain("No production adapter").And.NotContain("shutdown");
    }

    [Theory]
    [InlineData("missing-form")]
    [InlineData("inline")]
    [InlineData("source")]
    [InlineData("host-after-freshness")]
    public async Task NativeParameterSelectionFailsClosedWithoutRawTemplateFallback(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\nRequest ${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact-native-revision" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ =>
        {
            if (failure is "host-after-freshness") { f.Session.IsUnlocked = false; }
            return Task.FromResult(failure is not "source");
        });
        if (string.Equals(failure, "inline", StringComparison.Ordinal))
        { f.ViewModel.PromptPreparationRequested += _ => throw new InvalidOperationException("Form must not open for inline values."); }
        await f.RunAsync(string.Equals(failure, "inline", StringComparison.Ordinal) ? "/example secret=value" : "/example");
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.ResponseBody.Should().NotContain("${input:topic}");
        f.ViewModel.BindOwnedInstructionFreshness(null);
        (await f.ViewModel.IsPromptSourceCurrent(source, TestContext.Current.CancellationToken)).Should().BeFalse();
        await ((Func<Task>)(() => f.ViewModel.IsPromptSourceCurrent(source, new CancellationToken(true))))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("Kora, use example", true)]
    [InlineData("use example", false)]
    public async Task VoicePromptSelectionRequiresCurrentNameAndOnlyFocusesNativeInput(string command, bool activated)
    {
        var f = await Fixture.CreateInitializedAsync();
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\nRequest ${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact-native-revision" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        PromptParameterDraft? draft = null;
        f.ViewModel.PromptPreparationRequested += proposed => { draft = proposed; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync(command, 0.91f);
        if (activated) { draft.Should().NotBeNull(); draft!.Dispose(); }
        else { draft.Should().BeNull(); f.ViewModel.ResponseBody.Should().Contain("current assistant name"); }
        f.Reasoner.Requests.Should().BeEmpty();
        using var cancel = new CancellationTokenSource();
        f.ViewModel.BindOwnedInstructionFreshness(_ => { cancel.Cancel(); return Task.FromResult(true); });
        (await f.ViewModel.IsPromptSourceCurrent(source, cancel.Token)).Should().BeFalse();
    }
}
