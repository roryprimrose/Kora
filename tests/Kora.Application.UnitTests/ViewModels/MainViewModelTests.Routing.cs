using System.Reflection;

using Kora.Application.ViewModels;
using Kora.Core.Auditing;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static Task InvokeTranscriptForTest(MainViewModel viewModel, string transcript, SecurityAuditInitiator initiator)
    {
        var method = typeof(MainViewModel).GetMethod("HandleTranscriptAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The host transcript routing seam is unavailable.");
        object?[] arguments = [transcript, 1f, initiator,
            .. method.GetParameters().Skip(3).Select(_ => Type.Missing)];
        return (Task)method.Invoke(viewModel, arguments)!;
    }
}
