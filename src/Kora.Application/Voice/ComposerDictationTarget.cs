using System.ComponentModel;

using Kora.Core.Hosting;

namespace Kora.Application.Voice;

/// <summary>An exact volatile buffer observation, with host-owned admission and transactional insertion.</summary>
/// <remarks>A session composer may supply <paramref name="submit"/>; it runs only after a confirmed insertion.</remarks>
public sealed class ComposerDictationTarget(
    INotifyPropertyChanged source, string body, HostRequest request,
    Func<bool> isCurrent, Func<string, Func<bool>, bool> insert,
    ComposerDictationDestination destination = ComposerDictationDestination.NewRequest,
    Action? submit = null)
{
    internal ComposerDictationDestination Destination { get; } = destination;
    internal INotifyPropertyChanged Source { get; } = source;
    internal string Body { get; } = body;
    internal HostRequest Request { get; } = request;
    internal bool SubmitsAfterInsert => submit is not null;
    internal bool IsCurrent() => isCurrent();
    internal bool Insert(string literal, Func<bool> admitted) => insert(literal, admitted);
    internal void Submit() => submit?.Invoke();
}
