using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class ToolFreeSkillContextTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(120)]
    public void CompleteReviewCarriesZeroToolBudgetsWithoutExtendingSourceExpiry(int seconds)
    {
        var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal");
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var now = DateTimeOffset.UnixEpoch;
        var context = OwnedSkillGuidanceContext.Create(document, request, "Original", now, now.AddSeconds(seconds));
        context.ExpiresAt.Should().Be(now.AddSeconds(Math.Min(seconds, 15)));
        var constraints = context.Constraints!;
        constraints.Version.Should().Be(1);
        constraints.ToolIds.Should().BeEmpty();
        constraints.MaximumCalls.Should().Be(0);
        constraints.MaximumRounds.Should().Be(1);
        constraints.InputUtf8Bytes.Should().Be(32768);
        constraints.OutputUtf8Bytes.Should().Be(ModelContextEnvelope.MaximumOutputUtf8Bytes);
        constraints.DeadlineMilliseconds.Should().Be(15000);
        using var review = JsonDocument.Parse(context.Serialize());
        review.RootElement.GetProperty("Constraints").GetProperty("MaximumCalls").GetInt32().Should().Be(0);
        var parsed = ModelToolProtocol.ReadContext(context.Serialize(), new(new(Guid.NewGuid()), request,
            ModelProviderSelection.OllamaCandidate));
        parsed.Constraints!.MaximumRounds.Should().Be(1);
        parsed.Tools.Should().BeEmpty();
        parsed.UserRequest.Should().Be("Original");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteUtf8ThresholdIncludesConstraintsAndLiteralInputs(bool reference)
    {
        var document = reference
            ? OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description", "Literal",
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = "Exact reference"u8.ToArray() })
            : OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal");
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var now = DateTimeOffset.UnixEpoch;
        const string inputs = "Exact literal bindings";
        var empty = OwnedSkillGuidanceContext.Create(document, request, "", now, now.AddSeconds(15), inputs);
        var body = new string('x', 32768 - empty.Serialize().Length);
        var exact = OwnedSkillGuidanceContext.Create(document, request, body, now, now.AddSeconds(15), inputs);
        Encoding.UTF8.GetByteCount(Encoding.UTF8.GetString(exact.Serialize())).Should().Be(32768);
        var oversized = () => OwnedSkillGuidanceContext.Create(document, request, body + "x", now, now.AddSeconds(15), inputs);
        oversized.Should().Throw<InvalidDataException>();
        exact.Evidence.Last().Content.Should().Be(inputs);
    }
}
