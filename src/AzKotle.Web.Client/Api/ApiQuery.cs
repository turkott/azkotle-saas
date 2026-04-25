namespace AzKotle.Web.Client.Api;

internal static class ApiQuery
{
    public static string Build(params (string Name, string? Value)[] parts)
    {
        var pairs = parts
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value!)}")
            .ToArray();

        return pairs.Length == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }
}
