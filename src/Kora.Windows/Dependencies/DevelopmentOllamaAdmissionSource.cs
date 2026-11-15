using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Native-reviewed exact installed preview profile; no model attestation, inference, download or process start.</summary>
public sealed class DevelopmentOllamaAdmissionSource(DevelopmentPreviewAdmission admission, TimeProvider time)
    : IWindowsOllamaQualificationSource, IAsyncDisposable
{
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private bool disposed;
    private WindowsOllamaQualification? receipt;

    public async Task AdmitAsync(string model, Func<bool> eligible, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(ModelToolProtocol.Deadline, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token, timeout.Token);
        token = linked.Token;
        var host = HostActivity.RequireCurrent();
        var lease = admission.Resolve(DevelopmentPreviewCapability.LocalModel)
            ?? throw new InvalidOperationException("Enable the local-model development preview first.");
        if (host.Request.Origin != RequestOrigin.LocalUi || host.Request.InvocationId is not null
            || !eligible() || host.Outcome != HostOperationOutcome.Unknown || host.Activity!.IsStopped)
        { throw new InvalidOperationException("Exact model selection requires current original native input."); }
        ProviderProfileRules.ValidateId(model);
        await operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            receipt = null;
            WindowsOllamaRuntimeIdentity? runtime = null;
            foreach (var process in Process.GetProcessesByName("ollama"))
            {
                using (process)
                {
                    if (process.HasExited || !string.Equals(process.MainModule?.FileName,
                        WindowsOllamaSetupService.InstalledExecutablePath, StringComparison.OrdinalIgnoreCase)) { continue; }
                    await using var image = new FileStream(WindowsOllamaSetupService.InstalledExecutablePath,
                        FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(image, token).ConfigureAwait(false));
                    var observed = new WindowsOllamaRuntimeIdentity(process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()), image.Name, hash);
                    if (!await WindowsOllamaToolProcessObservation.IsCurrentAsync(observed, token).ConfigureAwait(false)) { continue; }
                    if (runtime is not null) { throw new InvalidDataException("Local model process identity is ambiguous."); }
                    runtime = observed;
                }
            }
            if (runtime is null) { throw new InvalidOperationException("Start the installed local Ollama runtime, then review again. Kora will not start or replace it."); }
            using var http = WindowsOllamaToolTransport.CreateClient();
            async Task<byte[]> Read(string path)
            {
                if (!eligible() || !lease.IsCurrent || host.Activity!.IsStopped || host.Outcome != HostOperationOutcome.Unknown
                    || !await WindowsOllamaToolProcessObservation.IsCurrentAsync(runtime, token).ConfigureAwait(false))
                { throw new InvalidOperationException("Preview or runtime identity changed during model review."); }
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(WindowsOllamaToolTransport.Endpoint, path));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (WindowsOllamaToolTransport.Status(response, request) != ModelTurnOutcome.Succeeded)
                { throw new InvalidDataException("Local metadata was refused; no alternate runtime is selected."); }
                return await WindowsOllamaToolTransport.ReadBoundedAsync(response.Content, 4096, token).ConfigureAwait(false);
            }
            var versionBytes = await Read("api/version").ConfigureAwait(false);
            using var versionJson = JsonDocument.Parse(versionBytes);
            var version = versionJson.RootElement.GetProperty("version").GetString()
                ?? throw new InvalidDataException("Runtime version is missing.");
            OllamaToolWire.VerifyVersion(versionBytes, version);
            var tags = await Read("api/tags").ConfigureAwait(false);
            using var models = JsonDocument.Parse(tags);
            var selected = models.RootElement.GetProperty("models").EnumerateArray()
                .SingleOrDefault(item => string.Equals(item.GetProperty("name").GetString(), model, StringComparison.Ordinal));
            if (selected.ValueKind == JsonValueKind.Undefined) { throw new InvalidOperationException("The exact selected model is not installed."); }
            var digest = selected.GetProperty("digest").GetString() ?? "";
            if (digest.StartsWith("sha256:", StringComparison.Ordinal)) { digest = digest[7..]; }
            OllamaToolWire.VerifyModel(tags, model, digest);
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Current local account is unknown.");
            var account = new ProviderAccountId("preview." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))));
            var id = Guid.NewGuid();
            var revision = new HostRevision(1);
            var tuple = new ProviderProfileTuple(ProviderService.Local, account, revision, new(id.ToString("D")), new("off"), new("bounded"));
            var entry = new ProviderCatalogueModel(tuple.Model, "Development Preview: " + model,
                new(ModelProviderIdentity.Ollama, new(id), revision),
                [new(tuple.Reasoning, "Off", false)], [new(tuple.Context, "Host bounded", true)]);
            if (!eligible() || !lease.IsCurrent || host.Activity!.IsStopped || host.Outcome != HostOperationOutcome.Unknown
                || !await WindowsOllamaToolProcessObservation.IsCurrentAsync(runtime, token).ConfigureAwait(false))
            { throw new InvalidOperationException("Original preview review retired before runtime admission."); }
            receipt = new(Guid.NewGuid(), new(ProviderService.Local, ProviderCatalogueStatus.Current, account,
                revision, time.GetUtcNow(), lease.ExpiresAt, [entry]), runtime, version, model, digest, lease);
        }
        finally { operation.Release(); }
    }

    public async Task<WindowsOllamaQualification?> ReadAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        cancellationToken = linked.Token;
        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (receipt is null || !receipt.IsCurrent(time.GetUtcNow())) { receipt = null; return null; }
            if (!await WindowsOllamaToolProcessObservation.IsCurrentAsync(receipt.Runtime, cancellationToken).ConfigureAwait(false))
            { receipt = null; return null; }
            return receipt;
        }
        finally { operation.Release(); }
    }
    public ValueTask DisposeAsync()
    {
        lock (disposalGate) { return new(disposal ??= CloseAsync()); }
    }
    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await operation.WaitAsync().ConfigureAwait(false);
        try { receipt = null; }
        finally { operation.Release(); operation.Dispose(); lifetime.Dispose(); }
    }
}
