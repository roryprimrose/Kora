namespace Kora.Core.Presentation;

/// <summary>Atomic device-local detail presentation state, independent of retained response content.</summary>
public interface IDetailPresentationPreferences
{
    /// <summary>Loads validated state, rejecting unconfirmed writes; missing legacy state is unsaved.</summary>
    DetailPresentationMode? Load();
    /// <summary>Reads exact saved state without clearing an evidence hold.</summary>
    DetailPresentationMode? ReadBack();
    /// <summary>Records a hold before replacing saved state.</summary>
    void BeginWrite();
    /// <summary>Saves one validated choice atomically.</summary>
    void Save(DetailPresentationMode mode);
    /// <summary>Clears the hold after readback and required audit completion.</summary>
    void ConfirmWrite();
}
