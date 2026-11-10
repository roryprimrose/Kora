using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AccountWorkbenchTests
{
    [Theory]
    [InlineData("unavailable")]
    [InlineData("authenticate")]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("tab")]
    public async Task HostedClaudeCredentialReviewKeepsIndependentApprovalAndClearsSecretOnWithdrawal(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = NativeUxTestFixture.Create(OwnedStorageFixture.ScratchParent());
            await native.InitializeAsync();
            var f = new ClaudeAccountBrokerTests.Fixture { Approved = action is not "unavailable" };
            await using var broker = f.Create();
            await using var admission = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            using var controller = new ClaudeAccountWindowController(native.Main, broker, admission,
                NullLogger<ClaudeAccountWindowController>.Instance);
            var shell = new ResponseWindow(native.Main);
            controller.BindWorkbench(() => shell);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            try
            {
                native.Main.ConfigureClaudeAccount();
                await controller.Operation;
                Dispatcher.UIThread.RunJobs();
                var view = controller.PresentedView!;
                var secret = view.GetVisualDescendants().OfType<TextBox>().Single();
                secret.PasswordChar.Should().Be('*');
                secret.IsEnabled.Should().Be(action is not "unavailable");
                f.Requests.Should().BeEmpty();
                if (action is "authenticate")
                {
                    secret.Text = ClaudeAccountBrokerTests.Fixture.Credential;
                    var signIn = view.GetVisualDescendants().OfType<Button>().Single(button =>
                        button.Content is string caption && caption.StartsWith("Authenticate approved", StringComparison.Ordinal));
                    await ((AsyncCommand)signIn.Command!).ExecuteAsync();
                    f.Requests.Should().BeEmpty("entering a credential is not account-use approval");
                    view.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
                    await ((AsyncCommand)signIn.Command!).ExecuteAsync();
                    f.Requests.Should().ContainSingle().Which.Method.Should().Be("GET");
                    secret.Text.Should().BeEmpty();
                    (await broker.ObserveAsync(native.Token)).Models.Should().NotBeEmpty();
                }
                else { secret.Text = "synthetic-unsent-secret"; }
                Withdraw(shell, action);
                controller.PresentedView.Should().BeNull();
                secret.Text.Should().BeEmpty();
                view.Content.Should().BeNull();
                native.Main.CommandText.Should().NotContain("synthetic-unsent-secret");
                native.Main.ResponseBody.Should().NotContain("synthetic-unsent-secret");
                if (action is not "authenticate") { f.Requests.Should().BeEmpty(); }
                windows.Distinct().Should().Equal(shell);
            }
            finally { shell.Close(); }
        });
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("authenticate")]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("tab")]
    [InlineData("hold-authentication")]
    public async Task HostedCopilotCredentialReviewRetainsExactAccountAndJoinsIgnoringAuthentication(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = NativeUxTestFixture.Create(OwnedStorageFixture.ScratchParent());
            await native.InitializeAsync();
            var f = new CopilotAccountBrokerTests.Fixture { Approved = action is not "unavailable" };
            var pending = new TaskCompletionSource<GitHub.Copilot.GetAuthStatusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (action is "hold-authentication")
            { f.Client.Authenticate = _ => { entered.TrySetResult(); return pending.Task; }; }
            await using var broker = f.Create();
            await using var admission = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            using var controller = new CopilotAccountWindowController(native.Main, broker, admission,
                NullLogger<CopilotAccountWindowController>.Instance);
            var shell = new ResponseWindow(native.Main);
            controller.BindWorkbench(() => shell);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            Task? authenticating = null;
            try
            {
                native.Main.ConfigureCopilotAccount();
                await controller.Operation;
                Dispatcher.UIThread.RunJobs();
                var view = controller.PresentedView!;
                var secret = view.GetVisualDescendants().OfType<TextBox>().Single(control => control.PasswordChar == '*');
                var login = view.GetVisualDescendants().OfType<TextBox>().Single(control => control.PasswordChar != '*');
                secret.IsEnabled.Should().Be(action is not "unavailable");
                f.Starts.Should().Be(0);
                login.Text = "selected-user";
                secret.Text = "synthetic-token";
                if (action is "authenticate" or "hold-authentication")
                {
                    var signIn = view.GetVisualDescendants().OfType<Button>().Single(button =>
                        button.Content is string caption && caption.StartsWith("Verify this account", StringComparison.Ordinal));
                    await ((AsyncCommand)signIn.Command!).ExecuteAsync();
                    f.Starts.Should().Be(0, "credential entry is not account-use approval");
                    view.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
                    authenticating = ((AsyncCommand)signIn.Command!).ExecuteAsync();
                    if (action is "hold-authentication") { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), native.Token); }
                    else
                    {
                        await authenticating;
                        f.Starts.Should().Be(1);
                        f.Options!.UseLoggedInUser.Should().BeFalse();
                        f.Client.Disposals.Should().Be(1);
                        (await broker.ObserveAsync(native.Token)).Models.Should().NotBeEmpty();
                    }
                    secret.Text.Should().BeEmpty();
                }
                Withdraw(shell, action);
                view.Content.Should().BeNull();
                controller.PresentedView.Should().BeNull();
                secret.Text.Should().BeEmpty();
                login.Text.Should().BeEmpty();
                if (action is "hold-authentication")
                {
                    await f.Client.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), native.Token);
                    authenticating!.IsCompleted.Should().BeFalse();
                    controller.Operation.IsCompleted.Should().BeFalse();
                    broker.IsQuiescent.Should().BeFalse();
                    f.Client.Disposals.Should().Be(1, "SDK disposal is requested before joining an ignoring authentication call");
                    native.Main.ConfigureCopilotAccount();
                    controller.PresentedView.Should().BeNull("pending authentication still owns setup despite hidden controls");
                    pending.TrySetResult(f.Client.Auth);
                    Func<Task> cancelled = async () => await authenticating;
                    await cancelled.Should().ThrowAsync<OperationCanceledException>();
                    broker.IsQuiescent.Should().BeTrue();
                    f.Client.Disposals.Should().Be(1);
                    (await broker.ObserveAsync(native.Token)).Models.Should().BeEmpty();
                }
                else if (action is not "authenticate") { f.Starts.Should().Be(0); }
                native.Main.CommandText.Should().NotContain("synthetic-token");
                native.Main.ResponseBody.Should().NotContain("synthetic-token");
                windows.Distinct().Should().Equal(shell);
            }
            finally
            {
                pending.TrySetResult(f.Client.Auth);
                shell.Close();
                if (authenticating is not null)
                {
                    try { await authenticating; }
                    catch (OperationCanceledException) { }
                }
            }
        });
    }

    private static void Withdraw(ResponseWindow shell, string action)
    {
        if (action is "minimize")
        { shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; }
        else if (action is "tab")
        {
            var other = new TextBlock { Text = "Another private review" };
            shell.ShowNativeReview(other, "Another private review", other, static () => true, _ => { });
        }
        else { shell.Hide(); shell.Show(); shell.Activate(); }
    }
}
