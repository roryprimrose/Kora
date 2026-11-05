namespace Kora;

/// <summary>Requests a native copy through the existing disclosure boundary.</summary>
internal delegate Task DetailCopy(bool confirmed, int? selectionStart, int selectionLength,
    DetailCopyRepresentation representation = DetailCopyRepresentation.ActiveView);
