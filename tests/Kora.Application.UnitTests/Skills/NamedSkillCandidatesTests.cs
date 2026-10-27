using System.Text;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class NamedSkillCandidatesTests
{
    [Theory]
    [InlineData("Lock")]
    [InlineData("shutdown")]
    [InlineData("e\u0301")]
    public void SameNamesAcrossSourcesAndVersionsRemainExplicitImmutableChoices(string name)
    {
        var first = Owned(name);
        var second = first with { Document = OwnedDeclarativeSkillDocument.Create("example", "2.0.0", name, "Description", "different") };
        var source = new SharedSkillSource(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        var snapshot = SharedSkillSnapshot.Parse(source.Id, "example\\SKILL.md",
            Encoding.UTF8.GetBytes($"---\nname: {name}\nversion: 1.0.0\ndescription: Example\n---\nliteral"), false);
        using var candidates = Candidates(name, [first, second], [new(source, [snapshot])]);
        candidates.Choices.Should().HaveCount(snapshot.Name is null ? 2 : 3);
        var choice = candidates.Choose(2);
        choice.Owned.Should().BeSameAs(second);
        choice.Source.Should().BeNull();
        choice.Original.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        choice.ToString().Should().Contain("2.").And.Contain("Owned").And.Contain("2.0.0");
        ((Action)(() => candidates.Choose(1))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("example", "Example")]
    [InlineData("\u00e9", "e\u0301")]
    [InlineData("Example ", "Example")]
    public void ExactNamesDoNotFuzzyMatchCaseWhitespaceOrUnicode(string requested, string declared)
    {
        using var candidates = Candidates(requested, [Owned(declared)], []);
        candidates.Choices.Should().BeEmpty();
        ((Action)(() => candidates.Choose(1))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("dispose")]
    public void RevokedChoicesNeverRecoverOrRetarget(string change)
    {
        var current = true;
        var time = new Clock();
        using var candidates = Candidates("Example", [Owned("Example")], [], () => current, time);
        var choice = candidates.Choices.Single();
        if (change is "owner") { current = false; }
        if (change is "expiry") { time.Now = time.Now.AddMinutes(2); }
        if (change is "clock") { time.Now = time.Now.AddTicks(-1); }
        if (change is "dispose") { candidates.Dispose(); }
        candidates.IsCurrent.Should().BeFalse();
        candidates.Choices.Should().BeEmpty();
        choice.Current().Should().BeFalse();
        current = true;
        time.Now = DateTimeOffset.UnixEpoch;
        ((Action)(() => candidates.Choose(1))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void UnissuedNumbersCannotSelectOrConsumeAValidChoice(int number)
    {
        using var candidates = Candidates("Example", [Owned("Example")], []);
        ((Action)(() => candidates.Choose(number))).Should().Throw<InvalidOperationException>();
        candidates.Choose(1).Owned.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IncompatibleKindsAndExcludedSourcesDoNotShadowOrFallback(bool compatible, bool included)
    {
        var source = new SharedSkillSource(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        var text = "---\nname: example\nversion: 1.0.0\ndescription: Example\n"
            + (compatible ? "" : "allowed-tools: network.get_web_page\n") + "---\nliteral";
        var snapshot = SharedSkillSnapshot.Parse(source.Id, "example\\SKILL.md", Encoding.UTF8.GetBytes(text), false);
        using var candidates = new NamedSkillCandidates("example", [Owned("other")], [new(source, [snapshot])],
            new(RequestOrigin.LocalUi, static () => true), static () => true, TimeProvider.System,
            static _ => false, (_, _) => included);
        var choice = candidates.Choices.Single();
        choice.Source.Should().Be(source);
        choice.Shared.Should().BeSameAs(snapshot);
        choice.ToString().Should().Contain("Shared source 1").And.Contain("example");
        choice.IsSupported.Should().Be(compatible && included);
        if (choice.IsSupported) { candidates.Choose(1).Should().BeSameAs(choice); }
        else
        {
            choice.ToString().Should().Contain("unavailable");
            ((Action)(() => candidates.Choose(1))).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void ExcludedOwnedPackageNeverFallsBackToAnotherSource()
    {
        using var candidates = new NamedSkillCandidates("Example", [Owned("Example")], [],
            new(RequestOrigin.LocalUi, static () => true), static () => true, TimeProvider.System,
            static _ => false, static (_, _) => true);
        candidates.Choices.Single().IsSupported.Should().BeFalse();
        ((Action)(() => candidates.Choose(1))).Should().Throw<InvalidOperationException>();
    }

    private static OwnedDeclarativeSkillSnapshot Owned(string name) =>
        new(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", name, "Description", "literal"), "directory", "manifest", "instructions");

    private static NamedSkillCandidates Candidates(string name, IReadOnlyList<OwnedDeclarativeSkillSnapshot> owned,
        IReadOnlyList<SharedSkillCatalogue> shared, Func<bool>? current = null, TimeProvider? time = null) =>
        new(name, owned, shared, new(RequestOrigin.ActivatedVoice, static () => true),
            current ?? (static () => true), time ?? TimeProvider.System, static _ => true, static (_, _) => true);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
