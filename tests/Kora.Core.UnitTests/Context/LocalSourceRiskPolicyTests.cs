using System.Collections;
using AwesomeAssertions;
using Kora.Core.Context;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalSourceRiskPolicyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("  private business notes\r\nsalary and health information remain private, not proven safe \u4e00")]
    [InlineData("""{"items":[{"name":"ordinary"},42,true,false,null,[]],"empty":{}}""")]
    [InlineData("  \r\n[]")]
    [InlineData("\"ordinary\"")]
    public void CompleteOrdinaryInputsAreAdmittedOnlyAsPrivateLocalUse(string text) =>
        LocalSourceRiskPolicy.Evaluate([text], Token).Should().Be(LocalSourceRiskOutcome.AdmittedPrivateLocalOnly);

    [Theory]
    [InlineData("password = short")]
    [InlineData("api_key=example")]
    [InlineData("Authorization: Bearer")]
    [InlineData("?sig=example")]
    [InlineData("-----BEGIN ENCRYPTED PRIVATE KEY-----")]
    [InlineData("-----BEGIN PGP PRIVATE KEY BLOCK-----")]
    [InlineData("https://user:example@example.com/")]
    [InlineData("sk-proj-aaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyzABCDEFGHIJ")]
    [InlineData("""{"api\u005fkey":"example"}""")]
    [InlineData("""[{"outer":[{"value":"password\u003dshort"}]}]""")]
    [InlineData("""{"outer":{"password":null}}""")]
    [InlineData("""{"outer":["ordinary","api_key=example"]}""")]
    [InlineData("\"api\\u005fkey=example\"")]
    public void DetectedStructuresIncludingDecodedJsonAreDeniedWithoutOverride(string text) =>
        LocalSourceRiskPolicy.Evaluate([text], Token).Should().Be(LocalSourceRiskOutcome.DetectedRisk);

    [Theory]
    [InlineData("{broken")]
    [InlineData("[")]
    [InlineData(" \r\n{")]
    public void MalformedOrUnscreenableContentIsUnavailable(string text) =>
        LocalSourceRiskPolicy.Evaluate([text], Token).Should().Be(LocalSourceRiskOutcome.Unavailable);

    [Fact]
    public void EveryInputAndExactByteBoundaryIsScreenedWithoutTruncation()
    {
        var maximum = new string('a', LocalSourceRiskPolicy.MaximumPartUtf8Bytes);
        LocalSourceRiskPolicy.Evaluate([maximum, maximum], Token).Should().Be(LocalSourceRiskOutcome.AdmittedPrivateLocalOnly);
        LocalSourceRiskPolicy.Evaluate([maximum, maximum, "a"], Token).Should().Be(LocalSourceRiskOutcome.Unavailable);
        LocalSourceRiskPolicy.Evaluate([maximum + "a"], Token).Should().Be(LocalSourceRiskOutcome.Unavailable);
        LocalSourceRiskPolicy.Evaluate([new string('\u4e00', LocalSourceRiskPolicy.MaximumPartUtf8Bytes / 3 + 1)], Token)
            .Should().Be(LocalSourceRiskOutcome.Unavailable);
        LocalSourceRiskPolicy.Evaluate([maximum[..^17] + "\napi_key=example"], Token).Should().Be(LocalSourceRiskOutcome.DetectedRisk);
        LocalSourceRiskPolicy.Evaluate(Enumerable.Repeat(string.Empty, LocalSourceRiskPolicy.MaximumParts).ToArray(), Token)
            .Should().Be(LocalSourceRiskOutcome.AdmittedPrivateLocalOnly);
        LocalSourceRiskPolicy.Evaluate(Enumerable.Repeat(string.Empty, LocalSourceRiskPolicy.MaximumParts + 1).ToArray(), Token)
            .Should().Be(LocalSourceRiskOutcome.Unavailable);
        LocalSourceRiskPolicy.Evaluate([], Token).Should().Be(LocalSourceRiskOutcome.Unavailable);
        LocalSourceRiskPolicy.Evaluate([null!], Token).Should().Be(LocalSourceRiskOutcome.Unavailable);
        LocalSourceRiskPolicy.Evaluate([new string('\ud800', 1)], Token).Should().Be(LocalSourceRiskOutcome.Unavailable);
        LocalSourceRiskPolicy.Evaluate([new string('[', 33) + "0" + new string(']', 33)], Token)
            .Should().Be(LocalSourceRiskOutcome.Unavailable);
        FluentActions.Invoking(() => LocalSourceRiskPolicy.Evaluate(null!, Token)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationBeforeOrDuringEnumerationNeverReturnsAdmission(bool before)
    {
        using var cancellation = new CancellationTokenSource();
        if (before) { cancellation.Cancel(); }
        FluentActions.Invoking(() => LocalSourceRiskPolicy.Evaluate(new CancellingParts(cancellation), cancellation.Token))
            .Should().Throw<OperationCanceledException>();
    }

    private sealed class CancellingParts(CancellationTokenSource cancellation) : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] => "ordinary";
        public IEnumerator<string> GetEnumerator()
        {
            yield return "ordinary";
            cancellation.Cancel();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
