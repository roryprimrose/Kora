using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Kora.Diagrams;

internal static class MermaidSvg
{
    private static readonly XNamespace Namespace = "http://www.w3.org/2000/svg";

    internal static string Write(MermaidDrawing drawing, Action check)
    {
        var root = new XElement(Namespace + "svg", new XAttribute("width", Number(drawing.Width)),
            new XAttribute("height", Number(drawing.Height)));
        foreach (var line in drawing.Lines)
        {
            check();
            root.Add(Line(line.X1, line.Y1, line.X2, line.Y2, line.Dashed));
            if (line.Arrow)
            {
                var angle = Math.Atan2(line.Y2 - line.Y1, line.X2 - line.X1);
                root.Add(Line(line.X2, line.Y2, line.X2 - 10 * Math.Cos(angle - 0.5), line.Y2 - 10 * Math.Sin(angle - 0.5), false));
                root.Add(Line(line.X2, line.Y2, line.X2 - 10 * Math.Cos(angle + 0.5), line.Y2 - 10 * Math.Sin(angle + 0.5), false));
            }
            root.Add(Text(Math.Min(line.X1, line.X2) + 4, (line.Y1 + line.Y2) / 2 - 6, line.Text));
        }
        foreach (var box in drawing.Boxes)
        {
            check();
            root.Add(new XElement(Namespace + "rect",
                new XAttribute("x", Number(box.X)), new XAttribute("y", Number(box.Y)),
                new XAttribute("width", Number(box.Width)), new XAttribute("height", Number(box.Height)),
                new XAttribute("fill", "none"), new XAttribute("stroke", "currentColor")));
            var row = 0;
            foreach (var text in box.Text.Split('\n'))
            {
                check();
                root.Add(Text(box.X + 8, box.Y + 24 + row++ * 24, text));
            }
        }
        var svg = root.ToString(SaveOptions.DisableFormatting);
        if (MermaidProfile.Utf8.GetByteCount(svg) > MermaidProfile.MaximumSvgBytes)
        { throw new MermaidRejectedException(MermaidFailure.UnsafeOutput); }
        return svg;
    }

    // Reject the entire projection, never retain a partially stripped or repaired SVG.
    internal static bool IsSafe(string svg, Action check)
    {
        if (MermaidProfile.Utf8.GetByteCount(svg) > MermaidProfile.MaximumSvgBytes) { return false; }
        using var input = new StringReader(svg);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MermaidProfile.MaximumSvgBytes,
        });
        var depth = 0;
        var elements = 0;
        var rootSeen = false;
        try
        {
            while (reader.Read())
            {
                check();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (++elements > 10000 || !string.Equals(reader.NamespaceURI, Namespace.NamespaceName, StringComparison.Ordinal)
                        || reader.LocalName is not ("svg" or "rect" or "line" or "text")
                        || reader.Depth > 1) { return false; }
                    if (reader.Depth == 0)
                    {
                        if (rootSeen || reader.LocalName is not "svg") { return false; }
                        rootSeen = true;
                    }
                    else if (reader.LocalName is "svg") { return false; }
                    depth = reader.Depth;
                    var element = reader.LocalName;
                    while (reader.MoveToNextAttribute())
                    {
                        if (reader.Name is "xmlns" && string.Equals(reader.Value, Namespace.NamespaceName, StringComparison.Ordinal)) { continue; }
                        if (reader.NamespaceURI.Length != 0 || !Attribute(element, reader.Name, reader.Value)) { return false; }
                    }
                    reader.MoveToElement();
                }
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    if (depth != 1 || reader.Value.Length > MermaidProfile.MaximumDiagramBytes) { return false; }
                }
                else if (reader.NodeType != XmlNodeType.EndElement) { return false; }
            }
            return rootSeen;
        }
        catch (XmlException) { return false; }
    }

    private static bool Attribute(string element, string name, string value)
    {
        if (name is "stroke") { return element is "rect" or "line" && value is "currentColor"; }
        if (name is "fill") { return element is "rect" or "text" && string.Equals(value, element is "rect" ? "none" : "currentColor", StringComparison.Ordinal); }
        if (name is "stroke-dasharray") { return element is "line" && value is "4 4"; }
        if (name is "font-size") { return element is "text" && value is "13"; }
        var numeric = element switch
        {
            "svg" => name is "width" or "height",
            "rect" => name is "x" or "y" or "width" or "height",
            "line" => name is "x1" or "x2" or "y1" or "y2",
            "text" => name is "x" or "y",
            _ => false,
        };
        return numeric && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number) && number is >= -100 and <= 131072;
    }

    private static string Number(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);
    private static XElement Line(double x1, double y1, double x2, double y2, bool dashed) =>
        new(Namespace + "line", new XAttribute("x1", Number(x1)), new XAttribute("y1", Number(y1)),
            new XAttribute("x2", Number(x2)), new XAttribute("y2", Number(y2)),
            new XAttribute("stroke", "currentColor"), dashed ? new XAttribute("stroke-dasharray", "4 4") : null);
    private static XElement Text(double x, double y, string value) =>
        new(Namespace + "text", new XAttribute("x", Number(x)), new XAttribute("y", Number(y)),
            new XAttribute("font-size", "13"), new XAttribute("fill", "currentColor"), value);
}
