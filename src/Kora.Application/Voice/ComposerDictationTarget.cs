using System.ComponentModel;

using Kora.Core.Hosting;

namespace Kora.Application.Voice;

/// <summary>An exact volatile buffer observation, with host-owned admission and transactional insertion.</summary>
public sealed class ComposerDictationTarget(
    INotifyPropertyChanged source, string body, HostRequest request,
    Func<bool> isCurrent, Func<string, Func<bool>, bool> insert,
    ComposerDictationDestination destination = ComposerDictationDestination.NewRequest)
{
    internal ComposerDictationDestination Destination { get; } = destination;
    internal INotifyPropertyChanged Source { get; } = source;
    internal string Body { get; } = body;
    internal HostRequest Request { get; } = request;
    internal bool IsCurrent() => isCurrent();
    internal bool Insert(string literal, Func<bool> admitted) => insert(literal, admitted);
}
