using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionMetadataAnswerWindowTests
{
    [Fact]
    public async Task Native_review_has_separate_disabled_confirmation_and_clears_complete_private_result()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new SessionMetadataAnswerWindow();
            window.Show();
            try
            {
                window.Fields.Confirm.IsEnabled.Should().BeFalse();
                window.Fields.Preview.IsReadOnly.Should().BeTrue();
                window.Fields.Status.Text.Should().Contain("before inspecting").And.Contain("separate confirmation")
                    .And.Contain("No fallback");
                window.Fields.Preview.Text = "complete private result";
                window.Fields.Confirm.IsEnabled = true;
                window.Fields.Clear();
                window.Fields.Preview.Text.Should().BeEmpty();
                window.Fields.Confirm.IsEnabled.Should().BeFalse();
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }
}
