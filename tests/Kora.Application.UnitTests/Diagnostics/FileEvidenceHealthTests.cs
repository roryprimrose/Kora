using AwesomeAssertions;
using Kora.Application.Diagnostics;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed class FileEvidenceHealthTests
{
    [Fact]
    public void Required_file_delivery_failure_closes_admission_without_a_success_shaped_reset()
    {
        var health = new FileEvidenceHealth();
        health.RequireHealthy();
        health.Fail();
        var check = () => health.RequireHealthy();
        check.Should().Throw<IOException>().WithMessage("*admission is closed*");
        health.Fail();
        check.Should().Throw<IOException>();
    }
}
