namespace Kora.Core.Communication;

public interface IInCallFeedbackPreferences
{
    InCallFeedbackMode? Load();
    InCallFeedbackMode? ReadBack();
    void BeginWrite();
    void ConfirmWrite();
    void Save(InCallFeedbackMode mode);
    void Reset();
}
