using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Fact]
    public async Task Audit_view_reads_real_bounded_records_in_the_shell_and_releases_private_results_when_hidden()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var window = f.Window!;
            f.Model.ComposerBody = "keep while reading audit";
            using var evidence = new EvidenceWindowController(f.Native.Main, f.Native.Evidence, f.Native.Access,
                NullLogger<EvidenceViewModel>.Instance, f.Native.Access);
            evidence.Bind();
            evidence.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Audit, view));
            window.BindEvidenceNavigation(evidence.Open);
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<EvidenceWindow>((_, _) => opens++);
            window.FindControl<Button>("AuditActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var view = (EvidenceView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            var model = (EvidenceViewModel)view.DataContext!;
            model.CanQuery.Should().BeTrue();
            model.Source = EvidenceSource.AuthorityAudit;
            await model.SearchAsync();
            model.ResultText.Should().NotBeEmpty("the explicit query must publish its actual bounded page outcome");
            window.ActiveActivity.Should().Be(WorkbenchActivity.Audit);
            opens.Should().Be(0);
            window.ShowSessions(false, activate: false);
            model.ResultText.Should().BeEmpty();
            model.Records.Should().BeEmpty();
            model.CanQuery.Should().BeFalse();
            model.Status.Should().Contain("retired").And.Contain("presentation");
            f.Model.ComposerBody.Should().Be("keep while reading audit");
            f.Admission.Calls.Should().Be(0);
        });
    }
}
