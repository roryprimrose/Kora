using System.Xml;

namespace Kora.Core.Context;

public static class ClipboardXmlPolicy
{
    public const int MaximumElementDepth = 64;

    public static ClipboardXmlValidationResult Validate(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        // An admitted strict UTF-8 snapshot has no more UTF-16 units than UTF-8 bytes.
        using var text = new StringReader(snapshot.Text);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Document,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            ValidationType = ValidationType.None,
            MaxCharactersInDocument = ClipboardSnapshot.MaximumUtf8Bytes,
            MaxCharactersFromEntities = ClipboardSnapshot.MaximumUtf8Bytes,
            CheckCharacters = true,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = false,
            CloseInput = false,
        });
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!reader.Read()) { break; }
                if (reader.NodeType == XmlNodeType.Element && reader.Depth >= MaximumElementDepth)
                {
                    var position = (IXmlLineInfo)reader;
                    return new(snapshot, ClipboardXmlOutcome.PolicyRefused, ClipboardXmlReason.ElementDepthExceeded,
                        position.LineNumber, position.LinePosition);
                }
            }
        }
        catch (XmlException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // XmlException.Message can contain source tokens, names, paths and excerpts.
            return new(snapshot, ClipboardXmlOutcome.Invalid, ClipboardXmlReason.MalformedOrProhibitedXml,
                exception.LineNumber, exception.LinePosition);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(snapshot, ClipboardXmlOutcome.WellFormedXml, ClipboardXmlReason.None, 0, 0);
    }
}
