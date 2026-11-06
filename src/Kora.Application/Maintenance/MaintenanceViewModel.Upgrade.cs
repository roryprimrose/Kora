using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Application.Maintenance;

public sealed partial class MaintenanceViewModel
{
    private IReleaseInstallerDownloader? installerDownloader;
    private IReleaseInstallerLauncher? installerLauncher;
    private ReleaseVersion? selectedVersion;
    private ReleaseCheck? approvedInstaller;
    private long approvedInstallerRevision;
    private string requestedVersionText = string.Empty;

    public AsyncCommand CheckSelectedVersionCommand { get; private set; } = null!;
    public AsyncCommand UpgradeCommand { get; private set; } = null!;

    public string RequestedVersionText
    {
        get => requestedVersionText;
        set
        {
            if (string.Equals(value, requestedVersionText, StringComparison.Ordinal)) { return; }
            requestedVersionText = value;
            InvalidateUpgradeApproval();
            OnPropertyChanged();
            NotifyUpgradeProperties();
        }
    }

    public bool CanReviewUpgrade => CanOpen && result!.Status == ReleaseAvailability.Available
        && architecture == ReleaseArchitecture.X64 && result.Release!.InstallerUpgradeSupported
        && installerDownloader is not null && installerLauncher is not null
        && (requestedVersionText.Length == 0 || string.Equals(requestedVersionText, result.Release!.Version.ToString(), StringComparison.Ordinal));

    public bool UpgradeApproved
    {
        get => approvedInstaller is not null && IsUpgradeApprovalCurrent(approvedInstaller, approvedInstallerRevision);
        set
        {
            if (!value)
            {
                if (approvedInstaller is not null) { operation?.Cancel(); }
                InvalidateUpgradeApproval();
            }
            else if (CanReviewUpgrade && HostActivity.Current is null)
            {
                approvedInstaller = result;
                approvedInstallerRevision = revision;
            }
            else
            {
                message = "Installer approval denied. Review the exact newer x64 release in the native window first.";
            }
            Notify();
        }
    }

    public bool CanUpgrade => !busy && UpgradeApproved;

    public string UpgradeDisclosure => "The installer is unsigned. SHA-256 verifies consistency with GitHub, not an independent publisher. "
        + "Approval downloads and runs only this exact installer. Setup must obtain graceful shutdown before changing Kora, "
        + "and starts the installed version only after success. Windows may request administrator approval. Checking never installs automatically. "
        + "Older releases without the graceful installer protocol require explicit external maintenance.";

    public void BindUpgradeServices(IReleaseInstallerDownloader downloader, IReleaseInstallerLauncher launcher)
    {
        installerDownloader = downloader;
        installerLauncher = launcher;
        NotifyUpgradeProperties();
    }

    private void InitializeUpgradeCommands()
    {
        CheckSelectedVersionCommand = new(CheckSelectedVersionAsync, ReportFailure);
        UpgradeCommand = new(UpgradeAsync, ReportFailure);
    }

    private Task<ReleaseCheck> CheckSelectedReleaseAsync(bool automatic, CancellationToken token) =>
        !automatic && selectedVersion is { } exact
            ? CheckExactVersionAsync(exact, token)
            : client.CheckAsync(channel, info.Version, architecture, token);

    private Task<ReleaseCheck> CheckExactVersionAsync(ReleaseVersion version, CancellationToken token) =>
        client is IReleaseVersionMetadataClient exact
            ? exact.CheckVersionAsync(channel, info.Version, architecture, version, token)
            : throw new InvalidOperationException("Exact-version release verification is unavailable.");

    private Task CheckSelectedVersionAsync()
    {
        selectedVersion = requestedVersionText.Length == 0 ? null : ReleaseVersion.Parse(requestedVersionText);
        return RunCheckAsync(automatic: false);
    }

    internal async Task<string> ExecuteUpdateAsync(ReleaseVersion? version, bool requestUpgrade,
        Func<bool> eligible, CancellationToken token)
    {
        string? output = null;
        await dispatcher.InvokeAsync(async () => output = await ExecuteUpdateCoreAsync(version, requestUpgrade, eligible, token));
        return output ?? throw new InvalidOperationException("Maintenance UI dispatch did not produce an outcome.");
    }

    private async Task<string> ExecuteUpdateCoreAsync(ReleaseVersion? version, bool requestUpgrade,
        Func<bool> eligible, CancellationToken token)
    {
        var host = HostActivity.RequireCurrent();
        if (host.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || !eligible() || !admitted())
        { throw new InvalidOperationException("A fresh original-user maintenance request and current host admission are required."); }
        token.ThrowIfCancellationRequested();
        if (!networkEnabled)
        { return "Update checking is disabled. Enable public GitHub release checks in native Maintenance settings; this tool cannot grant network consent."; }
        if (busy) { return "Maintenance is busy. No second check, approval or installer was started."; }
        selectedVersion = version;
        RequestedVersionText = version?.ToString() ?? string.Empty;
        await RunCheckAsync(automatic: false, originalCommand: true, cancellationToken: token);
        token.ThrowIfCancellationRequested();
        if (!eligible() || !admitted())
        { throw new InvalidOperationException("Maintenance request admission changed before its result."); }
        return MaintenanceCommandParser.BoundOutput(Status + "\n" + VerificationStatus
            + (CanReview ? "\n" + ReleaseDetails : string.Empty)
            + (requestUpgrade ? "\nOpen native Maintenance, review this exact release, and separately approve downloading and running its unsigned installer. This tool did not approve or start installation." : string.Empty));
    }

    public async Task UpgradeAsync()
    {
        var originalNativeInput = HostActivity.Current is null;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Runtime);
        var record = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "maintenance.start-installer", SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser, "maintenance.canonical-release");
        audit.Write(record);
        var target = approvedInstaller;
        if (!originalNativeInput || target is null || !CanUpgrade)
        {
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Denied, "exact-native-installer-approval-required"));
            message = "Installer startup denied. A fresh exact native release review and separate installer approval are required.";
            activity.Complete(HostOperationOutcome.Failed);
            Notify();
            return;
        }
        var expectedRevision = revision;
        busy = true;
        operation = new();
        var token = operation.Token;
        var launchRequested = false;
        bool Current() => !token.IsCancellationRequested && IsUpgradeApprovalCurrent(target, expectedRevision);
        Notify();
        try
        {
            var metadata = target.Release!;
            var installer = ReleaseInstallerDownloader.InstallerAsset(metadata);
            await VerifyCurrentAsync();
            await using (var downloaded = await installerDownloader!.DownloadAsync(metadata, token))
            {
                await VerifyCurrentAsync();
                if (!Current()) { throw new InvalidOperationException("Installer approval changed before startup."); }
                launchRequested = true;
                await installerLauncher!.LaunchAsync(downloaded.Path, installer, Current, token);
            }
            if (!Current())
            {
                audit.Write(record.WithOutcome(SecurityAuditOutcome.Unknown, "installer-start-outcome-unconfirmed"));
                message = "Installer startup outcome is unconfirmed after host admission changed. Inspect setup; no rollback or completed upgrade is claimed.";
                activity.Complete(HostOperationOutcome.Unknown);
                return;
            }
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
            message = "The reviewed installer was started. Complete setup to upgrade; Kora does not claim installation success from process startup.";
            activity.Complete(HostOperationOutcome.Completed);

            async Task VerifyCurrentAsync()
            {
                if (!Current()) { throw new InvalidOperationException("Upgrade approval or host admission changed."); }
                var fresh = await CheckExactVersionAsync(metadata.Version, token);
                PreserveRetryDeadline(fresh);
                if (!Current() || fresh.Status != ReleaseAvailability.Available || fresh.VerifiedAt is null
                    || fresh.Release is not { } release || release.Id != metadata.Id || release.Version != metadata.Version
                    || !string.Equals(release.SourceRevision, metadata.SourceRevision, StringComparison.Ordinal)
                    || release.Architecture != metadata.Architecture
                    || release.InstallerProtocol != metadata.InstallerProtocol
                    || !release.Assets.SequenceEqual(metadata.Assets))
                { throw new InvalidDataException("The exact approved release could not be reverified unchanged. No installer was authorized."); }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException
            or UnauthorizedAccessException or HttpRequestException or OperationCanceledException)
        {
            audit.Write(record.WithOutcome(launchRequested ? SecurityAuditOutcome.Unknown
                : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                launchRequested ? "installer-start-outcome-unconfirmed" : "installer-not-started"));
            ReportFailure(exception);
            message = launchRequested
                ? "Installer startup outcome is unconfirmed. Inspect setup before retrying; no rollback or completed upgrade is claimed."
                : "Upgrade stopped before installer startup: " + exception.Message;
            activity.Complete(launchRequested ? HostOperationOutcome.Unknown
                : exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            operation.Dispose();
            operation = null;
            busy = false;
            InvalidateUpgradeApproval();
            if (!disposed)
            {
                Schedule();
                Notify();
            }
        }
    }

    private bool IsUpgradeApprovalCurrent(ReleaseCheck target, long expectedRevision) =>
        !disposed && networkEnabled && admitted() && nativeCachedAdmission()
        && revision == expectedRevision && ReferenceEquals(target, approvedInstaller)
        && !IsStale;

    private void InvalidateUpgradeApproval()
    {
        approvedInstaller = null;
        approvedInstallerRevision = 0;
    }

    private void NotifyUpgradeProperties()
    {
        OnPropertyChanged(nameof(UpgradeApproved));
        OnPropertyChanged(nameof(CanUpgrade));
        OnPropertyChanged(nameof(CanReviewUpgrade));
    }
}
