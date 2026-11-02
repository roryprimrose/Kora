namespace Kora;

internal interface IDetailClipboard
{
    Task WriteAsync(IDetailView view, string source, string? htmlFragment);
}
