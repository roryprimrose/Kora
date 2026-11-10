using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Kora, ")]
    public async Task SessionMetadata_original_typed_and_current_activation_route_exact_ids_and_question(string activation)
    {
        var fixture = new Fixture();
        const string subject = "10000000-0000-0000-0000-000000000001";
        const string caller = "10000000-0000-0000-0000-000000000002";
        var routed = 0;
        fixture.ViewModel.SessionMetadataRequested += (command, origin, admitted) =>
        {
            routed++;
            command.Subject.Should().Be(subject);
            command.Caller.Should().Be(caller);
            command.Question.Should().Be("What work remains?");
            origin.Should().Be(RequestOrigin.LocalUi);
            admitted().Should().BeTrue();
            return Task.CompletedTask;
        };
        await fixture.RunAsync(activation + $"review session metadata {subject} for {caller}: What work remains?");
        routed.Should().Be(1);
    }

    [Theory]
    [InlineData("review session metadata invalid")]
    [InlineData("review session metadata 10000000-0000-0000-0000-000000000001 for 10000000-0000-0000-0000-000000000002: Original")]
    public async Task SessionMetadata_invalid_or_unavailable_native_owner_reports_failure_without_generic_model_fallback(string text)
    {
        var fixture = new Fixture();
        await fixture.RunAsync(text);
        fixture.ViewModel.ResponseTitle.Should().NotBeNullOrWhiteSpace();
        fixture.ViewModel.ResponseBody.Should().NotContain("Succeeded");
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("system")]
    [InlineData("unactivated")]
    [InlineData("stale")]
    [InlineData("name")]
    [InlineData("pending")]
    public async Task SessionMetadata_requires_current_original_private_input_and_has_no_generic_fallback(string fault)
    {
        var f = fault is "name" or "pending" ? new Fixture() : await Fixture.CreateInitializedAsync();
        if (fault is "name")
        {
            f.NamePreferences.Name = "Supported Commands";
            await f.ViewModel.InitializeAsync();
        }
        if (fault is "pending")
        {
            f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
            f.Reasoner.Question = new("Original question?", ["one", "two"]);
            await f.ViewModel.InitializeAsync();
            await f.RunAsync("do the work");
            await f.ViewModel.ActiveReasoningTask!;
        }
        const string text = "review session metadata 10000000-0000-0000-0000-000000000001 for 10000000-0000-0000-0000-000000000002: Original";
        var calls = 0;
        f.ViewModel.SessionMetadataRequested += (_, origin, admitted) =>
        {
            calls++;
            origin.Should().Be(RequestOrigin.ActivatedVoice);
            admitted().Should().BeTrue();
            return Task.CompletedTask;
        };
        if (fault is "system")
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                HostActivityLayer.Application, HostOperation.Request);
            await f.RunAsync(text);
        }
        else if (fault is "unactivated" or "name")
        { await f.RaiseActivatedTranscriptAsync(fault is "name" ? "Kora, " + text : text, 1); }
        else if (fault is "stale")
        {
            f.Voice.AfterStart = () => f.ViewModel.BindVoiceOwnershipGate(static () => false);
            await f.RaiseActivatedTranscriptAsync("Kora, " + text, 1);
        }
        else if (fault is "voice") { await f.RaiseActivatedTranscriptAsync("Kora, " + text, 1); }
        else { await f.RunAsync(text); }
        calls.Should().Be(fault is "voice" ? 1 : 0);
        if (fault is not "pending") { f.Reasoner.Requests.Should().BeEmpty(); }
    }
}
