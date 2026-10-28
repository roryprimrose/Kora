using Kora.Core.Hosting;

namespace Kora.Windows.Skills.RuntimeSetup;

public sealed class BundledRuntimeSetupPlan
{
    private int consumed;
    internal BundledRuntimeSetupPlan(Guid owner, BundledRuntimeSetupOperation operation,
        BundledRuntimeInspection expected, HostRequest request, Func<bool> current, DateTimeOffset expires)
    { Owner = owner; Operation = operation; Expected = expected; Request = request; Current = current; Expires = expires; }

    internal Guid Owner { get; }
    internal HostRequest Request { get; }
    internal Func<bool> Current { get; }
    internal DateTimeOffset Expires { get; }
    internal bool Consume() => Interlocked.Exchange(ref consumed, 1) == 0;
    public BundledRuntimeSetupOperation Operation { get; }
    public BundledRuntimeInspection Expected { get; }

    public string Disclosure => $"{Operation} the device-local bundled-runtime registration only.\n"
        + (Expected.Installation is { } installed
            ? $"Host-selected executable: {installed.Executable}\nInstalled file version: {installed.Version}\n"
                + $"{installed.Files.Count} protected installed files; {installed.Bytes:N0} existing bytes. Download: 0 bytes. Native installation/staging: 0 bytes.\n"
                + "Provenance: independently administrator/system-owned Program Files installation; executable Microsoft Authenticode verified using local cached trust only. "
                + "Companion-file hashes are a reviewed local baseline, not an upstream signed distribution manifest or current revocation proof. "
                + "The whole installation inventory, ACLs, device/platform and host implementation are pinned. External OS loader closure remains unqualified.\n"
                + "Microsoft PowerShell: https://github.com/PowerShell/PowerShell ; MIT and installed third-party terms below.\n"
                + "Installed LICENSE.txt:\n" + installed.License + "\nInstalled ThirdPartyNotices.txt:\n" + installed.ThirdPartyNotices + "\n"
            : "No supported protected installation was observed. Forget removes only the existing Kora registration.\n")
        + "Writes: bounded atomic local Preferences/bundled-runtime-registration.txt and bundled-runtime-pending.txt records (at most 256 bytes each), unique temporary replacements and trusted audit outcomes. "
        + "Interrupted/readback/audit failures remain unconfirmed; inspect and explicitly verify again. "
        + "No runtime files are copied, installed, modified or removed. No PATH, registry, ACL, privilege, firewall or account changes. "
        + "No executable, shell, version probe, imported module or bundled script is run. Forget never uninstalls this shared/user installation. "
        + "Setup never creates a package grant or effect consent.\n" + BundledRuntimeQualification.Requirements;
}
