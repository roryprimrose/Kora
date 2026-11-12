namespace Kora;

internal sealed partial class SessionsViewModel
{
    public Task OpenKnowledgeSourceAsync(Func<bool> native) => RunAsync(async () =>
    {
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        await (knowledgeSources ?? throw new InvalidOperationException("Conversation source management is unavailable."))
            .OpenAsync(target, () => !closed && access.CanInspect && access.CanControl
                && access.ControlRevision == control && selectionEpoch == epoch && selected == target && native(), lifetime.Token);
        status = "Source review closed. Explicitly read the original conversation for any retained question and answer.";
    });
}
