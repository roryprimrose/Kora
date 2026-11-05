using AwesomeAssertions;

using Kora.Core.Mcp;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpResourceTemplateTests
{
    [Theory]
    [InlineData("data://files/{path}", """{"path":"a/b"}""", "data://files/a%2Fb")]
    [InlineData("data://files/{+path}", """{"path":"a/b"}""", "data://files/a/b")]
    [InlineData("data://files{?query,missing}", """{"query":"a b"}""", "data://files?query=a%20b")]
    [InlineData("data://files{#path}", """{"path":"a/b"}""", "data://files#a/b")]
    [InlineData("data://files{/path}", """{"path":"a b"}""", "data://files/a%20b")]
    [InlineData("data://files/item{.path}", """{"path":"a b"}""", "data://files/item.a%20b")]
    [InlineData("data://files/item{;path}", """{"path":""}""", "data://files/item;path")]
    [InlineData("data://files/item?first=1{&path}", """{"path":""}""", "data://files/item?first=1&path=")]
    [InlineData("https://{host}/item", """{"host":"example.com"}""", "https://example.com/item")]
    [InlineData("data://files/{path:2}", """{"path":"abcd"}""", "data://files/ab")]
    [InlineData("data://files/{path*}", """{"path":"ab"}""", "data://files/ab")]
    [InlineData("data://files/{path}", "{}", "data://files/")]
    [InlineData("data://files/static", "{}", "data://files/static")]
    [InlineData("data://files/{value:1}", """{"value":"\ud83d\ude00tail"}""", "data://files/%F0%9F%98%80")]
    [InlineData("data://files/{+path}", """{"path":"%2F"}""", "data://files/%2F")]
    [InlineData("data://files/{+path}", """{"path":"%2G"}""", "data://files/%252G")]
    [InlineData("data://files/{+path}", """{"path":"%G2"}""", "data://files/%25G2")]
    [InlineData("data://files/{+path}", """{"path":"%"}""", "data://files/%25")]
    [InlineData("data://files/{+path}", """{"path":"%2"}""", "data://files/%252")]
    [InlineData("data://files/{+path}", """{"path":"%éa"}""", "data://files/%25%C3%A9a")]
    [InlineData("data://files/{+path}", """{"path":"%aé"}""", "data://files/%25a%C3%A9")]
    public void ExpandsBoundedScalarParametersWithoutLocalAccess(string template, string arguments, string expected)
    {
        var parsed = new McpResourceTemplate(template);
        parsed.Expand(arguments).Should().Be(expected);
    }

    [Theory]
    [InlineData("data://files/{")]
    [InlineData("data://files/}")]
    [InlineData("data://files/}{path}")]
    [InlineData("data://files/{bad-name}")]
    [InlineData("data://files/{}")]
    [InlineData("data://files/{path:0}")]
    [InlineData("data://files/{path:bad}")]
    [InlineData("data://files/{path:1:2}")]
    [InlineData("data://files/{path:2*}")]
    [InlineData("data://files/{.}")]
    [InlineData("data://files/{path..name}")]
    [InlineData("data://files/{a,b,c,d,e,f,g,h,i}")]
    public void RejectsMalformedTemplates(string template) =>
        FluentActions.Invoking(() => new McpResourceTemplate(template)).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"unknown":"value"}""")]
    [InlineData("""{"path":42}""")]
    [InlineData("""{"path":["a"]}""")]
    [InlineData("""{"path":"a\nb"}""")]
    public void RejectsArgumentsOutsideTheScalarProfile(string arguments) =>
        FluentActions.Invoking(() => new McpResourceTemplate("data://files/{path}").Expand(arguments)).Should().Throw<InvalidDataException>();

    [Fact]
    public void TemplateExpressionParameterAndValueBoundsAreEnforced()
    {
        foreach (var template in new[]
        {
            "data://files/{" + new string('a', 65) + "}",
            "data://files/{path:1025}",
            "data://files/{a,b,c,d,e,f,g,h}{i}",
            "data://files/" + string.Concat(Enumerable.Repeat("{a}", 17)),
        })
        { FluentActions.Invoking(() => new McpResourceTemplate(template)).Should().Throw<InvalidDataException>(); }
        FluentActions.Invoking(() => new McpResourceTemplate("data://files/{path}").Expand("{\"path\":\"" + new string('a', 1025) + "\"}"))
            .Should().Throw<InvalidDataException>();
    }
}