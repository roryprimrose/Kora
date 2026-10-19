using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Diagnostic_preview_is_explicit_native_read_only_separate_from_future_save_reset_and_foreground_response()
    {
        var absent = new Fixture();
        absent.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("unavailable");
        absent.ViewModel.CanPreviewDiagnosticImpact.Should().BeFalse();
        absent.ViewModel.CanCancelDiagnosticImpact.Should().BeFalse();
        absent.ViewModel.CanClearDiagnosticImpact.Should().BeFalse();
        absent.ViewModel.ProposedDiagnosticImpactDays = 1;
        absent.ViewModel.ProposedDiagnosticImpactDays = null;
        absent.ViewModel.Dispose();

        var fixture = new Fixture(enableDiagnosticRetention: true, enableDiagnosticImpact: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.CanClearDiagnosticImpact.Should().BeTrue();
        fixture.ViewModel.CanCancelDiagnosticImpact.Should().BeFalse();
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var transcript = fixture.ViewModel.Transcript;
        fixture.ViewModel.ProposedDiagnosticImpactDays.Should().BeNull();
        fixture.ViewModel.CanPreviewDiagnosticImpact.Should().BeFalse();
        fixture.ViewModel.ProposedDiagnosticImpactDays = 365;
        fixture.ViewModel.CanPreviewDiagnosticImpact.Should().BeFalse();
        fixture.ViewModel.ProposedDiagnosticImpactDays = 1;
        fixture.ViewModel.ProposedDiagnosticImpactDays = 1;
        fixture.ViewModel.CanPreviewDiagnosticImpact.Should().BeTrue();
        fixture.DiagnosticImpactReader.Requests.Should().BeEmpty();
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("Ordinary logs: Complete; 1 newly due")
            .And.Contain("Spans: Complete; 1 newly due").And.Contain("Owned links: Complete; 0 newly due")
            .And.Contain("Committed UTC").And.Contain("Proposed due UTC")
            .And.NotContain("private-stamp");
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.Transcript.Should().Be(transcript);
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.DiagnosticPolicy.Effective.Should().Be(DiagnosticRetentionDays.Default);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(fixture.ViewModel.ProposedDiagnosticImpactDays)
                or nameof(fixture.ViewModel.SelectedDiagnosticRetentionDays))
            {
                fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
            }
        };
        fixture.ViewModel.SelectedDiagnosticRetentionDays = 29;
        fixture.ViewModel.SelectedDiagnosticRetentionDays = 30;
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("draft edited");
        fixture.ViewModel.ProposedDiagnosticImpactDays = 2;
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("Proposal edited");
        fixture.DiagnosticImpactReader.LogStatus = DiagnosticImpactStatus.Incomplete;
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("Ordinary logs: Incomplete; no count");
        await fixture.ViewModel.ClearDiagnosticImpactCommand.ExecuteAsync();
        fixture.ViewModel.ProposedDiagnosticImpactDays.Should().BeNull();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("cleared");
        fixture.ViewModel.ProposedDiagnosticImpactDays = 1;
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        await fixture.ViewModel.RefreshDiagnosticRetentionCommand.ExecuteAsync();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        fixture.DiagnosticPreferences.Value = new(365);
        fixture.DiagnosticConfiguration!.Observe();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => false);
        fixture.ViewModel.CanPreviewDiagnosticImpact.Should().BeFalse();
        fixture.ViewModel.Dispose();
        fixture.ViewModel.CanCancelDiagnosticImpact.Should().BeFalse();
        fixture.ViewModel.CanClearDiagnosticImpact.Should().BeFalse();
        fixture.DiagnosticImpactReader.Signal();
    }

    [Fact]
    public async Task Diagnostic_preview_cancel_clear_close_owner_call_and_private_ABA_do_not_resume_or_replay()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true, enableDiagnosticImpact: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedDiagnosticImpactDays = 1;
        var pending = new TaskCompletionSource<DiagnosticImpactSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DiagnosticImpactReader.Pending = pending.Task;
        var scan = fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        fixture.ViewModel.CanCancelDiagnosticImpact.Should().BeTrue();
        await fixture.ViewModel.CancelDiagnosticImpactCommand.ExecuteAsync();
        await scan;
        fixture.ViewModel.CanCancelDiagnosticImpact.Should().BeFalse();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("cancelled");
        fixture.DiagnosticImpactReader.Pending = null;
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        pending.SetResult(DiagnosticImpactSnapshot.Unavailable);
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("Complete hypothetical");
        var owner = 1L;
        fixture.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        owner++;
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
            TestContext.Current.CancellationToken);
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        var privacy = fixture.PrivacyObservation.Current;
        fixture.PrivacyObservation.Publish(new(privacy, privacy with { SessionState = WindowsSessionState.Unknown },
            WindowsPrivacyChangeReason.Unknown));
        fixture.ViewModel.CanPreviewDiagnosticImpact.Should().BeFalse();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => false);
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Diagnostic_preview_failure_is_content_free_and_never_holds_future_policy_unavailable()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true, enableDiagnosticImpact: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedDiagnosticImpactDays = 1;
        fixture.DiagnosticImpactReader.Current = false;
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("failed");
        fixture.DiagnosticPolicy.Effective.Should().Be(DiagnosticRetentionDays.Default);
        fixture.DiagnosticConfiguration!.Get().Available.Should().BeTrue();
        fixture.ViewModel.Invoking(model => model.ProposedDiagnosticImpactDays = 0)
            .Should().Throw<ArgumentOutOfRangeException>();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Foreground_question_ABA_retires_preview_without_answering_or_changing_the_question()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true, enableDiagnosticImpact: true);
        await using var admission = fixture.DiagnosticAdmission;
        fixture.Probe.Status = new Kora.Core.Dependencies.DependencyStatus("local.inference", "Local model inference (Ollama)",
            Kora.Core.Dependencies.DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedDiagnosticImpactDays = 1;
        await fixture.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        fixture.Reasoner.Question = new Kora.Core.Dependencies.LocalModelQuestion("Which?", ["One", "Two"]);
        await fixture.RunAsync("choose a colour");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.CanPreviewDiagnosticImpact.Should().BeFalse();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await fixture.ViewModel.CancelModelQuestionAsync();
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("retired");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public void Diagnostic_formatter_rejects_unknown_source_instead_of_mislabeling_counts()
    {
        var describe = () => MainViewModel.DescribeDiagnosticImpact(
            new((DiagnosticImpactSource)99, DiagnosticImpactStatus.Unavailable, null));
        describe.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Native_command_boundary_reports_unexpected_preview_failure_without_content_or_policy_changes()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true, enableDiagnosticImpact: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedDiagnosticImpactDays = 1;
        fixture.DiagnosticImpactReader.OnCurrent = () => throw new FormatException("private sentinel");
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(MainViewModel.DiagnosticImpactPreviewStatus), StringComparison.Ordinal)
                && fixture.ViewModel.DiagnosticImpactPreviewStatus.Contains("Preview unavailable or failed", StringComparison.Ordinal))
            {
                reported.TrySetResult();
            }
        };
        fixture.ViewModel.PreviewDiagnosticImpactCommand.Execute(null);
        await reported.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.DiagnosticImpactPreviewStatus.Should().Contain("no result").And.NotContain("private sentinel");
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.DiagnosticPolicy.Effective.Should().Be(DiagnosticRetentionDays.Default);
        fixture.ViewModel.Dispose();
    }
}
