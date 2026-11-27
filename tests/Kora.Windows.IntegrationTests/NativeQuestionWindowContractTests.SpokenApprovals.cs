using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NativeQuestionWindowContractTests
{
    [Theory]
    [InlineData(SpokenApprovalReply.Operation.Once)]
    [InlineData(SpokenApprovalReply.Operation.Session)]
    [InlineData(SpokenApprovalReply.Operation.Deny)]
    public async Task ExactNativeApprovalVoiceAnswerCompletesAndAuditsItsActualChannel(SpokenApprovalReply.Operation decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var question = await fixture.PresentAsync();
            var model = new NativeQuestionViewModel(question, fixture.Questions, fixture.Authorization,
                new(fixture.Store, fixture.Time), static () => true, fixture.Time,
                NullLogger<NativeQuestionViewModel>.Instance);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                var target = model.CaptureSpokenToolApproval(static () => true)!;
                (await target.ReviewAsync(RequestOrigin.ActivatedVoice, static () => true))
                    .Outcome.Should().Be(HostInteractionOutcome.Presented);
                var result = await target.AnswerAsync(new(decision), RequestOrigin.ActivatedVoice, static () => true);
                result.Outcome.Should().Be(decision == SpokenApprovalReply.Operation.Deny
                    ? HostInteractionOutcome.Cancelled : HostInteractionOutcome.Approved);
                model.Completion.IsCompletedSuccessfully.Should().BeTrue();
                model.Question.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
                model.CaptureSpokenToolApproval(static () => true).Should().BeNull();
                fixture.Request.Origin.Should().Be(RequestOrigin.LocalUi);
                using (var connection = fixture.OpenRaw())
                using (var read = connection.CreateCommand())
                {
                    read.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
                    using var envelope = JsonDocument.Parse((string)read.ExecuteScalar()!);
                    var audit = envelope.RootElement;
                    audit.GetProperty("ApprovalAnswerChannel").GetInt32().Should().Be((int)RequestOrigin.ActivatedVoice);
                    audit.GetProperty("Request").GetProperty("Origin").GetInt32().Should().Be((int)RequestOrigin.LocalUi);
                    audit.TryGetProperty("DetailsAnswerChannel", out _).Should().BeFalse();
                }
                fixture.Reopen();
                await fixture.Store.InitializeAsync(fixture.Token);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("call")]
    [InlineData("expired")]
    [InlineData("manual-edit")]
    [InlineData("ownership")]
    public async Task NativeApprovalReplyRefusalRetainsTheExactCardWithoutAGrant(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var question = await fixture.PresentAsync();
            var current = true;
            var model = new NativeQuestionViewModel(question, fixture.Questions, fixture.Authorization,
                new(fixture.Store, fixture.Time), static () => true, fixture.Time,
                NullLogger<NativeQuestionViewModel>.Instance);
            var target = model.CaptureSpokenToolApproval(() => current)!;
            switch (boundary)
            {
                case "call":
                    fixture.Policy = fixture.Policy with { IsProtectedCall = true, AllowSpokenApprovalsDuringCalls = false };
                    await fixture.PublishAsync();
                    break;
                case "expired": fixture.Time.Now = question.ExpiresAt; break;
                case "manual-edit": model.Edit(new(["session"])); break;
                case "ownership": current = false; break;
            }
            var result = await target.AnswerAsync(new(SpokenApprovalReply.Operation.Once),
                RequestOrigin.ActivatedVoice, static () => true);
            result.Outcome.Should().Be(HostInteractionOutcome.Denied);
            model.Completion.IsCompleted.Should().BeFalse();
            fixture.Count("scoped_grants").Should().Be(0);
            model.Question.Should().BeSameAs(question);
            model.Close("Synthetic test complete.");
        });
    }

    [Fact]
    public async Task SpokenApprovalPresentationFailureJoinsItsOperationWithoutCreatingAGrant()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var question = await fixture.PresentAsync();
            var model = new NativeQuestionViewModel(question, fixture.Questions, fixture.Authorization,
                new(fixture.Store, fixture.Time), static () => true, fixture.Time,
                NullLogger<NativeQuestionViewModel>.Instance);
            var target = model.CaptureSpokenToolApproval(static () => true)!;
            var notifications = 0;
            model.PropertyChanged += (_, _) =>
            {
                if (++notifications == 1) { throw new IOException("Synthetic presentation failure"); }
            };
            Func<Task> answer = () => target.AnswerAsync(new(SpokenApprovalReply.Operation.Once),
                RequestOrigin.ActivatedVoice, static () => true);
            await answer.Should().ThrowAsync<IOException>();
            await model.OperationQuiescence.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
            Func<Task> completion = () => model.Completion;
            await completion.Should().ThrowAsync<IOException>();
            fixture.Count("scoped_grants").Should().Be(0);
        });
    }
}
