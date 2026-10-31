using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class OwnedSkillFixtureTests
{
    [Fact]
    public void ParseAndEvaluatePreserveExactInertDataAndReportEveryAssertion()
    {
        const string input = "  e\u0301\U0001f642\r\nowned skill save; shell.run  ";
        var source = " \r\n" + JsonSerializer.Serialize(new
        {
            input,
            assertions = new[]
            {
                new { kind = "equals", value = input },
                new { kind = "contains", value = "shell.run" },
                new { kind = "equals", value = "different" },
                new { kind = "contains", value = "SHELL.RUN" },
            },
        }) + "\t ";
        var fixture = OwnedSkillFixture.Parse(source);
        fixture.Source.Should().Be(source);
        fixture.Input.Should().Be(input);
        fixture.Assertions.Select(assertion => assertion.Kind).Should().Equal("equals", "contains", "equals", "contains");
        fixture.Evaluate(input).Select(result => result.Passed).Should().Equal(true, true, false, false);
        fixture.Evaluate(input)[0].Assertion.Should().BeSameAs(fixture.Assertions[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\uD800")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"input\":\"x\",\"input\":\"y\"}")]
    [InlineData("{\"input\":null,\"assertions\":[]}")]
    [InlineData("{\"input\":1,\"assertions\":[]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":null}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[1]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"kind\":\"equals\"}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"eval\",\"value\":\"true\"}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":null,\"value\":\"x\"}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":null}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"\"}]}")]
    [InlineData("{\"input\":\"\\uD800\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}]}")]
    [InlineData("{\"input\":\"\\u0000\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":{\"custom\":{\"deep\":true}}}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}],\"mockedToolResponse\":{\"tool\":\"WebPageGet\",\"result\":\"fake\"}}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}],\"hooks\":[]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}],\"inputs\":{\"topic\":\"bound\"}}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}],\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}]}")]
    [InlineData("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}],\"references\":[\"notes.md\"]}")]
    public void ParseDeniesMalformedUnknownExecutableAndCounterfeitToolFields(string source) =>
        ((Action)(() => OwnedSkillFixture.Parse(source))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("input", 4096, true)]
    [InlineData("input", 4097, false)]
    [InlineData("value", 1024, true)]
    [InlineData("value", 1025, false)]
    [InlineData("assertions", 8, true)]
    [InlineData("assertions", 9, false)]
    [InlineData("source", 8192, true)]
    [InlineData("source", 8193, false)]
    public void ParseEnforcesExactCompleteUtf8AndCountLimits(string field, int count, bool accepted)
    {
        var source = JsonSerializer.Serialize(new
        {
            input = field is "input" ? new string('x', count) : "x",
            assertions = Enumerable.Repeat(new { kind = "equals", value = field is "value" ? new string('x', count) : "x" },
                field is "assertions" ? count : 1),
        });
        if (field is "source") { source += new string(' ', count - Encoding.UTF8.GetByteCount(source)); }
        var parse = () => OwnedSkillFixture.Parse(source);
        if (accepted)
        {
            var fixture = parse();
            fixture.Source.Should().Be(source);
            if (field is "source") { Encoding.UTF8.GetByteCount(fixture.Source).Should().Be(count); }
        }
        else { parse.Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, false)]
    public void EvaluateEnforcesCompleteOutputByteLimit(int excess, bool denied)
    {
        var fixture = OwnedSkillFixture.Parse("{\"input\":\"x\",\"assertions\":[{\"kind\":\"contains\",\"value\":\"x\"}]}");
        var output = new string('x', ModelContextEnvelope.MaximumOutputUtf8Bytes + excess);
        var evaluate = () => fixture.Evaluate(output);
        if (denied) { evaluate.Should().Throw<InvalidDataException>(); }
        else { evaluate().Should().ContainSingle().Which.Passed.Should().BeTrue(); }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n")]
    [InlineData("malformed-utf16")]
    [InlineData("\0")]
    public void EvaluateRejectsInvalidOutputWithoutDefaultPass(string output)
    {
        if (output is "malformed-utf16") { output = new string('\uD800', 1); }
        var fixture = OwnedSkillFixture.Parse("{\"input\":\"x\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"x\"}]}");
        ((Action)(() => fixture.Evaluate(output))).Should().Throw<InvalidDataException>();
    }
}
