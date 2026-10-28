using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Configuration.SettingInspection;

public sealed class SettingInspectionTests
{
    private static DateTimeOffset Now => DateTimeOffset.UnixEpoch;
    private static SettingObservation[] Values(bool saved = false) =>
    [
        new(RegisteredSetting.Theme, 0, saved, 0), new(RegisteredSetting.ReaderTextScale, 100, saved, 3),
        new(RegisteredSetting.SummaryWords, 80, saved, 7), new(RegisteredSetting.SummarySentences, 3, saved, 7),
        new(RegisteredSetting.PlaybackVolume, 100, saved, 11), new(RegisteredSetting.ResponseMode, 0, saved, 19),
    ];

    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public void CompleteClosedSourceOriginalChannelAndFreshLocalTargetAreBoundExactly(bool saved, RequestOrigin origin)
    {
        var values = Values(saved);
        var review = new SettingInspectionReview(values, "What are my reading and speech settings?",
            HostRequest.Create(origin), Now);
        values[0] = new(RegisteredSetting.Theme, 2, !saved, 100);
        review.Observations[0].Value.Should().Be(0);
        review.Observations.Should().HaveCount(6);
        review.Observations.Select(item => item.Setting).Should().Equal(
            RegisteredSetting.Theme, RegisteredSetting.ReaderTextScale,
            RegisteredSetting.SummaryWords, RegisteredSetting.SummarySentences,
            RegisteredSetting.PlaybackVolume, RegisteredSetting.ResponseMode);
        review.Source.Should().Contain("System").And.Contain("100").And.Contain("Hybrid")
            .And.Contain(saved ? "confirmed saved" : "confirmed absent");
        Encoding.UTF8.GetByteCount(review.Source).Should().BeLessThan(4096, "only six closed scalar rows can enter the source");
        review.Text.Should().Contain(review.Source).And.Contain(review.Question)
            .And.Contain(SettingInspectionReview.Policy).And.Contain("NEW Local context conversation");
        review.Original.SessionId.Should().NotBe(review.Destination.SessionId);
        review.Destination.Origin.Should().Be(origin);
        var context = review.Bind(review.Destination, new(5), Now);
        context.Tools.Should().BeEmpty();
        context.ToolMode.Should().Be(ModelToolMode.None);
        context.Evidence.Should().ContainSingle();
        context.Evidence[0].Content.Should().Be(review.Source);
        context.Evidence[0].Disclosure.Should().Be(ModelEvidenceDisclosure.LocalOnly);
        context.Evidence[0].Revision.Value.Should().Be(5);
        context.UserRequest.Should().Be(review.Question);
        context.SystemPolicy.Should().Be(SettingInspectionReview.Policy);
        context.ExpiresAt.Should().Be(Now.AddSeconds(15));
        context.RequireCompleteInputLimit();
        context.Serialize().Length.Should().BeLessThanOrEqualTo(ModelContextEnvelope.MaximumInputUtf8Bytes);
        using var wire = JsonDocument.Parse(context.Serialize());
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(review.Source);
        review.Bind(review.Destination, new(5), review.ExpiresAt.AddSeconds(-1)).ExpiresAt.Should().Be(review.ExpiresAt);
        review.TryConsume().Should().BeTrue();
        review.TryConsume().Should().BeFalse();
    }

    [Theory]
    [InlineData(RegisteredSetting.Theme, -1)]
    [InlineData(RegisteredSetting.ReaderTextScale, 74)]
    [InlineData(RegisteredSetting.SummaryWords, 81)]
    [InlineData(RegisteredSetting.SummarySentences, 0)]
    [InlineData(RegisteredSetting.PlaybackVolume, 101)]
    [InlineData(RegisteredSetting.ResponseMode, 3)]
    [InlineData((RegisteredSetting)99, 0)]
    public void ClassificationRejectsAnythingOutsideClosedValidatedScalars(RegisteredSetting setting, int value) =>
        FluentActions.Invoking(() => new SettingObservation(setting, value, false, 1)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void NegativeRevisionAndIncompleteOrDuplicateSourcesAreRejected()
    {
        FluentActions.Invoking(() => new SettingObservation(RegisteredSetting.Theme, 0, false, -1))
            .Should().Throw<ArgumentOutOfRangeException>();
        foreach (var values in new[] { Values().Skip(1), Values().Append(Values()[0]) })
        {
            FluentActions.Invoking(() => new SettingInspectionReview(values, "Why?", HostRequest.Create(RequestOrigin.LocalUi), Now))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData("foreign-session")]
    [InlineData("foreign-origin")]
    [InlineData("invocation")]
    [InlineData("generation")]
    [InlineData("expired")]
    public void BindRejectsWrongTargetChannelGenerationOrLifetime(string defect)
    {
        var review = new SettingInspectionReview(Values(), "Why?", HostRequest.Create(RequestOrigin.LocalUi), Now);
        var request = review.Destination;
        if (defect is "foreign-session") { request = HostRequest.Create(RequestOrigin.LocalUi); }
        if (defect is "foreign-origin") { request = new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.ActivatedVoice); }
        if (defect is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        FluentActions.Invoking(() => review.Bind(request, defect is "generation" ? default : new(1),
            defect is "expired" ? review.ExpiresAt : Now)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("length")]
    [InlineData("bytes")]
    [InlineData("invalid-utf8")]
    [InlineData("tokens")]
    [InlineData("origin")]
    [InlineData("invocation")]
    public void InvalidQuestionOrNonOriginalInputNeverBecomesEvidence(string defect)
    {
        var request = HostRequest.Create(defect is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (defect is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        var question = defect switch
        {
            "length" => string.Join(' ', Enumerable.Repeat("x", 129)),
            "bytes" => string.Join(' ', Enumerable.Repeat("\u4e00", 128)) + "\u4e00",
            "invalid-utf8" => "x\ud800",
            "tokens" => new string('x', 65),
            _ => "Why?",
        };
        FluentActions.Invoking(() => new SettingInspectionReview(Values(), question, request, Now))
            .Should().Throw<Exception>();
    }
}
