using System.Security.Principal;

using AwesomeAssertions;

using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests.Coordination;

public sealed class OwnerContinuityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_verified_clean_disposal_clears_the_non_sensitive_bit(bool clean)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var path = Path.Combine(AppContext.BaseDirectory, $"Kora.test-continuity-{Guid.NewGuid():N}");
        try
        {
            using var continuity = new OwnerContinuity(identity.User!.Value, path);
            continuity.Begin();
            continuity.HasUncleanOwner.Should().BeTrue();
            continuity.End(clean);
            File.Exists(path).Should().Be(!clean);
            if (!clean)
            {
                File.ReadAllBytes(path).Should().Equal([1]);
                using var next = new OwnerContinuity(identity.User.Value, path);
                var action = next.Begin;
                action.Should().Throw<InvalidOperationException>()
                    .WithMessage("*no verified clean exit*");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
