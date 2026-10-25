using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ClaudeAccountWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly ClaudeAccountBroker accounts;
    private readonly AudioControlAdmission admission;
    private readonly ILogger<ClaudeAccountWindowController> logger;
    private Window? window;
    private TextBox? credential;
    private CancellationTokenSource? lifetime;
    private Task work = Task.CompletedTask;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Func<bool>? eligibility;
    private bool disposed;
    internal Window? PresentedWindow => window;
    internal Task Operation => work.WaitAsync(CancellationToken.None);

    internal ClaudeAccountWindowController(MainViewModel main, ClaudeAccountBroker accounts,
        AudioControlAdmission admission, ILogger<ClaudeAccountWindowController> logger)
    {
        this.main = main; this.accounts = accounts; this.admission = admission; this.logger = logger;
        main.ClaudeAccountSetupRequested += OnRequested;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.BindClaudeShutdown(StopAsync);
        timer.Tick += OnTick;
    }

    private void OnRequested(object? sender, EventArgs args)
    {
        if (disposed || !main.CanRevealPrivatePresentation || !work.IsCompleted) { return; }
        if (window is not null) { window.Activate(); return; }
        var eligible = eligibility = main.CaptureHandoffPresentationEligibility();
        var cancellation = lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var secret = credential = new TextBox
        { PlaceholderText = "Claude API credential (memory only)", PasswordChar = '*', MaxLength = 8192, IsEnabled = false };
        secret.Name = "ClaudeCredential";
        var confirm = new CheckBox { Content = "Use the independently approved credential/workspace lease. This is not transmission consent.", IsChecked = false };
        var status = new TextBlock { Text = "Checking independent deployment, terms and cost approval. No ambient account is used.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var opened = window = new Window { Title = "Claude account", Width = 680, Height = 360 };
        bool Live() => !disposed && ReferenceEquals(window, opened) && main.CanRevealPrivatePresentation && eligible();
        var verify = new Button { Content = "Authenticate approved credential and read complete Models catalogue", IsEnabled = false };
        void Failure(Exception exception)
        {
            SetupFailed(logger, exception.GetType().Name);
            status.Text = "Unavailable: Claude approval, credential lease or complete catalogue was not confirmed. No fallback or inference was attempted.";
        }
        verify.Command = new AsyncCommand(async () =>
        {
            if (!Live() || confirm.IsChecked != true) { status.Text = "Explicit account-use confirmation is required."; return; }
            var entered = secret.Text ?? string.Empty;
            secret.Text = string.Empty;
            verify.IsEnabled = secret.IsEnabled = false;
            try
            {
                work = admission.RunObservedAsync(RequestOrigin.LocalUi, Live, async ct =>
                { await accounts.SignInAsync(entered, Live, ct).ConfigureAwait(false); return true; },
                    (_, _, result) => result, cancellation.Token);
                await work;
                if (Live()) { status.Text = "Credential authenticated for the approved workspace, not a Console identity. Refresh models and save an exact profile. Each POST still needs separate native consent."; }
            }
            finally { entered = string.Empty; }
        }, Failure);
        var forget = new Button { Content = "Forget Claude credential and catalogue" };
        forget.Click += (_, _) =>
        {
            try { HostRequestRunner.Run(RequestOrigin.LocalUi, accounts.Forget); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { Failure(exception); }
            finally { Close(); }
        };
        opened.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12,
            Children = { status, secret, confirm, verify, forget } };
        opened.Closed += (_, _) => Close();
        opened.Show(); timer.Start();
        new AsyncCommand(async () =>
        {
            var checking = admission.RunObservedAsync(RequestOrigin.LocalUi, Live, accounts.CanSetUpAsync,
                (_, _, result) => result, cancellation.Token);
            work = checking;
            var available = await checking;
            if (!Live()) { return; }
            secret.IsEnabled = verify.IsEnabled = available;
            status.Text = available ? "Independent approval permits native credential setup. Enter only the approved API credential."
                : "Unavailable: no Claude deployment/account-use terms and cost approval. Credentials are disabled; no network request was made.";
        }, Failure).Execute(null);
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) { accounts.Revoke(); Close(); }

    private void OnTick(object? sender, EventArgs args)
    {
        if (eligibility?.Invoke() != true || !main.CanRevealPrivatePresentation || lifetime?.IsCancellationRequested != false)
        { OnPrivacyClosed(null, EventArgs.Empty); }
    }

    private void Close()
    {
        timer.Stop(); eligibility = null;
        var opened = window; window = null;
        if (credential is not null) { credential.Text = string.Empty; credential = null; }
        var cancellation = lifetime; lifetime = null;
        try { cancellation?.Cancel(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { SetupFailed(logger, exception.GetType().Name); main.ReportHostInteractionFailure("Claude setup cancellation is unconfirmed; owned work must join before exit."); }
        finally
        {
            cancellation?.Dispose();
            if (opened is not null) { opened.Content = null; opened.Close(); }
        }
    }

    private async Task<bool> StopAsync()
    {
        accounts.Revoke(); Close();
        try { await work.WaitAsync(CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { SetupFailed(logger, exception.GetType().Name); }
        return accounts.IsQuiescent && work.IsCompleted;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.ClaudeAccountSetupRequested -= OnRequested;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        OnPrivacyClosed(null, EventArgs.Empty);
        timer.Tick -= OnTick;
    }
}
