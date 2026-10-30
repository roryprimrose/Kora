namespace Kora.Core.Commands;

public enum SessionCommandOperation { Help, List, Status, Inspect, Create, Rename, Done, Resume, Invalid, TaskStatus, TaskInspect, TaskCancel, History, HistoryGet,
    QueueList, QueueStatus, QueueEnqueue, QueueCancel, QueueRemove, QueueClear, QueueDispatch, HistorySearch,
    QueuePause, QueueResume, QueuePauseStatus, QueueMoveUp, QueueMoveDown, QueueMoveConfirm, QueueMoveCancel,
    QueueCancelSelectedReview, QueueCancelSelectedConfirm, QueueCancelSelectedDiscard,
    QueueRequeueExpiredReview, QueueRequeueExpiredConfirm, QueueRequeueExpiredDiscard,
    RequestReview, RequestConfirm, RequestCancel,
    RetentionStatus, RetentionReview, RetentionConfirm, RetentionCancel,
    ConversationSearch, ConversationSearchNext, ConversationSearchCancel, AttachmentInspect,
    InstructionScopeReview, InstructionScopeConfirm, InstructionScopeStop, InstructionScopeStatus,
    PendingClearReview, PendingClearConfirm, PendingClearCancel }
