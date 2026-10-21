using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class EvidenceTraceCommandTests
{
    private const string Trace = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("open evidence")]
    [InlineData("prepare file search for \"value\"")]
    [InlineData("prepare folder search for \"value\"")]
    [InlineData("Nova, unrelated")]
    public void UnrelatedInputDoesNotClaimTheFamily(string text) =>
        EvidenceTraceCommand.Parse(text, "Nova").Should().BeNull();

    [Theory]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("０123456789abcdef0123456789abcdef")]
    [InlineData(" 0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef ")]
    [InlineData("\t0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef\n")]
    [InlineData("00-0123456789abcdef0123456789abcdef-0123456789abcdef-01")]
    [InlineData("https://example.invalid/0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcde\u200bf")]
    public void InvalidTraceIsReservedWithoutRepairOrDisclosure(string trace)
    {
        var command = EvidenceTraceCommand.Parse("prepare evidence trace " + trace, "Kora")!;
        command.IsValid.Should().BeFalse();
        command.TraceId.Should().BeNull();
        command.ToString().Should().Be(nameof(EvidenceTraceCommand));
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("evidence trace")]
    [InlineData("evidence trace help extra")]
    [InlineData("evidence trace scan")]
    [InlineData("prepare evidence")]
    [InlineData("prepare evidence trace")]
    [InlineData("prepare evidence trace ")]
    [InlineData("prepare evidence trace help")]
    [InlineData("prepare\tevidence trace 0123456789abcdef0123456789abcdef")]
    [InlineData("evidence trace help\n")]
    public void MalformedFamilyNeverFallsThrough(string text)
    {
        EvidenceTraceCommand.Parse(text, "Kora")!.IsValid.Should().BeFalse();
        EvidenceTraceCommand.Parse("Kora, " + text, "Kora")!.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Kora")]
    [InlineData("Nova")]
    [InlineData("Prepare")]
    [InlineData("Evidence")]
    public void ExactTypedAndCurrentNameCommandsKeepTheOriginalValue(string name)
    {
        var plain = EvidenceTraceCommand.Parse("prepare evidence trace " + Trace, name)!;
        plain.TraceId.Should().Be(Trace);
        plain.IsValid.Should().BeTrue();
        plain.IsHelp.Should().BeFalse();
        plain.IsActivated.Should().BeFalse();
        var activated = EvidenceTraceCommand.Parse(name + ", prepare evidence trace " + Trace, name)!;
        activated.TraceId.Should().Be(Trace);
        activated.IsActivated.Should().BeTrue();
        var help = EvidenceTraceCommand.Parse(name + ", EVIDENCE TRACE HELP", name)!;
        help.IsValid.Should().BeTrue();
        help.IsHelp.Should().BeTrue();
        help.TraceId.Should().BeNull();
        EvidenceTraceCommand.Parse("evidence trace help", name)!.IsHelp.Should().BeTrue();
        EvidenceTraceCommand.Parse("evidence trace invalid", name)!.IsValid.Should().BeFalse();
        EvidenceTraceCommand.DiscoveryPhrases.Should().Equal("prepare evidence trace", "evidence trace help");
        EvidenceTraceCommand.Syntax.Should().Contain("AND-composed").And.Contain("untrusted diagnostic data");
    }
}
