using Kora.Core.Communication;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalInCallFeedbackPreferences : IInCallFeedbackPreferences
{
    private const string FileName = "in-call-feedback.txt";
    private const string PendingFileName = "in-call-feedback-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalInCallFeedbackPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalInCallFeedbackPreferences(IPreferenceStore store) => this.store = store;

    public InCallFeedbackMode? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("An in-call feedback write has unconfirmed evidence. Inspect saved state and audit/intent receipts before explicit repair and refresh.");
        }
        return ReadBack();
    }

    public InCallFeedbackMode? ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return null; }
        if (lines.Length != 2 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved in-call feedback format is unknown or malformed.");
        }
        try { return InCallFeedbackRules.Parse(lines[1]); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved in-call feedback mode is invalid.", exception);
        }
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(InCallFeedbackMode mode)
    {
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        store.WriteLines(FileName, ["1", mode.ToString()]);
    }
    public void Reset() => store.Delete(FileName);
}
