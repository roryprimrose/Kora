using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Application.Dependencies;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AccountWorkbenchTests
{
    [Fact]
    public async Task Copilot_evaluation_requires_independent_runtime_and_account_consent_before_models_are_selectable()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = NativeUxTestFixture.Create(OwnedStorageFixture.ScratchParent());
            await native.InitializeAsync();
            using var paths = new OwnedStorageFixture();
            var executable = Path.Combine(paths.LocalRoot, "copilot.exe");
            await File.WriteAllTextAsync(executable, "synthetic-runtime-never-executed", native.Token);
            var f = new CopilotAccountBrokerTests.Fixture();
            using var evaluation = new CopilotEvaluationDeploymentSource(paths, f.Time, f,
                NullLogger<CopilotEvaluationDeploymentSource>.Instance);
            var starts = 0;
            await using var broker = new CopilotAccountBroker(evaluation, f.Time, f,
                _ => { starts++; return f.Client; }, NullLogger<CopilotAccountBroker>.Instance);
            await using var admission = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            using var controller = new CopilotAccountWindowController(native.Main, broker, admission,
                NullLogger<CopilotAccountWindowController>.Instance, evaluation);
            var shell = new ResponseWindow(native.Main);
            controller.BindWorkbench(() => shell);
            try
            {
                native.Main.ConfigureCopilotAccount();
                await controller.Operation;
                Dispatcher.UIThread.RunJobs();
                var view = controller.PresentedView!;
                var boxes = view.GetVisualDescendants().OfType<TextBox>().ToArray();
                var secret = boxes.Single(box => box.PasswordChar == '*');
                secret.IsEnabled.Should().BeFalse();
                boxes.Single(box => box.PlaceholderText!.StartsWith("Installed Copilot", StringComparison.Ordinal)).Text = executable;
                var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
                var inspect = buttons.Single(button => button.Content is string caption && caption.StartsWith("Inspect installed", StringComparison.Ordinal));
                await ((AsyncCommand)inspect.Command!).ExecuteAsync();
                starts.Should().Be(0);
                var enable = buttons.Single(button => button.Content is string caption && caption.StartsWith("Enable this reviewed", StringComparison.Ordinal));
                var consents = view.GetVisualDescendants().OfType<CheckBox>().ToArray();
                await ((AsyncCommand)enable.Command!).ExecuteAsync();
                secret.IsEnabled.Should().BeFalse();
                consents.Single(box => box.Content is string caption && caption.StartsWith("I approve executing", StringComparison.Ordinal)).IsChecked = true;
                await ((AsyncCommand)enable.Command!).ExecuteAsync();
                secret.IsEnabled.Should().BeTrue();
                starts.Should().Be(0);
                boxes.Single(box => string.Equals(box.PlaceholderText, "Exact GitHub login", StringComparison.Ordinal)).Text = "selected-user";
                secret.Text = "synthetic-token";
                var signIn = buttons.Single(button => button.Content is string caption && caption.StartsWith("Verify this account", StringComparison.Ordinal));
                await ((AsyncCommand)signIn.Command!).ExecuteAsync();
                starts.Should().Be(0);
                consents.Single(box => box.Content is string caption && caption.StartsWith("Use this exact account", StringComparison.Ordinal)).IsChecked = true;
                await ((AsyncCommand)signIn.Command!).ExecuteAsync();
                starts.Should().Be(1);
                secret.Text.Should().BeEmpty();
                var catalogue = await broker.ObserveAsync(native.Token);
                catalogue.Models.Should().ContainSingle();
                var model = catalogue.Models.Single();
                var profile = new ProviderProfileTuple(ProviderService.Copilot, catalogue.Account!, catalogue.Revision,
                    model.Id, new("default"), new("default"));
                (await broker.ResolveAsync(profile, native.Token))!.IsEvaluation.Should().BeTrue();
                await using var runtime = new CopilotSdkTurnRuntime(broker, f.Time, NullLogger<CopilotSdkTurnRuntime>.Instance);
                var factory = new CopilotModelProviderFactory(null!, broker, runtime,
                    new HostedTransmissionReviewService(f, f.Time), f.Time);
                var registration = (await factory.ResolveAsync(model.Selection, profile, native.Token))!;
                registration.Evidence.Should().HaveFlag(ModelQualificationGate.UserReviewedCopilotEvaluation);
                registration.Evidence.Should().NotHaveFlag(ModelQualificationGate.AllPathLifecycle);
                registration.IsQualified(f.Time.GetUtcNow()).Should().BeTrue();
                evaluation.Revoke();
                (await broker.ResolveAsync(profile, native.Token)).Should().BeNull();
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
