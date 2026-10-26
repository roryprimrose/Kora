namespace Kora;

internal sealed partial class SessionsViewModel
{
    private bool deviceProfileMemory;

    public bool DeviceProfileMemory
    {
        get => deviceProfileMemory;
        set
        {
            if (notifying || busy || conversationBusy || deviceProfileMemory == value) { return; }
            ClearMemories();
            memories?.ClearVolatile();
            deviceProfileMemory = value;
            OnPropertyChanged();
            NotifyMemories();
        }
    }
}
