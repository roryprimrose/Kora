using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AwesomeAssertions;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Kora.Mg1;

public sealed class HostTests
{
    private static readonly JsonSerializerOptions wireOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static string ProposalJson(int bytes)
    {
        var prefix = "{\"operation\":\"status\",\"target\":\"" + Envelope.Target
            + "\",\"revision\":7,\"text\":\"\u00e9\ud83d\ude42";
        const string suffix = "\"}";
        var count = bytes - Encoding.UTF8.GetByteCount(prefix + suffix);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return prefix + new string('a', count) + suffix;
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void CompleteUtf8OutputCountsFramingAndMultibyte(int bytes, bool accepted)
    {
        var json = ProposalJson(bytes);
        Encoding.UTF8.GetByteCount(json).Should().Be(bytes);
        json.Length.Should().BeLessThan(bytes);
        var output = new ProvisionalOutput();
        if (accepted)
        {
            foreach (var rune in json.EnumerateRunes()) output.Append(rune.ToString());
            output.Bytes.Should().Be(bytes);
            output.Complete().Target.Should().Be(Envelope.Target);
        }
        else
        {
            var append = () => output.Append(json);
            append.Should().Throw<InvalidDataException>().WithMessage("output-overflow");
            var complete = () => output.Complete();
            complete.Should().Throw<InvalidDataException>().WithMessage("output-overflow");
        }
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{not-json}")]
    [InlineData("{\"operation\":\"status\",\"target\":\"host-owned-synthetic-task\",\"revision\":7,\"text\":\"ok\",\"approved\":true}")]
    [InlineData("{\"operation\":\"status\",\"target\":\"host-owned-synthetic-task\",\"revision\":7,\"text\":\"ok\",\"sessionId\":\"foreign\"}")]
    [InlineData("{\"operation\":\"status\",\"target\":\"foreign-task\",\"revision\":7,\"text\":\"ok\"}")]
    [InlineData("{\"operation\":\"approve\",\"target\":\"host-owned-synthetic-task\",\"revision\":7,\"text\":\"ok\"}")]
    [InlineData("{\"operation\":\"status\",\"target\":\"host-owned-synthetic-task\",\"revision\":8,\"text\":\"ok\"}")]
    [InlineData("{\"operation\":\"status\",\"target\":\"host-owned-synthetic-task\",\"revision\":7,\"text\":null}")]
    [InlineData("{\"operation\":\"status\",\"target\":\"host-owned-synthetic-task\",\"revision\":7,\"text\":\"ok\",\"text\":\"override\"}")]
    [InlineData("{\"operation\":\"status\",\"target\":\"host-owned-synthetic-task\",\"revision\":\"7\",\"text\":\"ok\"}")]
    public void InvalidTypedOutputNeverBecomesProposal(string json)
    {
        var output = new ProvisionalOutput();
        output.Append(json);
        var complete = () => output.Complete();
        complete.Should().Throw<Exception>().Where(exception =>
            typeof(InvalidDataException).IsAssignableFrom(exception.GetType())
            || typeof(JsonException).IsAssignableFrom(exception.GetType()));
    }
    [Fact]
    public void StreamingRemainsProvisionalAndCannotBeAcceptedTwice()
    {
        var output = new ProvisionalOutput();
        output.Append(ProposalJson(128)[..^1]);
        var complete = () => output.Complete();
        complete.Should().Throw<JsonException>();
        var append = () => output.Append("}");
        append.Should().Throw<InvalidOperationException>();
    }
    [Theory]
    [InlineData(32768, true)]
    [InlineData(32769, false)]
    public void SelectionCountsEntireSerializedContext(int bytes, bool accepted)
    {
        const string history = "MG1_HISTORY:\u00e9\ud83d\ude42";
        const string prompt = "\u00e9\ud83d\ude42";
        var baseline = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { system = Envelope.System, prompt, history }));
        var padded = prompt + new string('a', bytes - baseline);
        if (accepted) Envelope.Select(padded, history);
        else
        {
            var select = () => Envelope.Select(padded, history);
            select.Should().Throw<InvalidDataException>();
        }
    }
    [Fact]
    public void FailuresConsumeThirtyAttemptsAndExactHourReturnsCapacityPerProfile()
    {
        var time = new ManualTime();
        var admission = new Admission(time);
        for (var index = 0; index < 30; index++)
        {
            var request = admission.Admit("profile-a");
            admission.End(request, true);
        }
        var denied = () => admission.Admit("profile-a");
        denied.Should().Throw<InvalidOperationException>().WithMessage("rolling-hour-budget");
        time.Advance(TimeSpan.FromHours(1) - TimeSpan.FromMilliseconds(1));
        denied.Should().Throw<InvalidOperationException>();
        var independent = admission.Admit("profile-b");
        admission.End(independent, true);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var retry = admission.Admit("profile-a");
        admission.End(retry, true);
    }
    [Fact]
    public void AbortAcknowledgementCannotReleaseUnknownSlotOrHostIdentity()
    {
        var admission = new Admission(new ManualTime());
        var request = admission.Admit("profile");
        admission.End(request, false);
        admission.Quarantined.Should().BeTrue();
        admission.LocalStatus().Should().Contain("Unknown").And.Contain("Cancel");
        var retry = () => admission.Admit("profile");
        retry.Should().Throw<InvalidOperationException>().WithMessage("termination-unknown");
        var hostile = () => admission.End(Guid.NewGuid(), true);
        hostile.Should().Throw<InvalidOperationException>().WithMessage("host-request-mismatch");
        admission.End(request, true);
        var admitted = admission.Admit("profile");
        admission.End(admitted, true);
    }
    [Fact]
    public void WallClockJumpCannotResetRollingHourAttemptBudget()
    {
        var time = new ManualTime();
        var admission = new Admission(time);
        for (var index = 0; index < 30; index++) admission.End(admission.Admit("profile"), true);
        time.JumpUtc(TimeSpan.FromHours(2));
        var retry = () => admission.Admit("profile");
        retry.Should().Throw<InvalidOperationException>().WithMessage("rolling-hour-budget");
        time.JumpUtc(TimeSpan.FromHours(-4));
        retry.Should().Throw<InvalidOperationException>();
        time.Advance(TimeSpan.FromHours(1));
        admission.End(admission.Admit("profile"), true);
    }
    [Fact]
    public async Task ConcurrentAdmissionAdmitsExactlyOneRequest()
    {
        var admission = new Admission(new ManualTime());
        var result = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            try { return (Guid?)admission.Admit("profile"); }
            catch (InvalidOperationException exception) when (exception.Message == "management-busy") { return null; }
        })));
        result.Count(value => value.HasValue).Should().Be(1);
        admission.End(result.Single(value => value.HasValue)!.Value, false);
        admission.Quarantined.Should().BeTrue();
    }
    [Fact]
    public async Task ActualUtf8LoopbackRequestsIncludeFailuresAndNoExtraForwarding()
    {
        await using var provider = new LoopbackProvider();
        provider.Start();
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        var time = new ManualTime();
        var admission = new Admission(time);
        for (var index = 0; index < 30; index++)
        {
            var request = admission.Admit("profile");
            provider.Enqueue(new Reply("error", Status: 500));
            using var content = new StringContent(JsonSerializer.Serialize(new { prompt = "\u00e9\ud83d\ude42" },
                wireOptions), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(provider.BaseUri.AbsoluteUri + "/chat/completions"), content,
                TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            admission.End(request, true);
        }
        var retry = () => admission.Admit("profile");
        retry.Should().Throw<InvalidOperationException>();
        provider.Requests.Count.Should().Be(30);
        provider.Requests.Should().OnlyContain(request => request.Bytes == Encoding.UTF8.GetByteCount(request.Body));
        provider.Requests.Should().OnlyContain(request => request.Bytes > request.Body.Length);
        time.Advance(TimeSpan.FromHours(1));
        var newRequest = admission.Admit("profile");
        provider.Enqueue(new Reply("error", Status: 429));
        using var final = new StringContent("{\"retry\":\"explicit-new-request\"}", Encoding.UTF8, "application/json");
        using var lastResponse = await http.PostAsync(new Uri(provider.BaseUri.AbsoluteUri + "/chat/completions"), final,
            TestContext.Current.CancellationToken);
        lastResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        admission.End(newRequest, true);
        provider.Requests.Count.Should().Be(31);
        provider.Errors.Should().BeEmpty();
    }
}
