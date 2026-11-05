using AwesomeAssertions;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Core.Commands;
using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionInstructionScopeUiTests
{
    [Theory]
    [InlineData("review", false)]
    [InlineData("confirm", false)]
    [InlineData("stop", false)]
    [InlineData("status", false)]
    [InlineData("refresh", false)]
    [InlineData("stop", true)]
    [InlineData("typed-stop", false)]
    [InlineData("voice-stop", false)]
    public async Task NativeScopeWindowCannotRetargetAfterSessionSelectionChanges(string action, bool returnToOriginal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            var original = await f.Workspace.CreateAsync(new("Original conversation"), RequestOrigin.LocalUi, f.Storage.Token);
            var other = await f.Workspace.CreateAsync(new("Other conversation"), RequestOrigin.LocalUi, f.Storage.Token);
            await f.Model.RefreshAsync();
            await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == original));
            foreach (var target in new[] { original, other })
            {
                var review = await f.Scopes.ReviewAsync(target, f.Commands.Choices.Single(),
                    static () => true, static () => true, f.Storage.Token);
                await f.Scopes.ConfirmAsync(review, static () => true, f.Storage.Token);
            }
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => f.Model, static () => null);
            try
            {
                controller.Open();
                var window = controller.Window!;
                window.Preview.Text.Should().Contain(original.Metadata!.Name.Value);
                window.Sources.SelectedItem = f.Commands.Choices.Single();
                window.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;
                f.Commands.Pending.Should().NotBeNull();
                var reads = f.Source.Reads;
                await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == other));
                if (returnToOriginal)
                { await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == original)); }

                if (action is "typed-stop" or "voice-stop")
                {
                    var origin = action is "voice-stop" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
                    await controller.Invoking(async item => await item.ExecuteAsync(
                        new(SessionCommandOperation.InstructionScopeStop), origin, static () => true, f.Storage.Token))
                        .Should().ThrowAsync<InvalidOperationException>();
                    failures.Should().BeEmpty();
                    window.Preview.Text.Should().BeEmpty();
                }
                else
                {
                    var button = action switch
                    {
                        "review" => window.Review,
                        "confirm" => window.Confirm,
                        "stop" => window.Stop,
                        "status" => window.Status,
                        "refresh" => window.Refresh,
                        _ => throw new InvalidOperationException("Unknown test action."),
                    };
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.Operation;
                    failures.Should().ContainSingle();
                    window.Preview.Text.Should().StartWith("Unconfirmed:").And.NotContain(f.Source.Snapshot!.Document.Text);
                }
                window.Confirm.IsEnabled.Should().BeFalse();
                f.Commands.Pending.Should().BeNull();
                f.Source.Reads.Should().Be(reads, "stale controls must not read the newly selected target's source");
                f.Scopes.Status(original).Should().StartWith("Applying example");
                f.Scopes.Status(other).Should().StartWith("Applying example");
                f.Adapter.Contexts.Should().BeEmpty();
                f.Storage.Count("conversation_messages").Should().Be(0);
                f.Storage.Count("scoped_grants").Should().Be(0);

                window.Close();
                await controller.CloseOperation;
                await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == other));
                controller.Open();
                var reopened = controller.Window!;
                reopened.Should().NotBeSameAs(window);
                reopened.Preview.Text.Should().Contain(other.Metadata!.Name.Value);
                reopened.Stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;
                f.Scopes.Status(other).Should().StartWith("No session instruction applies.");
                f.Scopes.Status(original).Should().StartWith("Applying example");
                var guided = await f.Conversation.SubmitAsync(original, "Original conversation request", static () => true, f.Storage.Token);
                var bare = await f.Conversation.SubmitAsync(other, "Other conversation request", static () => true, f.Storage.Token);
                guided.Message.AssistantResponse!.Body.Should().Be("Genuine guided local answer");
                bare.Message.AssistantResponse!.Body.Should().Be("Genuine context-free answer");
                f.Adapter.Guidance.Should().Equal(true, false);
                f.Storage.Count("conversation_messages").Should().Be(2);
                failures.Should().HaveCount(action is "typed-stop" or "voice-stop" ? 0 : 1);
            }
            finally { await controller.ShutdownAsync(); }
        });
    }

    [Fact]
    public async Task SelectionChangeDuringCancellationIgnoringSourceReadCannotPublishLateReview()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            var original = f.Model.SelectedSessionRecord!;
            var other = await f.Workspace.CreateAsync(new("Other conversation"), RequestOrigin.LocalUi, f.Storage.Token);
            await f.Model.RefreshAsync();
            await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == original));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Source.BeforeRead = async () => { entered.TrySetResult(); await release.Task; };
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => f.Model, static () => null);
            try
            {
                controller.Open();
                var window = controller.Window!;
                window.Sources.SelectedItem = f.Commands.Choices.Single();
                window.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await entered.Task;
                await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == other));
                controller.Operation.IsCompleted.Should().BeFalse();
                release.SetResult();
                await controller.Operation.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
                f.Commands.Pending.Should().BeNull();
                window.Confirm.IsEnabled.Should().BeFalse();
                window.Preview.Text.Should().StartWith("Unconfirmed:").And.NotContain(f.Source.Snapshot!.Document.Text);
                f.Scopes.Status(original).Should().StartWith("No session instruction applies.");
                f.Scopes.Status(other).Should().StartWith("No session instruction applies.");
                f.Adapter.Contexts.Should().BeEmpty();
                f.Storage.Count("conversation_messages").Should().Be(0);
                failures.Should().ContainSingle();
            }
            finally { release.TrySetResult(); await controller.ShutdownAsync(); }
        });
    }

    [Fact]
    public async Task NativeStaleSelectionCannotReviewSameNameReplacementAndFreshSelectionCompletesUseAndStop()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => f.Model, static () => null);
            try
            {
                controller.Open();
                var window = controller.Window!;
                var original = f.Commands.Choices.Single();
                window.Sources.SelectedItem = original;
                var replacement = f.Source.Snapshot! with
                {
                    Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                        "---\nkora-format: 1\nname: example\ndescription: Changed style\n---\nUse the explicitly reselected guidance.", false),
                        ArtifactKind.Instruction, "kora-profile", "example"),
                };
                f.Source.Snapshot = replacement;
                f.Catalogue.Publish(f.Revisions.Reconcile(
                    [replacement.Document.Definition with { OwnedInstructionRevision = replacement.Revision }]));
                f.Revisions.CompleteActivation(replacement, f.Revisions.PrepareActivation(replacement), f.Catalogue);
                f.Commands.Choices.Single().Should().NotBeSameAs(original);

                window.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
                f.Source.Reads.Should().Be(0, "a stale selection must not read its same-name replacement");
                f.Commands.Pending.Should().BeNull();
                window.Confirm.IsEnabled.Should().BeFalse();
                window.Preview.Text.Should().StartWith("Unconfirmed:").And.NotContain(replacement.Document.Text);
                f.Storage.Count("conversation_messages").Should().Be(0);
                f.Adapter.Contexts.Should().BeEmpty();
                failures.Should().ContainSingle();

                window.Refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Sources.SelectedItem = f.Commands.Choices.Single();
                f.Source.Reads.Should().Be(0, "refresh and selection are not source-read consent");
                window.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;
                window.Preview.Text.Should().Contain(replacement.Document.Text).And.NotContain("Use concise guidance.");
                window.Confirm.IsEnabled.Should().BeTrue();
                f.Adapter.Contexts.Should().BeEmpty();
                f.Storage.Count("conversation_messages").Should().Be(0);
                window.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;

                var target = f.Model.SelectedSessionRecord!;
                const string body = "Original request after explicit reselection";
                var receipt = await f.Conversation.SubmitAsync(target, body, static () => true, f.Storage.Token);
                receipt.Message.AssistantResponse!.Body.Should().Be("Genuine guided local answer");
                using (var wire = System.Text.Json.JsonDocument.Parse(f.Adapter.Contexts.Single()))
                {
                    var root = wire.RootElement;
                    root.GetProperty("UserRequest").GetString().Should().Be(body);
                    root.GetProperty("Evidence").EnumerateArray().Select(item => item.GetProperty("Content").GetString())
                        .Should().Equal(replacement.Document.Text);
                    root.GetProperty("Evidence")[0].GetProperty("Disclosure").GetInt32()
                        .Should().Be((int)Kora.Core.Dependencies.ModelEvidenceDisclosure.LocalOnly);
                    root.GetProperty("Tools").GetArrayLength().Should().Be(0);
                }
                window.Stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;
                var afterStop = await f.Conversation.SubmitAsync(target, "After stop", static () => true, f.Storage.Token);
                afterStop.Message.AssistantResponse!.Body.Should().Be("Genuine context-free answer");
                f.Adapter.Guidance.Should().Equal(true, false);
                f.Storage.Count("conversation_messages").Should().Be(2);
                f.Storage.Count("scoped_grants").Should().Be(0);
                failures.Should().ContainSingle();
            }
            finally { await controller.ShutdownAsync(); }
        });
    }

    [Theory]
    [InlineData("disabled", 0)]
    [InlineData("reload-unavailable", 0)]
    [InlineData("removed", 1)]
    [InlineData("unselected", 0)]
    public async Task NativeUnavailableChoiceCannotReadReplacementOrCreateApplicability(string fault, int expectedReads)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => f.Model, static () => null);
            try
            {
                controller.Open();
                var window = controller.Window!;
                if (fault is not "unselected") { window.Sources.SelectedItem = f.Commands.Choices.Single(); }
                if (fault is "disabled") { f.Revisions.DisableExact(f.Source.Snapshot!, f.Catalogue); }
                if (fault is "reload-unavailable") { f.Catalogue.ReportReloadFailure(new IOException("source unavailable")); }
                if (fault is "removed") { f.Source.Snapshot = null; }
                window.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
                f.Source.Reads.Should().Be(expectedReads);
                f.Commands.Pending.Should().BeNull();
                window.Confirm.IsEnabled.Should().BeFalse();
                window.Preview.Text.Should().StartWith("Unconfirmed:").And.NotContain("Use concise guidance.");
                f.Adapter.Contexts.Should().BeEmpty();
                f.Storage.Count("conversation_messages").Should().Be(0);
                f.Scopes.Status(f.Model.SelectedSessionRecord!).Should().StartWith("No session instruction applies.");
                failures.Should().ContainSingle();
            }
            finally { await controller.ShutdownAsync(); }
        });
    }

    [Fact]
    public async Task NativeRefreshAfterTargetDisappearsReportsUnavailableWithoutAnUnhandledEventFailure()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            SessionsViewModel? selected = f.Model;
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => selected, static () => null);
            controller.Open();
            var window = controller.Window!;
            selected = null;
            window.Refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Confirm.IsEnabled.Should().BeFalse();
            window.Preview.Text.Should().StartWith("Unconfirmed:");
            failures.Should().ContainSingle().Which.Should().Contain("Active session");
            f.Commands.Pending.Should().BeNull();
            await controller.ShutdownAsync();
        });
    }

    [Fact]
    public async Task CloseJoinsCancellationIgnoringReadAndCannotPublishLateReviewOrBinding()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Source.BeforeRead = async () => { entered.TrySetResult(); await release.Task; };
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => f.Model, static () => null);
            var read = controller.ExecuteAsync(new(SessionCommandOperation.InstructionScopeReview)
            { ScopeInstructionName = "example" }, RequestOrigin.ActivatedVoice, static () => true, f.Storage.Token);
            await entered.Task;
            var window = controller.Window!;
            window.Close();
            var joined = controller.CloseOperation;
            joined.IsCompleted.Should().BeFalse();
            f.Storage.Count("conversation_messages").Should().Be(0);
            release.SetResult();
            await read.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
            await joined;
            window.Preview.Text.Should().BeEmpty();
            f.Commands.Pending.Should().BeNull();
            f.Scopes.Status(f.Model.SelectedSessionRecord!).Should().StartWith("No session instruction applies.");
            f.Adapter.Guidance.Should().BeEmpty();
            failures.Should().ContainSingle();
            await controller.ShutdownAsync();
        });
    }
}
