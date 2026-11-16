using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>Read-only exact supported per-user runtime observation. No model execution, process start, install or alternate endpoint.</summary>
public sealed partial class WindowsOllamaAdmissionObservation(ILogger<WindowsOllamaAdmissionObservation> logger)
    : IWindowsOllamaAdmissionObservation, IWindowsOllamaEvaluationObservation
{
    public Task<WindowsOllamaAdmissionSubject?> Observe(LocalModelQualificationEvidence evidence, CancellationToken cancellationToken) =>
        ObserveSubject(evidence.ExecutableSha256, (subject, token) => IsCurrent(evidence, subject, token), cancellationToken);

    public async Task<WindowsOllamaAdmissionSubject?> ObserveEvaluationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = WindowsOllamaSetupService.InstalledExecutablePath;
            RestrictedStorageDirectory.RejectReparseAncestors(path);
            using var executable = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (executable.Length is <= 0 or > 268435456)
            { throw new InvalidDataException("The supported runtime executable exceeds the evaluation bound."); }
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(executable, cancellationToken).ConfigureAwait(false));
            return await ObserveSubject(digest, IsEvaluationCurrentAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ObservationUnavailable(logger, "evaluation-executable-unavailable");
            return null;
        }
    }

    public Task<bool> IsEvaluationCurrentAsync(WindowsOllamaAdmissionSubject subject, CancellationToken cancellationToken) =>
        IsCurrent(subject.Runtime.ExecutableSha256, WindowsOllamaSetupService.PackageVersion,
            WindowsOllamaSetupService.Model, WindowsOllamaSetupService.ModelDigest[7..], subject, cancellationToken);

    private async Task<WindowsOllamaAdmissionSubject?> ObserveSubject(string executableDigest,
        Func<WindowsOllamaAdmissionSubject, CancellationToken, Task<bool>> isCurrent, CancellationToken cancellationToken)
    {
        var processes = Process.GetProcessesByName("ollama");
        try
        {
            WindowsOllamaAdmissionSubject? selected = null;
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (process.HasExited || !string.Equals(process.MainModule?.FileName,
                        WindowsOllamaSetupService.InstalledExecutablePath, StringComparison.OrdinalIgnoreCase))
                    { continue; }
                    var subject = new WindowsOllamaAdmissionSubject(new(process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()),
                        WindowsOllamaSetupService.InstalledExecutablePath, executableDigest), Account(), CurrentSession());
                    if (!await isCurrent(subject, cancellationToken).ConfigureAwait(false)) { continue; }
                    if (selected is not null)
                    {
                        ObservationUnavailable(logger, "ambiguous-process");
                        return null;
                    }
                    selected = subject;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                { ObservationUnavailable(logger, "process-inaccessible"); }
            }
            if (selected is null) { ObservationUnavailable(logger, "exact-running-runtime-not-found"); }
            return selected;
        }
        finally { foreach (var process in processes) { process.Dispose(); } }
    }

    public Task<bool> IsCurrent(LocalModelQualificationEvidence evidence, WindowsOllamaAdmissionSubject subject,
        CancellationToken cancellationToken) =>
        IsCurrent(evidence.ExecutableSha256, evidence.RuntimeVersion, evidence.Model, evidence.ModelSha256, subject, cancellationToken);

    private async Task<bool> IsCurrent(string executableDigest, string runtimeVersion, string model, string modelDigest,
        WindowsOllamaAdmissionSubject subject,
        CancellationToken cancellationToken)
    {
        if (subject.Account != Account() || subject.WindowsSessionId != CurrentSession()
            || !string.Equals(subject.Runtime.ExecutablePath, WindowsOllamaSetupService.InstalledExecutablePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(subject.Runtime.ExecutableSha256, executableDigest, StringComparison.OrdinalIgnoreCase)
            || !await WindowsOllamaToolProcessObservation.IsCurrentAsync(subject.Runtime, cancellationToken).ConfigureAwait(false))
        {
            ObservationUnavailable(logger, "account-session-process-changed");
            return false;
        }
        try
        {
            using var client = WindowsOllamaToolTransport.CreateClient();
            await OllamaSdkMetadata.VerifyAsync(runtimeVersion, model, modelDigest,
                SendAsync, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return subject.Account == Account() && subject.WindowsSessionId == CurrentSession()
                && await WindowsOllamaToolProcessObservation.IsCurrentAsync(subject.Runtime, cancellationToken).ConfigureAwait(false);

            async Task<byte[]> SendAsync(HttpRequestMessage message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (!await WindowsOllamaToolProcessObservation.IsCurrentAsync(subject.Runtime, token).ConfigureAwait(false))
                { throw new InvalidDataException("The exact local runtime changed before SDK metadata dispatch."); }
                token.ThrowIfCancellationRequested();
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (WindowsOllamaToolTransport.Status(response, message) != ModelTurnOutcome.Succeeded)
                { throw new InvalidDataException("Exact local metadata is unavailable or redirected; no substitute is authorized."); }
                return await WindowsOllamaToolTransport.ReadBoundedAsync(response.Content,
                    ModelToolProtocol.MaximumOutputUtf8Bytes, token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            ObservationUnavailable(logger, "runtime-model-metadata-unconfirmed");
            return false;
        }
    }

    private static int CurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static ProviderAccountId Account()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Current Windows account is unknown; local model admission is unavailable.");
        return new("windows." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))));
    }
}