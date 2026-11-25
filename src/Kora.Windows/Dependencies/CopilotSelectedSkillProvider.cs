using Microsoft.Copilot;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Dependencies;

#pragma warning disable MCPSDK001 // One ephemeral reviewed native provider binding; no filesystem identity.
internal sealed class CopilotSelectedSkillProvider(CopilotCustomizationSelection selection,
    Func<CancellationToken, ValueTask<bool>> current, Func<IDisposable> enter,
    Func<HostActivity> continuation, CancellationToken turnToken) : ISkillProvider
{
    private int read;
    internal bool WasRead => Volatile.Read(ref read) != 0;

    public Task<IReadOnlyList<SkillProviderDescriptor>> ListSkillsAsync(CancellationToken cancellationToken = default) =>
        Call<IReadOnlyList<SkillProviderDescriptor>>(_ => Task.FromResult<IReadOnlyList<SkillProviderDescriptor>>(
            [new() { Name = selection.Name, Description = selection.Description }]), cancellationToken);

    public Task<string?> ReadSkillAsync(string name, CancellationToken cancellationToken = default) =>
        Call<string?>(async token =>
        {
            if (!string.Equals(name, selection.Name, StringComparison.Ordinal)) { return null; }
            await Require(token).ConfigureAwait(false);
            Volatile.Write(ref read, 1);
            return selection.SourceText;
        }, cancellationToken);

    private async Task<T> Call<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var lease = enter();
        using var activity = continuation();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turnToken, cancellationToken);
        try
        {
            await Require(linked.Token).ConfigureAwait(false);
            var result = await operation(linked.Token).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private async ValueTask Require(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await current(token).ConfigureAwait(false))
        { throw new OperationCanceledException("The selected skill source or turn is no longer current."); }
        token.ThrowIfCancellationRequested();
    }
}
#pragma warning restore MCPSDK001
