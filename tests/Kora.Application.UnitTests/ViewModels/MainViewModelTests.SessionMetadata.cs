using AwesomeAssertions;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Kora, ")]
    public async Task SessionMetadata_original_typed_and_current_activation_route_exact_ids_and_question(string activation)
    {
        var fixture = new Fixture();
        const string subject = "10000000-0000-0000-0000-000000000001";
        const string caller = "10000000-0000-0000-0000-000000000002";
        var routed = 0;
        fixture.ViewModel.SessionMetadataRequested += (command, origin, admitted) =>
        {
            routed++;
            command.Subject.Should().Be(subject);
            command.Caller.Should().Be(caller);
            command.Question.Should().Be("What work remains?");
            origin.Should().Be(RequestOrigin.LocalUi);
            admitted().Should().BeTrue();
            return Task.CompletedTask;
        };
        await fixture.RunAsync(activation + $"review session metadata {subject} for {caller}: What work remains?");
        routed.Should().Be(1);
    }

    [Theory]
    [InlineData("review session metadata invalid")]
    [InlineData("review session metadata 10000000-0000-0000-0000-000000000001 for 10000000-0000-0000-0000-000000000002: Original")]
    public async Task SessionMetadata_invalid_or_unavailable_native_owner_reports_failure_without_generic_model_fallback(string text)
    {
        var fixture = new Fixture();
        await fixture.RunAsync(text);
        fixture.ViewModel.ResponseTitle.Should().NotBeNullOrWhiteSpace();
        fixture.ViewModel.ResponseBody.Should().NotContain("Succeeded");
    }
}
