using System.Globalization;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed class EvidenceCutoverRecoveryStartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("--ordinary-avalonia-option")]
    public void Ordinary_startup_is_not_intercepted_or_granted_recovery(string? argument)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var handled = EvidenceCutoverRecoveryStartup.TryRun(argument is null ? [] : [argument],
            static () => throw new InvalidOperationException("Recovery must not run."),
            output, errors, static _ => throw new InvalidOperationException("No notification expected."), out var exitCode);

        handled.Should().BeFalse();
        exitCode.Should().Be(0);
        output.ToString().Should().BeEmpty();
        errors.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("--recover-evidence")]
    [InlineData("--recover-evidence-cutover-extra")]
    [InlineData("--RECOVER-EVIDENCE-CUTOVER")]
    [InlineData("--recover-evidence-cutover --other")]
    public void Malformed_recovery_arguments_are_explicitly_refused_without_falling_into_host_startup(string argument)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var notices = new List<string>();
        var called = false;
        var handled = EvidenceCutoverRecoveryStartup.TryRun(argument.Split(' '),
            () => { called = true; return new(Guid.NewGuid(), 1, 2); }, output, errors, notices.Add, out var exitCode);

        handled.Should().BeTrue();
        exitCode.Should().Be(1);
        called.Should().BeFalse();
        output.ToString().Should().BeEmpty();
        notices.Should().ContainSingle().Which.Should().Contain("exact argument");
        using var failure = JsonDocument.Parse(errors.ToString());
        failure.RootElement.GetProperty("ReasonCode").GetString().Should().Be("InvalidArguments");
    }

    [Fact]
    public void Explicit_recovery_emits_a_content_free_bootstrap_receipt_and_never_reports_host_or_marker_authority()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var receipt = new EvidenceCutoverRecoveryReceipt(Guid.NewGuid(), 1, 2);
        var notices = new List<string>();
        var calls = 0;
        var handled = EvidenceCutoverRecoveryStartup.TryRun([EvidenceCutoverRecoveryStartup.Argument],
            () => { calls++; return receipt; }, output, errors, notices.Add, out var exitCode);

        handled.Should().BeTrue();
        calls.Should().Be(1);
        exitCode.Should().Be(0);
        errors.ToString().Should().BeEmpty();
        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("BootstrapDiagnostic").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("AssistantStarted").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("OwnershipMarkerReleased").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("StoreIdentity").GetGuid().Should().Be(receipt.StoreIdentity);
        notices.Should().ContainSingle().Which.Should().Contain("No assistant was started");
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("identity")]
    public void Refused_recovery_is_an_explicit_nonzero_result_without_a_success_receipt(string kind)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var notices = new List<string>();
        Exception error = kind switch
        {
            "corrupt" => new InvalidDataException("Incomplete evidence."),
            "access" => new UnauthorizedAccessException("Unavailable authority."),
            "cancel" => new OperationCanceledException(),
            "identity" => new System.ComponentModel.Win32Exception(5),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        EvidenceCutoverRecoveryStartup.TryRun([EvidenceCutoverRecoveryStartup.Argument],
            () => throw error, output, errors, notices.Add, out var exitCode).Should().BeTrue();

        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        notices.Should().ContainSingle().Which.Should().Contain("did not complete");
        using var failure = JsonDocument.Parse(errors.ToString());
        failure.RootElement.GetProperty("Outcome").GetString().Should().Be("Failed");
        failure.RootElement.GetProperty("CommitState").GetString().Should().Be("Unconfirmed");
        failure.RootElement.GetProperty("RetryAllowed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Receipt_output_failure_does_not_relabel_a_committed_cutover_as_rolled_back_or_retry_it()
    {
        using var output = new FailingReceiptWriter();
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var notices = new List<string>();
        var calls = 0;
        EvidenceCutoverRecoveryStartup.TryRun([EvidenceCutoverRecoveryStartup.Argument],
            () => { calls++; return new(Guid.NewGuid(), 1, 2); }, output, errors, notices.Add, out var exitCode).Should().BeTrue();

        exitCode.Should().Be(1);
        calls.Should().Be(1);
        notices.Should().ContainSingle().Which.Should().Contain("cutover committed").And.Contain("Do not repeat");
        using var failure = JsonDocument.Parse(errors.ToString());
        failure.RootElement.GetProperty("Outcome").GetString().Should().Be("CommittedNotificationFailed");
        failure.RootElement.GetProperty("CommitState").GetString().Should().Be("Committed");
    }

    [Theory]
    [InlineData(EvidenceCutoverRecoveryAdmissionFailure.OwnershipUnverified)]
    [InlineData(EvidenceCutoverRecoveryAdmissionFailure.LifecycleBusy)]
    [InlineData(EvidenceCutoverRecoveryAdmissionFailure.OwnerPresent)]
    [InlineData(EvidenceCutoverRecoveryAdmissionFailure.SessionUnavailable)]
    [InlineData(EvidenceCutoverRecoveryAdmissionFailure.ConfirmationUnavailableOrDeclined)]
    public void Admission_refusal_records_its_typed_reason_without_copying_exception_text(
        EvidenceCutoverRecoveryAdmissionFailure reason)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var notices = new List<string>();
        const string privateDetail = "private-diagnostic-canary";
        EvidenceCutoverRecoveryStartup.TryRun([EvidenceCutoverRecoveryStartup.Argument],
            () => throw new EvidenceCutoverRecoveryAdmissionException(reason, privateDetail),
            output, errors, notices.Add, out var exitCode).Should().BeTrue();

        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        errors.ToString().Should().NotContain(privateDetail);
        Encoding.UTF8.GetByteCount(errors.ToString()).Should().BeLessThanOrEqualTo(4096);
        using var failure = JsonDocument.Parse(errors.ToString());
        failure.RootElement.GetProperty("ReasonCode").GetString().Should().Be(reason.ToString());
        failure.RootElement.GetProperty("Outcome").GetString().Should().Be("NotAdmitted");
        failure.RootElement.GetProperty("CommitState").GetString().Should().Be("NotStarted");
        failure.RootElement.GetProperty("BootstrapDiagnostic").GetBoolean().Should().BeTrue();
        failure.RootElement.GetProperty("AssistantStarted").GetBoolean().Should().BeFalse();
        failure.RootElement.GetProperty("OwnershipMarkerReleased").GetBoolean().Should().BeFalse();
        notices.Should().ContainSingle();
    }

    [Fact]
    public void Failure_receipt_output_failure_is_explicitly_notified_without_retry_or_success()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new FailingReceiptWriter();
        var notices = new List<string>();
        var calls = 0;
        EvidenceCutoverRecoveryStartup.TryRun([EvidenceCutoverRecoveryStartup.Argument],
            () => { calls++; throw new InvalidDataException("Invalid saved state."); },
            output, errors, notices.Add, out var exitCode).Should().BeTrue();

        calls.Should().Be(1);
        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        notices.Should().ContainSingle().Which.Should().Contain("failure receipt could not be written");
    }

    private sealed class FailingReceiptWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("Receipt output unavailable.");
    }
}
