using System.Text;
using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Memory;

namespace Kora.Core.UnitTests.Commands;

public sealed class MemoryCommandTests
{
    private const string Session = "11111111-1111-1111-1111-111111111111";
    private const string Memory = "22222222-2222-2222-2222-222222222222";
    private const string Target = Session + " " + Memory + " 4";

    [Theory]
    [InlineData("memory help", MemoryCommandOperation.Help)]
    [InlineData("list memories", MemoryCommandOperation.Help)]
    [InlineData("memory list " + Session, MemoryCommandOperation.List)]
    [InlineData("memory propose " + Session + " Decision \"exact \"\"quote\"\" café\"", MemoryCommandOperation.Propose)]
    [InlineData("memory inspect " + Target, MemoryCommandOperation.Inspect)]
    [InlineData("memory get " + Target, MemoryCommandOperation.Inspect)]
    [InlineData("memory review " + Target + " accept", MemoryCommandOperation.Review)]
    [InlineData("memory review " + Target + " reject", MemoryCommandOperation.Review)]
    [InlineData("memory admit " + Target, MemoryCommandOperation.Admit)]
    [InlineData("memory disable " + Target, MemoryCommandOperation.Disable)]
    [InlineData("memory forget " + Target, MemoryCommandOperation.Forget)]
    [InlineData("memory edit " + Target + " Decision \"exact \"\"quote\"\" café\"", MemoryCommandOperation.Edit)]
    [InlineData("memory set " + Target + " Decision \"exact \"\"quote\"\" café\"", MemoryCommandOperation.Edit)]
    public void Native_typed_and_voice_grammar_preserves_exact_selectors_and_content(string input, MemoryCommandOperation operation)
    {
        foreach (var prefix in new[] { "", "Kora, ", "Kora " })
        {
            var result = MemoryCommand.Parse(prefix + input, "Kora")!;
            result.Operation.Should().Be(operation);
            if (operation is not MemoryCommandOperation.Help) { result.SessionId.Should().Be(Guid.Parse(Session)); }
            if (operation is MemoryCommandOperation.Edit or MemoryCommandOperation.Propose)
            {
                result.Candidate.Should().Be(new MemoryCandidate(MemoryContentClass.Decision, "exact \"quote\" café"));
            }
        }
    }

    [Theory]
    [InlineData("memory list")]
    [InlineData("memory list profile")]
    [InlineData("memory list " + Session + " \"value\"")]
    [InlineData("memory list 00000000-0000-0000-0000-000000000000")]
    [InlineData("memory inspect " + Session + " " + Memory + " 0")]
    [InlineData("memory inspect " + Session + " " + Memory + " +1")]
    [InlineData("memory admit " + Target + " yes")]
    [InlineData("memory review " + Target + " yes")]
    [InlineData("memory edit " + Target + " Credential \"secret\"")]
    [InlineData("memory edit " + Target + " 4 \"not an enum name\"")]
    [InlineData("memory edit " + Target + " Decision \"unterminated")]
    [InlineData("memory edit " + Target + " Decision \"value\" trailing")]
    [InlineData("memory propose \"not delivered\"")]
    [InlineData("memory propose " + Session + " Credential \"secret\"")]
    [InlineData("memory propose " + Target + " Decision \"value\"")]
    [InlineData("memory propose " + Session + " Decision")]
    [InlineData("memory remember \"not delivered\"")]
    [InlineData("memory")]
    [InlineData("memory help \"value\"")]
    [InlineData("memory inspect " + Target + " \"value\"")]
    [InlineData("memory inspect " + Session + " invalid 1")]
    [InlineData("memory inspect " + Target + " extra")]
    [InlineData("memory unsupported " + Target)]
    [InlineData("memory review " + Target + " accept \"value\"")]
    [InlineData("memory edit " + Target + " Decision")]
    [InlineData("memory edit " + Target + " Decision\"value\"")]
    public void Ambiguous_broader_and_unsupported_commands_are_denied_not_model_routed(string input) =>
        MemoryCommand.Parse(input, "Kora")!.Operation.Should().Be(MemoryCommandOperation.Invalid);

    [Fact]
    public void Exact_values_use_the_authoritative_candidate_limits_without_normalizing_or_truncating()
    {
        foreach (var value in new[] { new string('x', 512), new string('x', 513), new string('界', 342),
            new string('\\', 512), " ", "line\nbreak", "\ud800", "e\u0301" })
        {
            var candidate = new MemoryCandidate(MemoryContentClass.Decision, value);
            var valid = MemoryPolicy.ValidateCandidate(candidate) == MemoryReason.None;
            foreach (var (selector, operation) in new[]
            {
                ("memory edit " + Target, MemoryCommandOperation.Edit),
                ("memory propose " + Session, MemoryCommandOperation.Propose)
            })
            {
                var command = MemoryCommand.Parse(selector + " Decision \"" + value + "\"", "Kora")!;
                command.Operation.Should().Be(valid ? operation : MemoryCommandOperation.Invalid);
                if (valid) { command.Candidate!.Value.Should().Be(value); }
            }
        }
        MemoryCommand.Parse("unrelated command", "Kora").Should().BeNull();
        MemoryCommand.Parse("memorysuffix", "Kora").Should().BeNull();
        MemoryCommand.Parse("Kora", "Kora").Should().BeNull();
        MemoryCommand.Parse("Korax memory help", "Kora").Should().BeNull();
        MemoryCommand.Parse("memory edit " + Target + " Decision \"" + new string('x', MemoryCommand.MaximumInputBytes) + "\"", "Kora")!
            .Operation.Should().Be(MemoryCommandOperation.Invalid);
    }

    [Theory]
    [InlineData("ExplicitFact", MemoryContentClass.ExplicitFact)]
    [InlineData("ResponsePreference", MemoryContentClass.ResponsePreference)]
    [InlineData("WorkflowPreference", MemoryContentClass.WorkflowPreference)]
    public void ReplacementSupportsEveryAdmittedContentClassification(string name, MemoryContentClass contentClass) =>
        MemoryCommand.Parse("memory edit " + Target + " " + name + " \"exact value\"", "Kora")!.Candidate
            .Should().Be(new MemoryCandidate(contentClass, "exact value"));

    [Theory]
    [InlineData("ExplicitFact", MemoryContentClass.ExplicitFact)]
    [InlineData("ResponsePreference", MemoryContentClass.ResponsePreference)]
    [InlineData("WorkflowPreference", MemoryContentClass.WorkflowPreference)]
    [InlineData("Decision", MemoryContentClass.Decision)]
    public void ProposalRequiresNoExistingIdentityAndPreservesExactClassAndValue(string name, MemoryContentClass contentClass)
    {
        var command = MemoryCommand.Parse("memory propose " + Session + " " + name + " \" exact value \"", "Kora")!;
        command.Operation.Should().Be(MemoryCommandOperation.Propose);
        command.MemoryId.Should().BeNull();
        command.Revision.Should().Be(0);
        command.Accept.Should().BeFalse();
        command.Candidate.Should().Be(new MemoryCandidate(contentClass, " exact value "));
    }

    [Fact]
    public void OversizedSerializedInventoryFailsExplicitlyWithoutTruncation()
    {
        var result = new MemoryCommandResult("observed", new string('x', SessionCommand.MaximumResultBytes));
        var serialize = () => MemoryCommandResult.Serialize(result);
        serialize.Should().Throw<InvalidDataException>().WithMessage("*64 KiB*");
    }

    [Fact]
    public void Full_identity_inventory_is_bounded_and_never_contains_candidate_or_lineage()
    {
        var scope = MemoryScope.Session(new(Guid.Parse(Session)));
        var result = new MemoryCommandResult("observed", "metadata")
        {
            Memories = [.. Enumerable.Range(0, MemoryPolicy.MaximumEntries).Select(_ =>
                new MemorySummary(Guid.NewGuid(), long.MaxValue, scope, MemoryReviewState.Admitted,
                    MemoryRetentionState.Enabled, DateTimeOffset.UtcNow))],
        };
        var bytes = MemoryCommandResult.Serialize(result);
        bytes.Length.Should().BeLessThanOrEqualTo(SessionCommand.MaximumResultBytes);
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("createdAt").And.Contain("scope").And.NotContain("candidate")
            .And.NotContain("lineage").And.NotContain("receipt");
    }
}
