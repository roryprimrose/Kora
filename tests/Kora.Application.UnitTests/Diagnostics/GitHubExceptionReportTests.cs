using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed class GitHubExceptionReportTests
{
    [Fact]
    public void Report_contains_only_explicit_environment_and_safe_exception_signature()
    {
        var report = Create();
        report.Version.Should().Be("1.2.3-beta1");
        report.WindowsVersion.Should().Be("10.0.26100");
        report.Architecture.Should().Be("X64");
        report.Title.Should().Contain("System.InvalidOperationException");
        report.Body.Should().Contain(report.Signature.ExceptionType).And.Contain(report.Signature.StackTrace)
            .And.Contain("Kora error fingerprint: v1:" + report.Signature.Fingerprint)
            .And.NotContain("## Possible regression");
        var destination = report.NewIssueDestination([]);
        destination.Address.Host.Should().Be("github.com");
        destination.Address.AbsolutePath.Should().Be("/roryprimrose/Kora/issues/new");
        Uri.UnescapeDataString(destination.Address.Query).Should().Contain(report.Body);
        GitHubExceptionReport.Disclosure.Should().Contain("account").And.Contain("history")
            .And.Contain("opening the browser does not submit").And.Contain("cannot").And.Contain("fingerprint");
    }

    [Fact]
    public void Regression_report_links_prior_issues_and_still_requires_version_review()
    {
        var report = Create();
        GitHubIssueMatch[] related = [new(12, "first", true), new(13, "second", true)];
        report.CreateBody(related).Should().Contain("## Possible regression").And.Contain("#12, #13")
            .And.Contain("Verify that this version includes");
        Uri.UnescapeDataString(report.NewIssueDestination(related).Address.Query).Should().Contain("#12, #13");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("private\ncontent")]
    [InlineData("C:\\Users\\private")]
    [InlineData("`markdown`")]
    public void Untrusted_or_content_bearing_environment_values_are_rejected(string value)
    {
        FluentActions.Invoking(() => new GitHubExceptionReport(Signature(), value, "10.0", "X64"))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new GitHubExceptionReport(Signature(), "1.0", value, "X64"))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new GitHubExceptionReport(Signature(), "1.0", "10.0", value))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Metadata_bounds_and_url_bounds_fail_without_truncating_the_report()
    {
        FluentActions.Invoking(() => new GitHubExceptionReport(Signature(), new string('x', 129), "10", "X64"))
            .Should().Throw<InvalidDataException>();
        new GitHubExceptionReport(Signature(), "1.2.3+build_4", "10.0 26100", "X64").Version.Should().Be("1.2.3+build_4");
        var frame = "Tests.Example::Operation(System.Int32" + string.Concat(Enumerable.Repeat("[]", 100)) + ")";
        var frames = string.Join('\n', Enumerable.Repeat(frame, 16));
        var report = new GitHubExceptionReport(ExceptionReportSignature.Create("System.InvalidOperationException", frames),
            new string('x', 128), new string('x', 128), new string('x', 128));
        report.Body.Should().Contain(frames);
        FluentActions.Invoking(() => report.NewIssueDestination([])).Should().Throw<InvalidDataException>();
        report.Body.Should().Contain(frames);
    }

    internal static ExceptionReportSignature Signature() =>
        ExceptionReportSignature.Create("System.InvalidOperationException", "Tests.Example::Operation()");

    internal static GitHubExceptionReport Create() => new(Signature(), "1.2.3-beta1", "10.0.26100", "X64");
}
