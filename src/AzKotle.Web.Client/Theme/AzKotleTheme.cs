using MudBlazor;

namespace AzKotle.Web.Client.Theme;

public static class AzKotleTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0F6B8A",
            Secondary = "#D97706",
            AppbarBackground = "#0F6B8A",
            AppbarText = "#FFFFFF",
            TextPrimary = "#0F1A24",
            TextSecondary = "#475569",
            Background = "#F8FAFC",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#0F1A24",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Inter", "system-ui", "Segoe UI", "sans-serif" },
            },
            H1 = new H1Typography { FontFamily = new[] { "Inter", "system-ui", "sans-serif" } },
            H2 = new H2Typography { FontFamily = new[] { "Inter", "system-ui", "sans-serif" } },
            H3 = new H3Typography { FontFamily = new[] { "Inter", "system-ui", "sans-serif" } },
            H4 = new H4Typography { FontFamily = new[] { "Inter", "system-ui", "sans-serif" } },
            H5 = new H5Typography { FontFamily = new[] { "Inter", "system-ui", "sans-serif" } },
            H6 = new H6Typography { FontFamily = new[] { "Inter", "system-ui", "sans-serif" } },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
        },
    };
}
