using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public sealed class CustomizationManagementService(ICustomizationStore store, InstructionControlAdmission admission,
    ArtifactUsePreferences use, ArtifactCatalogue catalogue, ISecurityAuditLog audit,
    ArtifactDirectoryConfigurationService? mcpConfiguration = null)
{
    public InstructionOriginalRegistration? RegisteredInstruction => store.RegisteredInstruction;
    public bool IsInstructionRegistered(CustomizationSource source) => store.IsInstructionRegistered(source);
    public void RetireInstructionRegistration(CustomizationSource source) => store.WithdrawInstruction(source);

    public Task<bool> RegisterInstruction(CustomizationSource source,
        Func<InstructionOriginalRegistration, CancellationToken, Task<bool>> confirm,
        Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (request, session, current, admitted) =>
        {
            var proposal = await store.PrepareInstructionRegistration(source, admitted).ConfigureAwait(false);
            Require(current, admitted);
            if (!await confirm(proposal, admitted).ConfigureAwait(false)) { return false; }
            var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                "definition.instruction.register-original", SecurityAuditOutcome.Requested,
                SecurityAuditInitiator.LocalUser, source.Id);
            audit.Write(entry);
            try
            {
                await store.RegisterInstruction(proposal, current, admitted).ConfigureAwait(false);
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                store.WithdrawInstruction(source);
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "registration-not-confirmed"));
                throw;
            }
        }, token);

    public Task WithdrawInstruction(CustomizationSource source, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, (request, session, current, admitted) =>
            admission.Publish(request, session, current, () =>
            {
                var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "definition.instruction.withdraw-original", SecurityAuditOutcome.Requested,
                    SecurityAuditInitiator.LocalUser, source.Id);
                try { audit.Write(entry); }
                finally { store.WithdrawInstruction(source); }
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                return true;
            }, admitted).AsTask(), token);

    public bool IsDisabled(CustomizationSource source) => use.IsDisabled(source.Id);
    public event EventHandler? UseChanged
    {
        add => use.Changed += value;
        remove => use.Changed -= value;
    }

    public Task<IReadOnlyList<CustomizationSource>> List(Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var sources = await store.List(admitted).ConfigureAwait(false);
            Require(current, admitted);
            return sources;
        }, token);

    public Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name,
        Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var source = await store.Create(kind, scope, name, admitted).ConfigureAwait(false);
            Require(current, admitted);
            return source;
        }, token);

    public Task<CustomizationSnapshot> Read(CustomizationSource source, bool recovery, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var snapshot = recovery ? await store.ReadRecovery(source, admitted).ConfigureAwait(false)
                : await store.Read(source, admitted).ConfigureAwait(false);
            Require(current, admitted);
            return snapshot;
        }, token);

    public Task RequireUnchangedSource(CustomizationSource source, CustomizationSnapshot? expected,
        Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            await RequireUnchangedSourceCore(source, expected, current, admitted).ConfigureAwait(false);
            return true;
        }, token);

    public Func<CancellationToken, Task> CaptureAuthoringSourceValidation(CustomizationSource source,
        CustomizationSnapshot? expected, Func<bool> eligible) =>
        token => RequireUnchangedSourceCore(source, expected, eligible, token);

    private async Task RequireUnchangedSourceCore(CustomizationSource source, CustomizationSnapshot? expected,
        Func<bool> eligible, CancellationToken token)
    {
        Require(eligible, token);
        if (expected is null)
        {
            var destination = await store.Create(source.Kind, source.Scope, source.Name, token).ConfigureAwait(false);
            if (destination != source) { throw new InvalidOperationException("The original draft destination changed."); }
        }
        else
        {
            var live = await store.Read(source, token).ConfigureAwait(false);
            if (live.Source != source || !string.Equals(live.Revision, expected.Revision, StringComparison.Ordinal))
            { throw new InvalidOperationException("The original editor source changed. Reload and start a fresh request."); }
        }
        Require(eligible, token);
    }

    public Task<bool> SetDisabled(CustomizationSource source, bool disabled,
        Func<CustomizationSnapshot?, CancellationToken, Task<bool>> confirm, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (request, session, current, admitted) =>
        {
            var controlRevision = use.Revision;
            var snapshot = !disabled && source.Scope != CustomizationScope.BuiltIn
                ? await store.Read(source, admitted).ConfigureAwait(false) : null;
            if (snapshot is not null)
            {
                if (snapshot.Source != source) { throw new InvalidOperationException("The selected definition source changed. Review it again."); }
                ValidatePrimary(source, snapshot.Files);
            }
            Require(current, admitted);
            if (!await confirm(snapshot, admitted).ConfigureAwait(false)) { return false; }
            if (snapshot is not null)
            {
                var live = await store.Read(source, admitted).ConfigureAwait(false);
                if (live.Source != source || !string.Equals(live.Revision, snapshot.Revision, StringComparison.Ordinal))
                { throw new InvalidOperationException("The reviewed source changed before use control. Review it again; nothing was enabled."); }
            }
            return await admission.Publish(request, session, current, () =>
            {
                if (use.Revision != controlRevision)
                { throw new InvalidOperationException("Use control changed during review. Start a fresh review."); }
                var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    disabled ? "definition.use.disable" : "definition.use.allow", SecurityAuditOutcome.Requested,
                    SecurityAuditInitiator.LocalUser, source.Id);
                audit.Write(entry);
                try
                {
                    use.SetDisabled(source.Id, disabled,
                        snapshot is not null && source.Kind != CustomizationKind.McpServer
                            ? snapshot.Files.Single(file => string.Equals(file.Name, source.PrimaryFile, StringComparison.Ordinal)).Digest : null);
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                    return true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    try { use.SetDisabled(source.Id, true); }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    { exception = new AggregateException(exception, failure); }
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Unknown, "use-control-unconfirmed"));
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
                    throw;
                }
            }, admitted).ConfigureAwait(false);
        }, token);

    public Task<CustomizationSnapshot?> Save(CustomizationSource source, CustomizationSnapshot? original,
        IReadOnlyList<CustomizationFile> files, Func<CancellationToken, Task<bool>> confirm,
        Func<bool> eligible, CancellationToken token)
    {
        var proposal = CustomizationSnapshot.ValidateFiles(source, files).ToArray();
        ValidatePrimary(source, proposal);
        if (source.Kind == CustomizationKind.McpServer
            && (source.Scope != CustomizationScope.Kora || mcpConfiguration is null))
        { throw new InvalidOperationException("Saving requires Kora's canonical MCP source and the host's independent MCP inclusion control."); }
        return admission.Run<CustomizationSnapshot?>(eligible, async (request, session, current, admitted) =>
        {
            if (!await confirm(admitted).ConfigureAwait(false)) { return null; }
            if (source is { Scope: CustomizationScope.User, Kind: CustomizationKind.Instruction }
                && original is not null && proposal.Single().CopyBytes().AsSpan().SequenceEqual(original.Files.Single().CopyBytes()))
            {
                var live = await store.Read(source, admitted).ConfigureAwait(false);
                Require(current, admitted);
                if (live.Source != source)
                { throw new InvalidOperationException("The unchanged original no longer matches the selected source. Reopen it explicitly; the draft is retained."); }
                if (!string.Equals(live.Revision, original.Revision, StringComparison.Ordinal))
                { throw new InstructionOriginalConflictException(); }
                return live;
            }
            var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ResourceWrite,
                "definition.source.save", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, source.Id);
            var attempted = false;
            audit.Write(entry);
            try
            {
                await admission.Publish(request, session, current, () =>
                {
                    use.SetDisabled(source.Id, true);
                    if (source.Kind == CustomizationKind.McpServer)
                    {
                        mcpConfiguration!.SetMcpServerIncluded(ArtifactUseIdentity.McpServerId(source.Location, source.Name), false);
                    }
                    return true;
                }, admitted).ConfigureAwait(false);
                var saved = await store.Save(source, original, proposal, current,
                    operation => admission.Publish(request, session, current, () =>
                    {
                        attempted = true;
                        return operation();
                    }, admitted).AsTask(), admitted).ConfigureAwait(false);
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                return saved;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                audit.Write(entry.WithOutcome(exception is InstructionOriginalConflictException ? SecurityAuditOutcome.Failed
                    : attempted ? SecurityAuditOutcome.Unknown
                    : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                    exception is InstructionOriginalConflictException ? "base-changed-not-published"
                        : attempted ? "publication-requires-inspection" : "not-published"));
                throw;
            }
        }, token);
    }

    public ArtifactDefinition? ObservedArtifact(CustomizationSource source) => catalogue.Candidates.FirstOrDefault(candidate =>
        string.Equals(candidate.Id, source.Id, StringComparison.Ordinal)
        || string.Equals(candidate.UseControlId, source.Id, StringComparison.Ordinal));

    public static void ValidatePrimary(CustomizationSource source, IReadOnlyList<CustomizationFile> files)
    {
        var primary = files.Single(file => string.Equals(file.Name, source.PrimaryFile, StringComparison.Ordinal));
        if (source.Kind == CustomizationKind.McpServer) { _ = McpDefinitionDocument.ValidateHttpsServer(primary.CopyBytes()); }
        else
        {
            var originalInstruction = source.Scope == CustomizationScope.User && source.Kind == CustomizationKind.Instruction;
            var document = ArtifactFileDocument.Parse(primary.CopyBytes(), CustomizationLayout.ArtifactKind(source.Kind),
                source.Scope == CustomizationScope.Kora ? "kora-profile" : originalInstruction ? "profile-instruction" : "user-profile", source.Name);
            if (originalInstruction) { document.RequireProfileInstructionSource(); }
            if (source.Scope == CustomizationScope.Kora && source.Kind is CustomizationKind.Instruction or CustomizationKind.Prompt)
            { document.RequireEditableInstruction(); }
        }
    }

    private static void Require(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Definition admission expired; the operation did not complete."); }
    }
}