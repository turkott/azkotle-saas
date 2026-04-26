namespace AzKotle.Application.Common;

public static class CursorPagination
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    private const int CursorByteLength = sizeof(long) + 16; // ticks + guid

    /// <summary>
    /// Encodes a composite cursor (CreatedAt, Id). The Id tie-breaker keeps
    /// pagination stable when multiple rows share an identical CreatedAt
    /// (bulk imports, clock-tick collisions). Old single-value cursors emitted
    /// before F10 will fail TryDecode and the client falls back to page 1.
    /// </summary>
    public static string Encode(DateTime createdAt, Guid id)
    {
        var bytes = new byte[CursorByteLength];
        BitConverter.GetBytes(createdAt.Ticks).CopyTo(bytes, 0);
        id.ToByteArray().CopyTo(bytes, sizeof(long));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? cursor, out DateTime createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }
            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length != CursorByteLength)
            {
                return false;
            }
            createdAt = new DateTime(BitConverter.ToInt64(bytes, 0), DateTimeKind.Utc);
            var guidBytes = new byte[16];
            Array.Copy(bytes, sizeof(long), guidBytes, 0, 16);
            id = new Guid(guidBytes);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static int ClampPageSize(int? requested) =>
        requested switch
        {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested.Value,
        };
}
