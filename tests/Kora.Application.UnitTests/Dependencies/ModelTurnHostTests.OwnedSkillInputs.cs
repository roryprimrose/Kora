using System.Text.Json;
using System.Text;
using System.Collections;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static void Parameterize(OwnedSkillHarness skill, string guidance = "Compare ${input:topic} and ${input:topic}.")
    {
        skill.Store.Snapshot = skill.Store.Snapshot! with
        {
            Document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", guidance,
                guidanceFormat: OwnedSkillGuidanceFormat.RequiredLiteralInputs),
        };
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task RequiredSkillInputsAreExactPrivateImmutableGuidanceNotTheOriginalTask(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        await using var skill = new OwnedSkillHarness(f, origin);
        Parameterize(skill);
        using var review = await skill.Review();
        review.RequiredNames.Should().Equal("topic");
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        const string literal = "  e\u0301 ${input:recursive} ${file:secret}  ";
        const string body = "Original task unchanged";
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = literal };
        using var invocation = await skill.Workflow.PrepareAsync(review, new(f.Session, null), body,
            skill.Authority, skill.Eligible, Token, values);
        values["topic"] = "MUTATED AFTER PREVIEW";
        using (var preview = JsonDocument.Parse(invocation.Preview))
        {
            preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
            preview.RootElement.GetProperty("Evidence")[1].GetProperty("Content").GetString().Should().Be(skill.Store.Snapshot!.Document.Guidance);
            using var binding = JsonDocument.Parse(preview.RootElement.GetProperty("Evidence")[2].GetProperty("Content").GetString()!);
            binding.RootElement.GetProperty("RequiredLiteralInputs").GetProperty("topic").GetString().Should().Be(literal);
            binding.RootElement.GetProperty("ExpandedGuidance").GetString().Should().Be($"Compare {literal} and {literal}.");
            preview.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        }
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        Encoding.UTF8.GetString(f.Adapter.Wire!).Should().NotContain("MUTATED AFTER PREVIEW");
        f.Messages.Should().NotContain(message => message.Contains(literal, StringComparison.Ordinal) || message.Contains(body, StringComparison.Ordinal));
        skill.Store.Writes.Should().Be(0);
        invocation.Preview.Should().BeEmpty();
        review.Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong-name")]
    [InlineData("blank")]
    [InlineData("unicode")]
    [InlineData("nul")]
    [InlineData("value-bound")]
    [InlineData("cumulative-bound")]
    [InlineData("wire-bound")]
    [InlineData("old-format")]
    public async Task InvalidRequiredSkillBindingsCannotSubmitRetainOrWrite(string fault)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        if (fault is not "old-format")
        {
            Parameterize(skill, fault is "cumulative-bound" or "wire-bound"
                ? string.Concat(Enumerable.Repeat("${input:topic}", 8)) + (fault is "cumulative-bound" ? "x" : "") : "${input:topic}");
        }
        using var review = await skill.Review();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = "literal" };
        switch (fault)
        {
            case "missing": values.Clear(); break;
            case "extra": values.Add("extra", "text"); break;
            case "wrong-name": values = new(StringComparer.Ordinal) { ["Topic"] = "text" }; break;
            case "blank": values["topic"] = " \r\n"; break;
            case "unicode": values["topic"] = new string('\ud800', 1); break;
            case "nul": values["topic"] = "\0"; break;
            case "value-bound": values["topic"] = new string('a', 513); break;
            case "cumulative-bound": values["topic"] = new string('a', 512); break;
            case "wire-bound": values["topic"] = new string('\u754c', 512); break;
        }
        await skill.Workflow.Invoking(flow => flow.PrepareAsync(review, new(f.Session, null), "Original",
            skill.Authority, skill.Eligible, Token, values)).Should().ThrowAsync<InvalidDataException>();
        review.IsCurrent.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredSkillInputsNeverFallbackToHostedOrAcceptAChangedProvider(bool foreignReply)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f, qualified: foreignReply);
        Parameterize(skill);
        using var review = await skill.Review();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Workflow.PrepareAsync(review, new(f.Session, null), "Original",
            skill.Authority, skill.Eligible, Token, new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = "private literal" });
        if (foreignReply)
        {
            f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.CopilotCandidate,
                new("forbidden foreign-provider answer", null));
        }
        var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(foreignReply ? 1 : 0);
        f.RetainCalls.Should().Be(1);
        skill.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task DuplicateRequiredSkillBindingKeysCannotProduceAReviewOrSend()
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        Parameterize(skill);
        using var review = await skill.Review();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        await skill.Workflow.Invoking(flow => flow.PrepareAsync(review, new(f.Session, null), "Original",
            skill.Authority, skill.Eligible, Token, new DuplicateSkillInputs())).Should().ThrowAsync<ArgumentException>();
        review.IsCurrent.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }

    private sealed class DuplicateSkillInputs : IReadOnlyDictionary<string, string>
    {
        public int Count => 2;
        public IEnumerable<string> Keys => ["topic", "topic"];
        public IEnumerable<string> Values => ["first", "second"];
        public string this[string key] => "first";
        public bool ContainsKey(string key) => string.Equals(key, "topic", StringComparison.Ordinal);
        public bool TryGetValue(string key, out string value) { value = "first"; return ContainsKey(key); }
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, string>>)new[] { new KeyValuePair<string, string>("topic", "first"), new("topic", "second") }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
