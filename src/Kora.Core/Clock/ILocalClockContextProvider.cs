namespace Kora.Core.Clock;

public interface ILocalClockContextProvider
{
    LocalClockContext GetCurrent();
}
