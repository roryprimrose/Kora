using AwesomeAssertions;

namespace W2Proof.Tests;

public sealed class AdmissionTests
{
    [Fact]
    public void SnapshotDoesNotFollowMutableReviewInputs()
    {
        byte[] original = [1, 2];
        var file = new InputFile("a", original);
        var digest = Admission.Digest("Kora.ScriptSet.v1", [file]);
        original[0] = 99;
        file.Bytes[1] = 99;
        Admission.Digest("Kora.ScriptSet.v1", [file]).Should().Be(digest);
    }

    [Fact]
    public void HelperByteChangesBothDependentSets()
    {
        InputFile entry = new("scripts\\entry.ps1", [1]);
        InputFile secondEntry = new("scripts\\second.ps1", [2]);
        InputFile helper = new("scripts\\helper.ps1", [3]);
        InputFile changed = new("scripts\\helper.ps1", [4]);
        Admission.Digest("Kora.ScriptSet.v1", [entry, helper])
            .Should().NotBe(Admission.Digest("Kora.ScriptSet.v1", [entry, changed]));
        Admission.Digest("Kora.ScriptSet.v1", [secondEntry, helper])
            .Should().NotBe(Admission.Digest("Kora.ScriptSet.v1", [secondEntry, changed]));
    }

    [Fact]
    public void DomainSeparatesDefinitionFromScripts() =>
        Admission.Digest("Kora.ScriptSet.v1", [new("a", [1])])
            .Should().NotBe(Admission.Digest("Kora.SkillDefinition.v1", [new("a", [1])]));

    [Fact]
    public void RejectsEmptySet()
    {
        Action operation = () => Admission.Digest("Kora.ScriptSet.v1", []);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsOversizedFile()
    {
        Action operation = () => Admission.Digest("Kora.ScriptSet.v1", [new("a", new byte[1024 * 1024 + 1])]);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MatchesCanonicalScriptVector() => Admission.Digest("Kora.ScriptSet.v1",
        [new("scripts\\b.ps1", [0x63]), new("scripts\\a.ps1", [0x61, 0x62])])
        .Should().Be("57e8d8ad0c7a993252c17a068caecc526f06e48d8ae9be7f24ce974f334ff9b7");

    [Theory]
    [InlineData("Scripts\\a.ps1")]
    [InlineData("scripts/a.ps1")]
    [InlineData("scripts\\..\\a.ps1")]
    [InlineData("\\scripts\\a.ps1")]
    [InlineData("scripts\\\\a.ps1")]
    [InlineData("c:\\a.ps1")]
    [InlineData("")]
    public void RejectsAliases(string name) => Admission.ValidName(name).Should().BeFalse();

    [Fact]
    public void RejectsDuplicateIdentity()
    {
        Action operation = () => Admission.Digest("Kora.ScriptSet.v1",
            [new("scripts\\a.ps1", [1]), new("scripts\\a.ps1", [2])]);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FramingRejectsConcatenationAmbiguity()
    {
        var left = Admission.Digest("Kora.ScriptSet.v1", [new("a", [1, 2]), new("b", [3])]);
        var right = Admission.Digest("Kora.ScriptSet.v1", [new("a", [1]), new("b", [2, 3])]);
        left.Should().NotBe(right);
    }

    [Theory]
    [InlineData("other", 7, "digest", 102)]
    [InlineData("run", 8, "digest", 102)]
    [InlineData("run", 7, "changed", 102)]
    [InlineData("run", 7, "digest", 103)]
    public void MismatchIsUnknown(string run, int pid, string digest, int value) =>
        Admission.Outcome("run", 7, "digest", new(run, pid, digest, value)).Should().Be("Unknown");

    [Fact]
    public void LostReceiptIsUnknownAfterTermination() =>
        Admission.Outcome("run", 7, "digest", null).Should().Be("Unknown");

    [Fact]
    public void ExactSyntheticReceiptIsObserved() =>
        Admission.Outcome("run", 7, "digest", new("run", 7, "digest", 102)).Should().Be("Observed");

    [Theory]
    [InlineData(5, "Denied")]
    [InlineData(1260, "Denied")]
    [InlineData(367, "Denied")]
    [InlineData(2, "Unknown")]
    [InlineData(258, "Unknown")]
    [InlineData(577, "Unknown")]
    public void OnlyAttributableErrorsAreDenial(int code, string outcome) =>
        Admission.ClassifyNative(code).Should().Be(outcome);
}
