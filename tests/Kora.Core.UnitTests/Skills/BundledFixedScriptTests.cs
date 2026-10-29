using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class BundledFixedScriptTests
{
    [Fact]
    public void Plan_requires_an_immutable_package_and_known_closed_effect()
    {
        ((Action)(() => _ = new BundledFixedScript(null!, FixedSessionAction.Lock)))
            .Should().Throw<ArgumentNullException>();
        var resources = new[]
        {
            new SkillResourceSnapshot("manifest.json", "Kora.Manifest", """
                {"schemaVersion":1,"id":"test","version":"1","name":"Test","description":"Test",
                "action":"test","entryPoint":"entry.ps1","parameterContract":"test","runtime":"powershell7",
                "dependencies":["test"],"helperLoadOrder":[],"files":[
                {"name":"skill.md","resourceId":"Kora.Instructions","kind":"instructions"},
                {"name":"entry.ps1","resourceId":"Kora.Entry","kind":"entry"}]}
                """u8.ToArray()),
            new SkillResourceSnapshot("skill.md", "Kora.Instructions", "# Test"u8.ToArray()),
            new SkillResourceSnapshot("entry.ps1", "Kora.Entry", "throw 'test only'"u8.ToArray()),
        };
        var package = new SkillPackageCatalogue(["Kora.Manifest"], resources).Packages[0];
        ((Action)(() => _ = new BundledFixedScript(package, (FixedSessionAction)99)))
            .Should().Throw<ArgumentOutOfRangeException>();
        var plan = new BundledFixedScript(package, FixedSessionAction.Lock);
        plan.Package.Should().BeSameAs(package);
        plan.Action.Should().Be(FixedSessionAction.Lock);
        var invocation = HostRequest.Create(RequestOrigin.LocalUi);
        var receipt = new BundledScriptReceipt(invocation, plan.Action, package.DeclaredResourceDigest, true, true);
        receipt.Invocation.Should().Be(invocation);
        receipt.Action.Should().Be(plan.Action);
        receipt.DeclaredResourceDigest.Should().Be(package.DeclaredResourceDigest);
        receipt.ScriptsCompleted.Should().BeTrue();
        receipt.ResourcesJoined.Should().BeTrue();
        var result = new FixedSessionEffectResult(FixedSessionEffectOutcome.RequestAccepted);
        result.Outcome.Should().Be(FixedSessionEffectOutcome.RequestAccepted);
        result.NativeError.Should().Be(0);
        result.AuditConfirmed.Should().BeTrue();
        default(FixedSessionEffectOutcome).Should().Be(FixedSessionEffectOutcome.Unconfirmed);
    }
}