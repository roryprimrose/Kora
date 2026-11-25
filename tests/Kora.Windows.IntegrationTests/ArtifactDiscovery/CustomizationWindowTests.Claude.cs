using Kora.Core.Customizations;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("hosted-source")]
    [InlineData("retention")]
    [InlineData("changed-task")]
    [InlineData("complete")]
    [InlineData("dirty-draft")]
    [InlineData("missing-source")]
    [InlineData("workflow-unavailable")]
    [InlineData("stop-join")]
    [InlineData("task-join")]
    [InlineData("close-join")]
    public Task SelectedClaudeNativeUiRequiresSeparateReadCopyAndRetentionDecisions(string stop) =>
        SelectedHostedNativeUiRequiresDecisions(stop, "Claude");

    private sealed class PausedCustomizationStore(ICustomizationStore inner) : ICustomizationStore
    {
        internal bool Pause { get; set; }
        internal int PausedReads { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CustomizationSnapshot> Read(CustomizationSource source, CancellationToken token)
        {
            if (!Pause) { return await inner.Read(source, token); }
            PausedReads++;
            Entered.TrySetResult();
            await Release.Task;
            return await inner.Read(source, CancellationToken.None);
        }
        public InstructionOriginalRegistration? RegisteredInstruction => inner.RegisteredInstruction;
        public bool IsInstructionRegistered(CustomizationSource source) => inner.IsInstructionRegistered(source);
        public void WithdrawInstruction(CustomizationSource source) => inner.WithdrawInstruction(source);
        public Task<InstructionOriginalRegistration> PrepareInstructionRegistration(CustomizationSource source, CancellationToken token) =>
            inner.PrepareInstructionRegistration(source, token);
        public Task RegisterInstruction(InstructionOriginalRegistration proposal, Func<bool> eligible, CancellationToken token) =>
            inner.RegisterInstruction(proposal, eligible, token);
        public Task<IReadOnlyList<CustomizationSource>> List(CancellationToken token) => inner.List(token);
        public Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name, CancellationToken token) =>
            inner.Create(kind, scope, name, token);
        public Task<CustomizationSnapshot> ReadRecovery(CustomizationSource source, CancellationToken token) => inner.ReadRecovery(source, token);
        public Task<CustomizationSnapshot> Save(CustomizationSource source, CustomizationSnapshot? expected, IReadOnlyList<CustomizationFile> files,
            Func<bool> eligible, Func<Func<CustomizationSnapshot>, Task<CustomizationSnapshot>> commit, CancellationToken token,
            CustomizationSnapshot? copiedFrom = null) => inner.Save(source, expected, files, eligible, commit, token, copiedFrom);
        public Task Delete(CustomizationSnapshot reviewed, Func<bool> eligible, Func<Action, Task> commit, CancellationToken token) =>
            inner.Delete(reviewed, eligible, commit, token);
    }
}
