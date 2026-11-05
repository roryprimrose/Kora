using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.IntegrationTests.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class HostedTransmissionDisclosureTests
{
    [Theory]
    [InlineData(true, "approve")]
    [InlineData(true, "deny")]
    [InlineData(true, "close")]
    [InlineData(true, "cancel")]
    [InlineData(true, "privacy")]
    [InlineData(false, "approve")]
    [InlineData(false, "deny")]
    [InlineData(false, "close")]
    [InlineData(false, "cancel")]
    [InlineData(false, "privacy")]
    public async Task CompleteDisclosureIsKeyboardReadableBeforeSeparateOneSendDecision(bool claude, string disposition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = NativeUxTestFixture.Create(Path.GetTempPath());
            await native.InitializeAsync();
            var f = new ClaudeAccountBrokerTests.Fixture();
            var reviews = new HostedTransmissionReviewService(f, f.Time);
            using IDisposable controller = claude
                ? new ClaudeTransmissionWindowController(native.Main, reviews, NullLogger<ClaudeTransmissionWindowController>.Instance)
                : new CopilotTransmissionWindowController(native.Main, reviews, NullLogger<CopilotTransmissionWindowController>.Instance);
            Window? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) => window = view);
            var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var now = f.Time.GetUtcNow();
            var context = new ModelContextEnvelope(request, "policy", "complete original request", [], now, now.AddMinutes(1), ModelToolMode.None);
            var profile = new ProviderProfileTuple(claude ? ProviderService.Claude : ProviderService.Copilot,
                f.Lease.Account, new(1), new("exact-model"), new("disabled"), new("8192"));
            HostedTurnAuthorization authorization = claude ? reviews.PrepareClaudeTurn(context, profile) : reviews.PrepareTurn(context, profile);
            var body = Encoding.UTF8.GetBytes("{\"prompt\":\"" + string.Concat(Enumerable.Repeat("complete request ", 1000))
                + "FINAL_DISCLOSURE_MARKER\"}");
            var transmission = new CopilotTransmission(new(new(Guid.NewGuid()), request,
                new(claude ? ModelProviderIdentity.Claude : ModelProviderIdentity.Copilot, new(Guid.NewGuid()), new(1))),
                profile, new(claude ? "https://api.anthropic.com/v1/messages" : "https://api.githubcopilot.com/chat/completions"),
                body, [new("content-type", "application/json")]);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var decision = reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(true), cancellation.Token).AsTask();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.Should().NotBeNull();
                window!.UpdateLayout();
                var reader = window.GetLogicalDescendants().OfType<TextBox>().Single();
                reader.IsReadOnly.Should().BeTrue();
                reader.IsUndoEnabled.Should().BeFalse();
                reader.Text.Should().EndWith(Encoding.UTF8.GetString(body));
                reader.Text.Should().Contain(transmission.Destination.AbsoluteUri).And.Contain("exact-model");
                ControlAutomationPeer.CreatePeerForElement(reader)!.GetName().Should().Contain("Complete").And.Contain("disclosure");
                var original = reader.Text;
                var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
                var deny = buttons.Single(button => Equals(button.Content, "Deny"));
                var consent = buttons.Single(button => !ReferenceEquals(button, deny));
                deny.IsDefault.Should().BeTrue();
                consent.IsDefault.Should().BeFalse();
                for (var tab = 0; tab < 4 && !reader.IsFocused; tab++) { Press(window, Key.Tab, PhysicalKey.Tab); }
                reader.IsFocused.Should().BeTrue();
                Press(window, Key.End, PhysicalKey.End, RawInputModifiers.Control);
                window.UpdateLayout();
                reader.CaretIndex.Should().Be(original!.Length);
                reader.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset.Y.Should().BeGreaterThan(0);
                window.KeyTextInput("MUST_NOT_EDIT");
                Press(window, Key.Back, PhysicalKey.Backspace);
                reader.Text.Should().Be(original);
                transmission.Body.ToArray().Should().Equal(body);
                decision.IsCompleted.Should().BeFalse();
                f.Audits.Where(item => item.Outcome == SecurityAuditOutcome.Succeeded).Should().BeEmpty();
                for (var tab = 0; tab < 4 && !consent.IsFocused; tab++) { Press(window, Key.Tab, PhysicalKey.Tab); }
                consent.IsFocused.Should().BeTrue();
                switch (disposition)
                {
                    case "approve":
                        Press(window, Key.Space, PhysicalKey.Space);
                        break;
                    case "deny":
                        deny.Focus().Should().BeTrue();
                        Press(window, Key.Space, PhysicalKey.Space);
                        break;
                    case "close":
                        window.Close();
                        break;
                    case "cancel":
                        cancellation.Cancel();
                        break;
                    case "privacy":
                        native.Access.SetOpen(false);
                        await native.Main.PrivacyClosureTask;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown test disposition.");
                }
                if (disposition is "cancel")
                {
                    var cancelled = async () => await decision;
                    await cancelled.Should().ThrowAsync<OperationCanceledException>();
                }
                else { (await decision).Should().Be(disposition is "approve"); }
                Dispatcher.UIThread.RunJobs();
                reader.Text.Should().BeNullOrEmpty();
                window.Content.Should().BeNull();
                f.Audits.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
                    disposition is "approve" ? SecurityAuditOutcome.Succeeded
                        : disposition is "cancel" ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied);
                f.Audits.Select(item => item.ActionId).Should().NotContain(action => action.Contains("complete request", StringComparison.Ordinal));
            }
            finally
            {
                cancellation.Cancel();
                try { await decision; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                window?.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }
}
