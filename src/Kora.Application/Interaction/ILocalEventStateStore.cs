using Kora.Core.Interaction;

namespace Kora.Application.Interaction;

public interface ILocalEventStateStore
{
    LocalEventBrokerState? Load();
    void BeginWrite();
    void Save(LocalEventBrokerState state);
    void ConfirmWrite();
}
