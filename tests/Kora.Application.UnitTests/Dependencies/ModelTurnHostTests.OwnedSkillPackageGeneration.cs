using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, 1)]
    [InlineData(RequestOrigin.ActivatedVoice, 4)]
    public async Task InitialPackageGenerationPreservesCompleteOriginalTreeThroughSeparateDisabledSave(RequestOrigin origin, int count)
    {
        await using var f = new SkillRefinementFixture(origin);
        var draft = PackageGenerationDraft(count);
        const string body = "Explain the supplied prose using [notes](references\\guide.md).";
        f.Model.Answer(body);
        var input = new OwnedSkillGenerationRequest("  Original purpose\r\n", "User prose", "Explanation", f.Authority);
        var proposal = (await f.Service.ProposeInitial(draft, input, (candidate, _) =>
        {
            candidate.Draft.Should().BeSameAs(draft);
            candidate.Proposed.ManifestBytes.ToArray().Should().Equal(draft.ManifestBytes.ToArray());
            candidate.Proposed.Guidance.Should().Be(body);
            candidate.Proposed.References.Select(item => item.RelativePath).Should().Equal(draft.ReferenceDeclaration!.Paths);
            foreach (var reference in draft.References)
            {
                candidate.Proposed.References.Single(item => string.Equals(item.RelativePath, reference.RelativePath, StringComparison.Ordinal))
                    .Bytes.ToArray().Should().Equal(reference.Bytes.ToArray());
            }
            candidate.Generation.Should().BeSameAs(input);
            candidate.Destination.Should().Be("fixed-owned-destination");
            f.Store.Writes.Should().Be(0);
            return Task.FromResult(true);
        }, f.Eligible, Token))!;
        using var wire = JsonDocument.Parse(f.Model.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(input.Purpose);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
        using var evidence = JsonDocument.Parse(wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString()!);
        evidence.RootElement.GetProperty("input").GetString().Should().Be(input.Input);
        evidence.RootElement.GetProperty("output").GetString().Should().Be(input.Output);
        evidence.RootElement.GetProperty("manifest").GetString().Should().Be(draft.ManifestText);
        evidence.RootElement.GetProperty("primary").GetString().Should().Be(draft.Guidance);
        evidence.RootElement.GetProperty("destination").GetString().Should().Be(proposal.Destination);
        var components = evidence.RootElement.GetProperty("components").EnumerateArray().ToArray();
        components.Should().HaveCount(count);
        for (var index = 0; index < count; index++)
        {
            components[index].GetProperty("RelativePath").GetString().Should().Be(draft.References[index].RelativePath);
            components[index].GetProperty("Content").GetString().Should().Be(draft.References[index].Text);
        }
        f.Store.Writes.Should().Be(0);
        var review = new SkillRefinementReview { Confirm = false };
        (await f.Service.Save(proposal, f.Authority, review, f.Eligible, Token)).Should().BeNull();
        f.Store.Writes.Should().Be(0);
        review.Confirm = true;
        var saved = (await f.Service.Save(proposal, f.Authority, review, f.Eligible, Token))!;
        saved.Document.CompleteText.Should().Be(proposal.Proposed.CompleteText);
        saved.Document.IsAvailableForInvocation.Should().BeFalse();
        review.Calls.Should().Equal("review", "confirm", "review", "confirm");
        f.Store.Writes.Should().Be(1);
        draft.Guidance.Should().Be("Original primary.");
        f.Model.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("[invented](other.md)")]
    [InlineData("[alias](references/guide.md)")]
    [InlineData("[malformed](references\\guide.md")]
    [InlineData("![image](references\\guide.md)")]
    [InlineData("```powershell\nGet-Content secret\n```")]
    [InlineData("Run helper.ps1")]
    [InlineData("<script>bad</script>")]
    [InlineData("https://example.test")]
    [InlineData(" ")]
    public async Task InitialPackageGenerationRejectsInvalidPrimaryWithoutReviewOrMutation(string body)
    {
        await using var f = new SkillRefinementFixture();
        var draft = PackageGenerationDraft();
        var before = draft.CompleteText;
        f.Model.Answer(body);
        await f.Service.Invoking(service => service.ProposeInitial(draft, new("Purpose", "Input", "Output", f.Authority),
            (_, _) => throw new InvalidOperationException("Invalid output must not reach review"), f.Eligible, Token))
            .Should().ThrowAsync<InvalidDataException>();
        draft.CompleteText.Should().Be(before);
        f.Store.Writes.Should().Be(0);
        f.Model.Model.RetainCalls.Should().Be(0);
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"body\":\"NEW\",\"components\":[]}")]
    [InlineData("{\"body\":\"NEW\",\"manifest\":\"changed\"}")]
    [InlineData("{\"body\":\"NEW\",\"files\":{\"other.md\":\"invented\"}}")]
    [InlineData("{\"body\":\"NEW\",\"body\":\"OTHER\"}")]
    public async Task InitialPackageGenerationRejectsModelChosenTreeOrExtraProperties(string answer)
    {
        await using var f = new SkillRefinementFixture();
        f.Model.Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(answer, null));
        await f.Service.Invoking(service => service.ProposeInitial(PackageGenerationDraft(),
            new("Purpose", "Input", "Output", f.Authority), (_, _) => Task.FromResult(true), f.Eligible, Token))
            .Should().ThrowAsync<InvalidDataException>();
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task InitialPackageGenerationAcceptsExactSerializedBudgetAndRejectsOneByteOverBeforeProvider()
    {
        await using var f = new SkillRefinementFixture();
        var draft = PackageGenerationDraft(4);
        f.Model.Answer("Complete guidance.");
        await f.Service.ProposeInitial(draft, new("Purpose", "x", "Output", f.Authority),
            (_, _) => Task.FromResult(true), f.Eligible, Token);
        var baseline = f.Model.Model.Adapter.Wire!.Length;
        var input = new string('x', 1 + ModelContextEnvelope.MaximumInputUtf8Bytes - baseline);
        await f.Service.ProposeInitial(draft, new("Purpose", input, "Output", f.Authority),
            (_, _) => Task.FromResult(true), f.Eligible, Token);
        f.Model.Model.Adapter.Wire!.Length.Should().Be(32768);
        f.Model.Model.Adapter.Calls.Should().Be(2);
        await f.Service.Invoking(service => service.ProposeInitial(draft, new("Purpose", input + "x", "Output", f.Authority),
            (_, _) => Task.FromResult(true), f.Eligible, Token)).Should().ThrowAsync<InvalidDataException>();
        f.Model.Model.Adapter.Calls.Should().Be(2);
        f.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("destination-model")]
    [InlineData("destination-review")]
    [InlineData("catalogue")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("authority")]
    [InlineData("expiry")]
    [InlineData("eligible")]
    [InlineData("reject")]
    [InlineData("unavailable")]
    public async Task InitialPackageGenerationRetiresChangedOrUnavailableProposalWithoutWrite(string change)
    {
        await using var f = new SkillRefinementFixture();
        f.Model.Answer("Complete guidance.");
        f.Service.Destination(PackageGenerationDraft()).Should().Be("fixed-owned-destination");
        f.Model.Model.Adapter.OnRun = () =>
        {
            switch (change)
            {
                case "destination-model": f.Store.Path = "changed"; break;
                case "catalogue": f.Store.Catalogue = [new(f.Draft, "directory", "manifest", "guidance")]; break;
                case "owner": f.Model.Model.Current = false; break;
                case "privacy": f.Model.Model.CanControl = false; break;
                case "authority": f.Authority.Retire(); break;
                case "expiry": f.Model.Model.Time.Now += TimeSpan.FromMinutes(3); break;
                case "eligible": f.Open = false; break;
            }
        };
        if (change is "unavailable") { f.Model.Model.Time.Now += TimeSpan.FromMinutes(6); }
        var operation = () => f.Service.ProposeInitial(PackageGenerationDraft(), new("Purpose", "Input", "Output", f.Authority),
            (_, _) =>
            {
                if (change is "destination-review") { f.Store.Path = "changed"; }
                return Task.FromResult(change is not "reject");
            }, f.Eligible, Token);
        if (change is "reject") { (await operation()).Should().BeNull(); }
        else { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        f.Store.Writes.Should().Be(0);
        f.Model.Model.RetainCalls.Should().Be(0);
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
        if (change is "unavailable") { f.Model.Model.Adapter.Calls.Should().Be(0); }
    }

    [Theory]
    [InlineData("current")]
    [InlineData("destination")]
    [InlineData("catalogue")]
    [InlineData("authority")]
    public async Task GeneratedPackageRefinementPreservesTreeAndOriginalSaveBindings(string change)
    {
        await using var f = new SkillRefinementFixture();
        f.Model.Answer("FIRST");
        var initial = (await f.Service.ProposeInitial(PackageGenerationDraft(), new("Purpose", "Input", "Output", f.Authority),
            (_, _) => Task.FromResult(true), f.Eligible, Token))!;
        f.Model.Answer("SECOND");
        var refined = (await f.Service.Propose(initial.Proposed, null, "Original critique", f.Authority,
            (_, _) => Task.FromResult(true), f.Eligible, Token, initial))!;
        refined.Generation.Should().BeSameAs(initial.Generation);
        refined.Proposed.ManifestBytes.ToArray().Should().Equal(initial.Proposed.ManifestBytes.ToArray());
        refined.Proposed.References.Single().Bytes.ToArray().Should().Equal(initial.Proposed.References.Single().Bytes.ToArray());
        using var wire = JsonDocument.Parse(f.Model.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(initial.Proposed.CompleteText);
        if (change is "destination") { f.Store.Path = "changed"; }
        if (change is "catalogue") { f.Store.Catalogue = [new(f.Draft, "directory", "manifest", "guidance")]; }
        if (change is "authority") { f.Authority.Retire(); }
        var saving = () => f.Service.Save(refined, f.Authority, new SkillRefinementReview(), f.Eligible, Token);
        if (change is "current")
        {
            (await saving())!.Document.CompleteText.Should().Be(refined.Proposed.CompleteText);
            f.Store.Writes.Should().Be(1);
        }
        else
        {
            await saving.Should().ThrowAsync<InvalidOperationException>();
            f.Store.Writes.Should().Be(0);
        }
    }

    private static OwnedDeclarativeSkillDocument PackageGenerationDraft(int count = 1) =>
        OwnedDeclarativeSkillDocument.CreatePackage("example", "2.0.0", "Example", "Description", "Original primary.",
            Enumerable.Range(0, count).ToDictionary(index => index == 0 ? @"references\guide.md" : $"notes{index}.md",
                index => Encoding.UTF8.GetBytes("Literal notes " + index + "\r\ne\u0301 ${input:literal}"), StringComparer.Ordinal));
}
