namespace Kora.Application.Diagnostics;

public sealed class FileEvidenceHealth
{
    private int failed;

    public void Fail() => Interlocked.Exchange(ref failed, 1);

    public void RequireHealthy()
    {
        if (Volatile.Read(ref failed) != 0)
        {
            throw new IOException("The required daily evidence stream failed; durable admission is closed.");
        }
    }
}
