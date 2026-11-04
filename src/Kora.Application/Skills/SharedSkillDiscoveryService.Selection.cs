using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

public sealed partial class SharedSkillDiscoveryService
{
    internal Task<SharedSkillFileBinding> BindSelectedFileAsync(string selectedFile, Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
        {
            var registered = await admission.CommitAsync(request, session, eligible, () =>
            { RequireObservation(); return preferences.Load(); }, currentToken).ConfigureAwait(false);
            var binding = await reader.BindFileAsync(registered, selectedFile, currentToken).ConfigureAwait(false);
            return await admission.CommitAsync(request, session, eligible, () =>
            {
                RequireRegistration(binding.Source);
                if (!preferences.Load().SequenceEqual(registered))
                { throw new InvalidOperationException("The selected registration changed during metadata binding."); }
                return binding;
            }, currentToken).ConfigureAwait(false);
        }, token);

    internal Task<SharedSkillSnapshot> ReadSelectedFileAsync(SharedSkillFileBinding binding, Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
        {
            await admission.CommitAsync(request, session, eligible, () =>
            { RequireRegistration(binding.Source); return true; }, currentToken).ConfigureAwait(false);
            var snapshot = await reader.ReadFileAsync(binding, currentToken).ConfigureAwait(false);
            return await admission.CommitAsync(request, session, eligible, () =>
            { RequireRegistration(binding.Source); RequireSelectedSnapshot(binding, snapshot); return snapshot; }, currentToken).ConfigureAwait(false);
        }, token);

    internal Task ValidateSelectedForUseAsync(SharedSkillSnapshot snapshot, SharedSkillFileBinding binding,
        SharedSkillUseAdmission useAdmission, Func<bool> eligible, RequestOrigin origin, CancellationToken token) =>
        useAdmission.RunAsync(origin, eligible, async (request, session, live, currentToken) =>
        {
            await useAdmission.CommitAsync(request, session, live, () =>
            { RequireRegistration(binding.Source); return true; }, currentToken).ConfigureAwait(false);
            var current = await reader.ReadFileAsync(binding, currentToken).ConfigureAwait(false);
            return await useAdmission.CommitAsync(request, session, live, () =>
            {
                RequireRegistration(binding.Source);
                RequireSelectedSnapshot(binding, current);
                if (!current.Bytes.SequenceEqual(snapshot.Bytes))
                { throw new InvalidOperationException("The selected exact skill bytes changed."); }
                return true;
            }, currentToken).ConfigureAwait(false);
        }, token);

    private static void RequireSelectedSnapshot(SharedSkillFileBinding binding, SharedSkillSnapshot snapshot)
    {
        if (snapshot.SourceId != binding.Source.Id || !string.Equals(snapshot.RelativeFile, binding.RelativeFile, StringComparison.Ordinal)
            || !string.Equals(snapshot.PackageDirectoryIdentity, binding.PackageIdentity, StringComparison.Ordinal)
            || !snapshot.IsInstructionCompatible || snapshot.ReferenceDeclaration is not null)
        { throw new InvalidOperationException("The selected skill is not the exact registered single-file zero-tool instruction-only profile. No downgrade is available."); }
    }
}
