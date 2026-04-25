namespace AzKotle.Application.Common;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
