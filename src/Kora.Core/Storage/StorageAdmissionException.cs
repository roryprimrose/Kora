namespace Kora.Core.Storage;

public sealed class StorageAdmissionException : InvalidOperationException
{
    public StorageAdmissionException()
        : base("Durable host/evidence persistence is unavailable: production request, private-profile and recovery composition is not complete.")
    {
    }
}
