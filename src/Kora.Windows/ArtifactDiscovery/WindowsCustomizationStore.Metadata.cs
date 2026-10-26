using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Tools;

namespace Kora.Windows.Artifacts;

public sealed partial class WindowsCustomizationStore
{
    public Task<CustomizationMetadataObservation> ReadMetadata(CustomizationSource source, CancellationToken token) =>
        Task.Run(() =>
        {
            lock (gate)
            {
                if (!CustomizationDiscoveryTools.IsSupported(source.Kind) || source.UnavailableReason is not null)
                { throw new InvalidDataException("This exact source does not support metadata discovery."); }
                var target = Resolve(source);
                if (File.Exists(JournalPath(target)))
                { throw new InvalidOperationException("This source requires recovery. No current metadata snapshot was produced."); }
                using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token);
                var file = WindowsOwnedDefinitionFileAccess.ReadDefinitionFile(
                    Path.Combine(target.Directory, target.FileName), ArtifactDefinition.MaximumContentLength, token)
                    ?? throw new IOException("The chosen definition disappeared. Choose a fresh source.");
                var category = source.Scope == CustomizationScope.Kora ? "kora-profile" : "user-profile";
                var document = ArtifactFileDocument.Parse(file.Bytes, CustomizationLayout.ArtifactKind(source.Kind), category, source.Name);
                var digestRevision = new DiskState(pinned.Identity, [new(target.FileName, file.Bytes, file.Identity)]).Revision;
                // Restoring old bytes in place must not revive an earlier metadata review.
                var revision = digestRevision + ":" + file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new CustomizationMetadataObservation(source, revision, document.Definition.Name, document.Definition.Description,
                    document.Definition.Id, document.Definition.Digest);
            }
        }, token);
}
