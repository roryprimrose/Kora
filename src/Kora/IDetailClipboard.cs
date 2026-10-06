namespace Kora;

internal interface IDetailClipboard
{
    Task WritePlainTextAsync(IDetailView view, string source);
}
