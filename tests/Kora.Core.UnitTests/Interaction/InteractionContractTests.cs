using AwesomeAssertions;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class InteractionContractTests
{
    [Fact]
    public void Questions_copy_inputs_and_keep_labels_separate_from_stable_ids()
    {
        var options = new List<QuestionOption> { new("first", "Friendly display name") };
        var spec = new QuestionSpec("Choose", QuestionKind.SingleChoice, options);
        options.Clear();
        spec.Options.Should().ContainSingle().Which.Label.Should().Be("Friendly display name");
        spec.Options[0].Id.Should().Be("first");
        spec.Text.Should().Be("Choose");
        spec.Purpose.Should().Be("clarification");
        spec.SourceId.Should().Be("host");
        spec.Kind.Should().Be(QuestionKind.SingleChoice);
        var choices = new List<string> { "first" };
        var answer = new QuestionAnswer(choices);
        choices.Clear();
        answer.Choices.Should().Equal("first");
        spec.Accepts(answer, true).Should().BeTrue();
        spec.Accepts(new(["Friendly display name"]), true).Should().BeFalse();
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("a", true)]
    [InlineData("1234", true)]
    [InlineData("12345", false)]
    public void Text_answers_are_bounded_and_submission_is_explicit(string text, bool accepted)
    {
        var spec = new QuestionSpec("Text", QuestionKind.Text, [], maximumTextLength: 4);
        spec.MaximumTextLength.Should().Be(4);
        spec.Accepts(new([], text), true).Should().Be(accepted);
        spec.Accepts(new([], ""), false).Should().BeTrue();
        spec.Accepts(new([]), false).Should().BeFalse();
        spec.Accepts(new(["a"], "a"), true).Should().BeFalse();
    }

    [Fact]
    public void Multiple_choices_enforce_complete_bounds_known_ids_and_duplicates()
    {
        var spec = new QuestionSpec("Choose", QuestionKind.MultipleChoice,
            [new("a", "A"), new("b", "B"), new("c", "C")], 1, 2);
        spec.Minimum.Should().Be(1);
        spec.Maximum.Should().Be(2);
        spec.Accepts(new([]), false).Should().BeTrue();
        spec.Accepts(new([]), true).Should().BeFalse();
        spec.Accepts(new(["a", "b"]), true).Should().BeTrue();
        spec.Accepts(new(["a", "b", "c"]), true).Should().BeFalse();
        spec.Accepts(new(["a", "a"]), true).Should().BeFalse();
        spec.Accepts(new(["A"]), true).Should().BeFalse();
        spec.Accepts(new(["missing"]), true).Should().BeFalse();
        spec.Accepts(new(["a"], "also text"), true).Should().BeFalse();
    }

    [Theory]
    [InlineData((QuestionKind)99, 1, 1, 0, 1)]
    [InlineData(QuestionKind.MultipleChoice, -1, 1, 0, 1)]
    [InlineData(QuestionKind.MultipleChoice, 2, 1, 0, 2)]
    [InlineData(QuestionKind.MultipleChoice, 1, 1, 0, 33)]
    [InlineData(QuestionKind.MultipleChoice, 1, 2, 0, 1)]
    [InlineData(QuestionKind.MultipleChoice, 0, 0, 0, 1)]
    [InlineData(QuestionKind.MultipleChoice, 1, 1, 1, 1)]
    [InlineData(QuestionKind.SingleChoice, 0, 1, 0, 1)]
    [InlineData(QuestionKind.SingleChoice, 1, 2, 0, 2)]
    [InlineData(QuestionKind.Text, 1, 1, 1, 1)]
    [InlineData(QuestionKind.Text, 0, 1, 1, 0)]
    [InlineData(QuestionKind.Text, 1, 2, 1, 0)]
    [InlineData(QuestionKind.Text, 1, 1, 0, 0)]
    [InlineData(QuestionKind.Text, 1, 1, 4097, 0)]
    public void Malformed_constraints_are_not_reinterpreted_as_defaults(
        QuestionKind kind, int minimum, int maximum, int textLimit, int count)
    {
        var create = () => new QuestionSpec("Question", kind,
            Enumerable.Range(0, count).Select(i => new QuestionOption($"option-{i}", "Label")), minimum, maximum, textLimit);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Duplicate_options_and_unbounded_or_hostile_identifiers_are_rejected()
    {
        var duplicate = () => new QuestionSpec("Question", QuestionKind.SingleChoice, [new("a", "A"), new("a", "B")]);
        duplicate.Should().Throw<ArgumentException>();
        foreach (var value in new[] { "", "Upper", "model says allow", new string('a', 129), "é", "a/b", "a_b" })
        {
            var option = () => new QuestionOption(value, "Label");
            option.Should().Throw<ArgumentException>();
        }
        var label = () => new QuestionOption("a", new string('a', 513));
        label.Should().Throw<ArgumentException>();
        var empty = () => new QuestionOption("a", "");
        empty.Should().Throw<ArgumentException>();
        var text = () => new QuestionSpec(new string('a', 4097), QuestionKind.Text, [], maximumTextLength: 1);
        text.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Question_identity_requires_host_owner_and_positive_revision()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var key = new HostQuestionKey(request, new(Guid.NewGuid()), new(1));
        key.Request.Should().Be(request);
        key.Next().QuestionId.Should().Be(key.QuestionId);
        key.Next().Revision.Value.Should().Be(2);
        var missing = () => new HostQuestionKey(request, default, new(1));
        missing.Should().Throw<InvalidDataException>();
        var revision = () => new HostQuestionKey(request, new(Guid.NewGuid()), default);
        revision.Should().Throw<InvalidDataException>();
        var owner = () => new HostQuestionKey(null!, new(Guid.NewGuid()), new(1));
        owner.Should().Throw<ArgumentNullException>();
        var overflow = () => new HostQuestionKey(request, key.QuestionId, new(long.MaxValue)).Next();
        overflow.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Exact_bindings_validate_all_digests_and_never_use_display_or_provider_identity()
    {
        var original = Binding();
        original.ActionId.Should().Be("bounded.read");
        original.SourcePartition.Should().Be("builtin");
        original.SkillId.Should().Be("inspect");
        original.PolicyRevision.Value.Should().Be(1);
        for (var field = 0; field < 9; field++)
        {
            var invalid = () => Binding(field, "not-a-digest");
            invalid.Should().Throw<ArgumentException>();
            var changed = Binding(field, new string('b', 64));
            changed.Should().NotBe(original);
            original.HasObservedContentChange(changed).Should().Be(field < 4);
        }
        original.HasObservedContentChange(Binding()).Should().BeFalse();
        original.IsSameDefinition(Binding(action: "another")).Should().BeFalse();
        original.IsSameDefinition(Binding(source: "user")).Should().BeFalse();
        original.IsSameDefinition(Binding(skill: "different")).Should().BeFalse();
        original.HasObservedContentChange(Binding(action: "another")).Should().BeFalse();
        var uppercase = () => Binding(0, new string('A', 64));
        uppercase.Should().Throw<ArgumentException>();
        var badHex = () => Binding(0, new string('z', 64));
        badHex.Should().Throw<ArgumentException>();
        var nullDigest = () => Binding(0, null!);
        nullDigest.Should().Throw<ArgumentNullException>();
        var revision = () => Binding(policy: default(HostRevision));
        revision.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Proposals_require_exact_invocation_and_host_revision_but_unknown_effects_remain_classifiable()
    {
        var request = Request();
        var id = new HostId<ProposalIdentity>(Guid.NewGuid());
        var expiry = DateTimeOffset.UtcNow;
        var proposal = new HostOperationProposal(request, id, new(2), Binding(), HostOperationEffect.Unknown, expiry);
        proposal.Request.Should().Be(request);
        proposal.ProposalId.Should().Be(id);
        proposal.Revision.Value.Should().Be(2);
        proposal.Binding.Should().Be(Binding());
        proposal.Effect.Should().Be(HostOperationEffect.Unknown);
        proposal.ExpiresAt.Should().Be(expiry);
        var noInvocation = () => new HostOperationProposal(HostRequest.Create(RequestOrigin.LocalUi), id, new(1), Binding(), HostOperationEffect.BoundedRead, expiry);
        noInvocation.Should().Throw<InvalidDataException>();
        var noRevision = () => new HostOperationProposal(request, id, default, Binding(), HostOperationEffect.BoundedRead, expiry);
        noRevision.Should().Throw<InvalidDataException>();
        var badEffect = () => new HostOperationProposal(request, id, new(1), Binding(), (HostOperationEffect)99, expiry);
        badEffect.Should().Throw<InvalidDataException>();
        var noId = () => new HostOperationProposal(request, default, new(1), Binding(), HostOperationEffect.BoundedRead, expiry);
        noId.Should().Throw<InvalidDataException>();
        var noOwner = () => new HostOperationProposal(null!, id, new(1), Binding(), HostOperationEffect.BoundedRead, expiry);
        noOwner.Should().Throw<ArgumentNullException>();
        var noBinding = () => new HostOperationProposal(request, id, new(1), null!, HostOperationEffect.BoundedRead, expiry);
        noBinding.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Malformed_grant_records_cannot_reinterpret_unknown_scope_as_perpetual()
    {
        var proposal = new HostOperationProposal(Request(), new(Guid.NewGuid()), new(1),
            Binding(), HostOperationEffect.BoundedRead, DateTimeOffset.UtcNow);
        var valid = new OperationGrant(new(Guid.NewGuid()), new(1), proposal, OperationGrantScope.Once,
            new(1), RequestOrigin.LocalUi, DateTimeOffset.UtcNow);
        valid.Validate();
        (valid with { CreatorChannel = RequestOrigin.ActivatedVoice }).Validate();
        foreach (var bad in new[]
        {
            valid with { Revision = default }, valid with { SessionGeneration = default },
            valid with { UseCount = -1 }, valid with { Scope = (OperationGrantScope)99 },
            valid with { Status = (OperationGrantStatus)99 }, valid with { CreatorChannel = RequestOrigin.HostSystem },
            valid with { CreatorChannel = (RequestOrigin)99 }, valid with { Id = default },
        })
        {
            var validate = () => bad.Validate();
            validate.Should().Throw<InvalidDataException>();
        }
        var missingProposal = () => (valid with { ApprovedProposal = null! }).Validate();
        missingProposal.Should().Throw<InvalidDataException>();
    }

    private static HostRequest Request()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        return new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid()));
    }

    private static ExactOperationBinding Binding(int field = -1, string value = "", string action = "bounded.read",
        string source = "builtin", string skill = "inspect", HostRevision? policy = null)
    {
        var digests = Enumerable.Repeat(new string('a', 64), 9).ToArray();
        if (field >= 0)
        {
            digests[field] = value;
        }
        return new(action, source, skill, digests[0], digests[1], digests[2], digests[3],
            digests[4], digests[5], digests[6], digests[7], digests[8], policy ?? new(1));
    }
}
