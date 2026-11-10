namespace Kora.Application.Tools;

public delegate Task<SessionMetadataReview.Decision> SessionMetadataPresenter(
    SessionMetadataReview review, CancellationToken token);
