using System.Diagnostics;

using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>The original passive source and issued review, never a new enqueue or dispatch authority.</summary>
public sealed record SessionDiagnosticsPlanPreparation(SessionWorkSnapshot Work, SessionDiagnosticsPlanReview Review,
    ActivityContext Cause);
