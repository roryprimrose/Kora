using AwesomeAssertions;

using Kora.Core;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public void Native_host_admission_failure_has_truthful_visible_recovery_without_a_question_window()
    {
        var fixture = new Fixture();
        fixture.ViewModel.ReportHostInteractionFailure("Storage unavailable. No successful query receipt.");
        fixture.ViewModel.ResponseTitle.Should().Be("Native question unavailable.");
        fixture.ViewModel.ResponseBody.Should().Contain("No successful query receipt");
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }
}
