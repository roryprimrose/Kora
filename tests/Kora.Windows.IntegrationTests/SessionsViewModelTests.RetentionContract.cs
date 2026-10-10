using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Fact]
    public void Native_retention_bindings_are_exact_passive_metadata_and_separate_review_confirmation_without_checkbox_save()
    {
        var source = Read("SessionsWindow.axaml");
        var document = XDocument.Parse(source);
        source.Should().Contain("RetentionStatus").And.Contain("RetentionPreview")
            .And.Contain("CanKeepSession").And.Contain("CanUseOrdinaryRetention")
            .And.Contain("CanConfirmRetention").And.Contain("CanReadExactRetention")
            .And.Contain("names and history are not read").And.Contain("no cleanup now");
        document.Descendants().Where(element => element.Name.LocalName is "CheckBox")
            .Should().NotContain(element => element.Attributes().Any(attribute =>
                attribute.Value.Contains("Retention", StringComparison.Ordinal)));
        var code = Read("SessionsWindow.axaml.cs");
        code.Should().Contain("model.RefreshRetentionAsync()").And.Contain("model.ReadExactRetentionAsync()")
            .And.Contain("model.PreviewRetention(keep: true)").And.Contain("model.PreviewRetention(keep: false)")
            .And.Contain("model.ConfirmRetentionAsync()").And.Contain("model.CancelRetentionReview()")
            .And.Contain("model.Close()");
    }
}
