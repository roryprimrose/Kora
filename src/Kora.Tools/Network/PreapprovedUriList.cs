using Kora.Core.Network;

namespace Kora.Tools.Network;

public sealed class PreapprovedUriList(IPreapprovedUriConfiguration configuration)
{
    internal PreapprovedUriSettings Execute() => configuration.GetSettings();
}
