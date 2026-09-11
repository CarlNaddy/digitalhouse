using MudBlazor;

namespace DigitalHouse.Components.Layout;

/// <summary>
/// The "quest board" theme — a dark arcane HUD in Interactive/dark mode, a
/// sunlit parchment scroll in light mode. See CLAUDE.md's MudBlazor section;
/// the corner-cut panel look and font pairing live in wwwroot/app.css.
/// </summary>
public static class AppTheme
{
    public static readonly MudTheme Instance = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#9b7bff",
            Secondary = "#f5b942",
            Tertiary = "#35e0c8",
            Info = "#3ec6e0",
            Success = "#3ddc84",
            Warning = "#f2994a",
            Error = "#ef4763",
            Background = "#0a0e1c",
            BackgroundGray = "#0f1526",
            Surface = "#131a2e",
            DrawerBackground = "#0d1224",
            DrawerText = "#c9d0e8",
            DrawerIcon = "#9aa4c7",
            AppbarBackground = "#0d1224",
            AppbarText = "#e8ecf7",
            TextPrimary = "#e8ecf7",
            TextSecondary = "#9aa4c7",
            ActionDefault = "#9aa4c7",
            Divider = "#26304d",
            DividerLight = "#1c2439",
            LinesDefault = "#26304d",
            LinesInputs = "#3a4568",
            TableLines = "#26304d",
            TableHover = "#1a2340",
            TableStriped = "#0f1526",
        },
        PaletteLight = new PaletteLight
        {
            Primary = "#6d4fd1",
            Secondary = "#c8811a",
            Tertiary = "#12968a",
            Info = "#1f7fa3",
            Success = "#2f9e5f",
            Warning = "#c26b1f",
            Error = "#c23a52",
            Background = "#f4ecd8",
            BackgroundGray = "#ece0c4",
            Surface = "#faf4e4",
            DrawerBackground = "#f0e6cc",
            DrawerText = "#3a2f1c",
            DrawerIcon = "#6b5a3a",
            AppbarBackground = "#2b2013",
            AppbarText = "#f4ecd8",
            TextPrimary = "#2b2013",
            TextSecondary = "#6b5a3a",
            ActionDefault = "#6b5a3a",
            Divider = "#d9c99e",
            DividerLight = "#e6d9b6",
            LinesDefault = "#d9c99e",
            LinesInputs = "#c2ac74",
            TableLines = "#d9c99e",
            TableHover = "#ece0c4",
            TableStriped = "#f0e6cc",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "280px",
            AppbarHeight = "68px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Rajdhani", "Segoe UI", "sans-serif"],
                FontWeight = "500",
            },
            H1 = new H1Typography { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "700" },
            H2 = new H2Typography { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "700" },
            H3 = new H3Typography { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "600" },
            H4 = new H4Typography { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "600" },
            H5 = new H5Typography { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "600" },
            H6 = new H6Typography { FontFamily = ["Rajdhani", "Segoe UI", "sans-serif"], FontWeight = "700" },
            Button = new ButtonTypography
            {
                FontFamily = ["Rajdhani", "Segoe UI", "sans-serif"],
                FontWeight = "700",
                TextTransform = "uppercase",
                LetterSpacing = ".04em",
            },
        },
    };
}
