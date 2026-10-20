using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class LocalContextSessionTargetTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public void ExistingPreservesReviewedSourceTargetAndGenerationWithoutCreatingASession(RequestOrigin origin, bool distinct)
    {
        var source = HostRequest.Create(origin);
        var reviewed = distinct ? HostRequest.Create(origin) : source;
        var generation = new HostRevision(7);

        var target = LocalContextSessionTarget.Existing(source, reviewed, generation);

        target.SourceRequest.Should().BeSameAs(source);
        target.Request.Should().BeSameAs(reviewed);
        target.ExpectedGeneration.Should().Be(generation);
        target.CreatesSession.Should().BeFalse();
        target.TryConsume().Should().BeTrue();
        target.TryConsume().Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewedFreshPreservesExactReviewedIdentityAndRequiresOneShotConsumption(bool distinct)
    {
        var source = HostRequest.Create(RequestOrigin.LocalUi);
        var reviewed = distinct
            ? new HostRequest(new(Guid.NewGuid()), source.SessionId, new(Guid.NewGuid()), source.Origin) : source;

        var target = LocalContextSessionTarget.ReviewedFresh(source, reviewed);

        target.SourceRequest.Should().BeSameAs(source);
        target.Request.Should().BeSameAs(reviewed);
        target.ExpectedGeneration.Should().BeNull();
        target.CreatesSession.Should().BeTrue();
        target.TryConsume().Should().BeTrue();
        target.TryConsume().Should().BeFalse();
        LocalContextSessionTarget.FreshSessionDisclosure.Should().Contain(LocalContextSessionTarget.FreshSessionName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConstructionRejectsNullSourceOrReviewedRequest(bool existing, bool nullSource)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var source = nullSource ? null! : request;
        var reviewed = nullSource ? request : null!;
        var create = () => existing
            ? LocalContextSessionTarget.Existing(source, reviewed, new(1))
            : LocalContextSessionTarget.ReviewedFresh(source, reviewed);

        create.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("system", false)]
    [InlineData("system", true)]
    [InlineData("origin", false)]
    [InlineData("origin", true)]
    [InlineData("request", false)]
    [InlineData("request", true)]
    [InlineData("task", false)]
    [InlineData("task", true)]
    [InlineData("copy", false)]
    [InlineData("copy", true)]
    public void ConstructionRejectsUntrustedOriginOrAliasedSourceAuthority(string defect, bool existing)
    {
        var source = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var reviewed = new HostRequest(defect is "request" or "copy" ? source.RequestId : new(Guid.NewGuid()),
            new(Guid.NewGuid()), defect is "task" or "copy" ? source.TaskId : new(Guid.NewGuid()),
            defect switch
            {
                "system" => RequestOrigin.HostSystem,
                "origin" => RequestOrigin.LocalUi,
                _ => source.Origin,
            });
        var create = () => existing
            ? LocalContextSessionTarget.Existing(source, reviewed, new(1))
            : LocalContextSessionTarget.ReviewedFresh(source, reviewed);

        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingRejectsUnknownGenerationEvenForAnOtherwiseReviewedTarget(bool distinct)
    {
        var source = HostRequest.Create(RequestOrigin.LocalUi);
        var reviewed = distinct ? HostRequest.Create(source.Origin) : source;
        var create = () => LocalContextSessionTarget.Existing(source, reviewed, default);

        create.Should().Throw<InvalidDataException>();
    }
}
