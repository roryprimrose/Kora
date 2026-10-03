namespace Kora.Core.Diagnostics;

public interface IApplicationLogReader
{
    IReadOnlyList<ApplicationLogFile> GetAvailableLogs();

    Task<string> ReadTailAsync(
        string fileName,
        int maxCharacters,
        CancellationToken cancellationToken = default);
}
