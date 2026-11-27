using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task RoutingAdviceRequiresNativeOptInAndCapturedConsentRetiresOnChange()
    {
        var f = await ReplyHostAsync();
        await using var setting = new RoutingAdviceConfigurationServiceTests.Fixture();
        f.ViewModel.IsRoutingAdviceEnabled.Should().BeFalse();
        f.ViewModel.CanChangeRoutingAdvice.Should().BeFalse();
        f.ViewModel.RoutingAdviceStatus.Should().Contain("unavailable");
        f.ViewModel.CaptureRoutingAdviceConsent().Should().BeNull();
        f.ViewModel.BindRoutingAdviceConfiguration(setting.Service);
        f.ViewModel.IsRoutingAdviceEnabled.Should().BeFalse();
        f.ViewModel.RoutingAdviceStatus.Should().Contain("Off (default)");
        f.ViewModel.CanChangeRoutingAdvice.Should().BeFalse();
        await f.ViewModel.EnableRoutingAdviceCommand.ExecuteAsync();
        setting.Store.Writes.Should().Be(0);
        f.ViewModel.BindRoutingAdviceNativeLifetime(static () => true);
        await f.ViewModel.EnableRoutingAdviceCommand.ExecuteAsync();
        f.ViewModel.RoutingAdviceStatus.Should().StartWith("On.");
        var admitted = f.ViewModel.CaptureRoutingAdviceConsent()!;
        admitted().Should().BeTrue();
        await f.ViewModel.DisableRoutingAdviceCommand.ExecuteAsync();
        admitted().Should().BeFalse();
        f.ViewModel.CaptureRoutingAdviceConsent().Should().BeNull();
        f.Voice.StartCalls.Should().Be(0);
        await f.ViewModel.EnableRoutingAdviceCommand.ExecuteAsync();
        admitted = f.ViewModel.CaptureRoutingAdviceConsent()!;
        await f.ViewModel.DisposeAsync();
        admitted().Should().BeFalse();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("corrupt")]
    [InlineData("save")]
    [InlineData("native-lost")]
    [InlineData("native-replaced")]
    public async Task RoutingAdviceFailuresCannotAuthorizeDisclosure(string boundary)
    {
        var f = await ReplyHostAsync();
        await using var setting = new RoutingAdviceConfigurationServiceTests.Fixture();
        if (boundary is "load") { setting.Store.LoadFailure = new UnauthorizedAccessException(); }
        if (boundary is "corrupt") { setting.Store.Value = "bad"; }
        f.ViewModel.BindRoutingAdviceConfiguration(setting.Service);
        var native = true;
        f.ViewModel.BindRoutingAdviceNativeLifetime(() => native);
        if (boundary is "load" or "corrupt") { f.ViewModel.CanChangeRoutingAdvice.Should().BeFalse(); }
        if (boundary is "save") { setting.Store.SaveFailure = new IOException(); }
        setting.Backing.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.ViewModel.CanChangeRoutingAdvice.Should().BeFalse();
            if (boundary is "native-lost") { native = false; }
            if (boundary is "native-replaced") { f.ViewModel.BindRoutingAdviceNativeLifetime(static () => true); }
        };
        if (boundary is "save")
        {
            var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(f.ViewModel.ResponseTitle)
                    && f.ViewModel.ResponseTitle is "Routing advice setting not confirmed.") { failure.TrySetResult(); }
            };
            f.ViewModel.EnableRoutingAdviceCommand.Execute(null);
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
        else { await f.ViewModel.EnableRoutingAdviceCommand.ExecuteAsync(); }
        f.ViewModel.IsRoutingAdviceEnabled.Should().BeFalse();
        f.ViewModel.CaptureRoutingAdviceConsent().Should().BeNull();
        f.Voice.StartCalls.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("typed")]
    [InlineData("retired")]
    [InlineData("foreground")]
    public async Task DestinationRoutingCarriesOnlyAnEligibleOriginalVoiceLastSelection(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        var last = new ForegroundConversationTarget(store.Session, static () => true);
        f.ViewModel.PublishLastSelectedConversationTarget(last);
        if (mode is "retired") { last.Retire(); }
        if (mode is "foreground") { f.ViewModel.PublishForegroundConversationTarget(new(store.Session, static () => true)); }
        var calls = 0;
        f.ViewModel.BindDestinationSessionRouting((body, origin, admitted, foreground, preferred) =>
        {
            calls++;
            body.Should().Be("compare plans");
            origin.Should().Be(mode is "typed" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice);
            preferred.Should().BeSameAs(mode is "voice" ? last : null);
            (foreground is not null).Should().Be(mode is "foreground");
            admitted().Should().BeTrue();
            f.ViewModel.PublishLastSelectedConversationTarget(null);
            admitted().Should().Be(mode is not "voice");
            return Task.FromResult<SessionRoutingReceipt?>(null);
        });
        if (mode is "typed") { await f.RunAsync("compare plans"); }
        else { await f.RaiseActivatedTranscriptAsync("Kora, compare plans", 1); }
        calls.Should().Be(1);
        await f.ViewModel.DisposeAsync();
    }
}
