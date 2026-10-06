using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed class StorageAdmissionException : InvalidOperationException
{
    public StorageAdmissionException()
        : base("Durable content persistence is unavailable: maintained authenticated SQLite and installed native/profile/recovery admission have not passed.")
    {
    }
}
