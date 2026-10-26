using System.Text;

namespace Kora.Core.Dependencies;

/// <summary>A bounded, decoded provisional answer snapshot. It is not a terminal result or retention authority.</summary>
public sealed record ModelAnswerUpdate(ModelTurnProvenance Provenance, string Answer)
{
    public void Validate()
    {
        ModelToolProtocol.ValidateProvenance(Provenance);
        try
        {
            if (string.IsNullOrEmpty(Answer)
                || new UTF8Encoding(false, true).GetByteCount(Answer) > ModelToolProtocol.MaximumOutputUtf8Bytes
                || Answer.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            { throw new InvalidDataException("Invalid provisional answer text."); }
        }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("Malformed provisional answer Unicode.", exception); }
    }
}
