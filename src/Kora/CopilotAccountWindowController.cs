using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kora.Application.Infrastructure;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class CopilotAccountWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly CopilotAccountBroker accounts;
    private readonly AudioControlAdmission admission;
    private readonly ILogger<CopilotAccountWindowController> logger;
    private readonly CopilotEvaluationDeploymentSource? evaluation;
    private PrivateReviewView? view;
    private Func<Window>? getWorkbench;
    internal PrivateReviewView? PresentedView => view;
    internal void BindWorkbench(Func<Window> owner) => getWorkbench = owner;
    private TextBox? login;
    private TextBox? credential;
    private CancellationTokenSource? lifetime;
    private bool disposed;
    private int operations;
    private Task work = Task.CompletedTask;
    internal Task Operation => work.WaitAsync(CancellationToken.None);

    internal CopilotAccountWindowController(MainViewModel main, CopilotAccountBroker accounts,
        AudioControlAdmission admission, ILogger<CopilotAccountWindowController> logger,
        CopilotEvaluationDeploymentSource? evaluation = null)
    {
        this.main = main;
        this.accounts = accounts;
        this.admission = admission;
        this.logger = logger;
        this.evaluation = evaluation;
        main.CopilotAccountSetupRequested += OnRequested;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.BindCopilotSetupQuiescence(() => Volatile.Read(ref operations) == 0 && accounts.IsQuiescent);
    }

    private void OnRequested(object? sender, EventArgs args)
    {
        if (disposed || !main.CanRevealPrivatePresentation || Volatile.Read(ref operations) != 0) { return; }
        if (view is not null) { view.Activate(); return; }
        var eligible = main.CaptureHandoffPresentationEligibility();
        var cancellation = lifetime = new();
        login = new TextBox { PlaceholderText = "Exact GitHub login", MaxLength = 128, IsEnabled = false };
        credential = new TextBox { PlaceholderText = "Token (memory only)", PasswordChar = '*', MaxLength = 8192, IsEnabled = false };
        var useAccount = new CheckBox { Content = "Use this exact account. Every hosted request still requires separate disclosure approval.", IsChecked = false };
        var status = new TextBlock { Text = "Runtime and account-use deployment approval required. No ambient login, token persistence or automatic runtime installation.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        PrivateReviewView? opened = null;
        bool OriginalLive() => !disposed && ReferenceEquals(view, opened) && main.CanRevealPrivatePresentation && eligible();
        bool Live() => OriginalLive() && opened?.IsPresented == true;
        var signIn = new Button { Content = "Verify this account and read its model catalogue", IsEnabled = false };
        signIn.Command = new AsyncCommand(async () =>
        {
            if (!Live() || useAccount.IsChecked != true) { status.Text = "Explicit account-use confirmation is required."; return; }
            var enteredLogin = login.Text ?? string.Empty;
            var token = credential.Text ?? string.Empty;
            credential.Text = string.Empty;
            Interlocked.Increment(ref operations);
            signIn.IsEnabled = false;
            try
            {
                work = admission.RunObservedAsync(RequestOrigin.LocalUi, Live,
                    async ct => { await accounts.SignInAsync(enteredLogin, token, Live, ct).ConfigureAwait(false); return true; },
                    (_, _, result) => result, cancellation.Token);
#pragma warning disable VSTHRD003 // Keep the actual account authentication operation owned through cancellation and physical release.
                await work;
#pragma warning restore VSTHRD003
                if (Live())
                { status.Text = "Account verified. Refresh services/models, select this account's exact model and save. This is not transmission consent."; }
            }
            catch { accounts.Revoke(); throw; }
            finally
            {
                token = string.Empty;
                Interlocked.Decrement(ref operations);
                if (Live()) { signIn.IsEnabled = true; }
            }
        }, exception =>
        {
            SetupFailed(logger, exception.GetType().Name);
            if (Live()) { status.Text = "Unavailable: account setup was not confirmed. Runtime/account-use approval, exact authentication and current catalogue are required. Nothing will fall back."; }
        });
        var signOut = new Button { Content = "Forget this account" };
        signOut.Click += (_, _) =>
        {
            if (!Live()) { main.ReportHostInteractionFailure("The original Copilot account controls retired. No account was forgotten."); return; }
            try { HostRequestRunner.Run(RequestOrigin.LocalUi, accounts.Forget); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                SetupFailed(logger, exception.GetType().Name);
                main.ReportHostInteractionFailure("Account removal evidence is unconfirmed. The volatile account has been revoked.");
            }
            finally { evaluation?.Revoke(); Close(); }
        };
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12,
            Orientation = Orientation.Vertical, Children = { status, login, credential, useAccount, signIn, signOut } };
        if (evaluation is not null)
        {
            panel.Children.Insert(0, CreateEvaluationControls(Live,
                () => !disposed && main.CanRevealPrivatePresentation && eligible(),
                () =>
                {
                    login!.IsEnabled = credential!.IsEnabled = signIn.IsEnabled = true;
                    status.Text = "Live evaluation admitted for one hour. Enter your exact GitHub login and a Copilot-capable token, then confirm account use. Every model request still requires separate disclosure review.";
                }, cancellation.Token));
        }
        var owner = getWorkbench?.Invoke() ?? new Window { Width = 650, Height = 400 };
        opened = new(owner, new ScrollViewer { Content = panel }, OriginalLive);
        view = opened;
        opened.Closed += (_, _) => { if (ReferenceEquals(view, opened)) { Close(); } };
        opened.Show("Copilot account");
        new AsyncCommand(async () =>
        {
            Interlocked.Increment(ref operations);
            try
            {
                var checking = admission.RunObservedAsync(RequestOrigin.LocalUi, Live, accounts.CanSetUpAsync,
                    (_, _, result) => result, cancellation.Token);
                work = checking;
                var available = await checking;
                if (Live())
                {
                    login!.IsEnabled = credential!.IsEnabled = signIn.IsEnabled = available;
                    if (!available) { status.Text = "Unavailable: no approved Copilot runtime/account-use deployment. No credentials are accepted and no sign-in is attempted."; }
                }
            }
            finally { Interlocked.Decrement(ref operations); }
        }, exception =>
        {
            SetupFailed(logger, exception.GetType().Name);
            if (Live()) { status.Text = "Runtime/account-use approval could not be confirmed. Account setup remains unavailable."; }
        }).Execute(null);
    }

    private void OnPrivacyClosed(object? sender, EventArgs args)
    {
        accounts.Revoke();
        evaluation?.Revoke();
        Close();
    }
    private void Close()
    {
        evaluation?.DiscardPendingReview();
        var opened = view;
        view = null;
        if (credential is not null) { credential.Text = string.Empty; credential = null; }
        if (login is not null) { login.Text = string.Empty; login = null; }
        var cancellation = lifetime;
        lifetime = null;
        try { cancellation?.Cancel(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetupFailed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("Copilot setup cancellation reported a failure. Setup remains unavailable until its owned runtime terminates.");
        }
        finally
        {
            cancellation?.Dispose();
            opened?.Dispose();
        }
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.CopilotAccountSetupRequested -= OnRequested;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        OnPrivacyClosed(null, EventArgs.Empty);
    }
}
