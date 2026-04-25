namespace AzKotle.Application.Common;

public static class CursorPagination
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static string Encode(DateTime createdAt) =>
        Convert.ToBase64String(BitConverter.GetBytes(createdAt.Ticks))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? cursor, out DateTime createdAt)
    {
        createdAt = default;
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
            if (bytes.Length != sizeof(long))
            {
                return false;
            }
            createdAt = new DateTime(BitConverter.ToInt64(bytes, 0), DateTimeKind.Utc);
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
