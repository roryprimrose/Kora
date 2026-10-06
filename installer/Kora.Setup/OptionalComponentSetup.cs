using System.Security.Principal;
using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora.Setup;

internal sealed class OptionalComponentSetup(ILogger<KokoroTextToSpeechProvider> logger) : IOptionalComponentSetup
{
    public async Task PrepareAsync(OptionalComponents components, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(progress);
        // Burn's engine elevates separately. Per-user assets must belong to the interactive BA user.
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException(
                "Optional components require non-elevated setup. Close setup and run it normally, not as administrator.");
        }

        if (components.PowerShell)
        {
            progress.Report("Installing or reusing PowerShell 7 for this user (Microsoft; MIT).");
            await new WindowsPowerShellSetupService().InstallAsync(cancellationToken);
            progress.Report("PowerShell 7 verified.");
        }

        if (components.LocalInference)
        {
            progress.Report("Preparing Ollama and the pinned qwen3:1.7b model for this user.");
            using var httpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
            }) { Timeout = TimeSpan.FromMinutes(30) };
            using var ollama = new WindowsOllamaSetupService(httpClient);
            await ollama.InstallAsync(new Progress<LocalModelSetupProgress>(update => progress.Report(update.Detail)),
                cancellationToken);
        }

        if (components.Kokoro)
        {
            progress.Report("Downloading and verifying Kokoro neural voice assets for this user (about 229 MB).");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var kokoro = new KokoroTextToSpeechProvider(new ApplicationDataPaths(developmentBuild: false),
                client, logger);
            await kokoro.InstallAsync(new Progress<SpeechProviderInstallProgress>(update =>
                progress.Report($"Kokoro: {update.Stage}, {update.Percentage}%.")), cancellationToken);
            if (!kokoro.IsInstalled || kokoro.GetVoices().Count == 0)
            {
                throw new InvalidOperationException("Kokoro setup did not produce verified model and voice assets.");
            }

            progress.Report("Kokoro model loaded and voices verified. No speech playback or microphone capture started.");
        }
    }
}
