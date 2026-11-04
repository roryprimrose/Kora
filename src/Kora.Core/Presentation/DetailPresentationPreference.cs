namespace Kora.Core.Presentation;

public static class DetailPresentationPreference
{
    public const DetailPresentationMode Default = DetailPresentationMode.Offer;

    public static DetailPresentationMode? Parse(string? value) => value switch
    {
        null => null,
        "Offer" => DetailPresentationMode.Offer,
        "LinkOnly" => DetailPresentationMode.LinkOnly,
        _ => throw new InvalidDataException("The saved detail-presentation preference is invalid; inspect saved state before repair."),
    };

    public static string Format(DetailPresentationMode value) => value switch
    {
        DetailPresentationMode.Offer => "Offer",
        DetailPresentationMode.LinkOnly => "LinkOnly",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
