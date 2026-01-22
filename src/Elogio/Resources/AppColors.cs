using System.Windows.Media;

namespace Elogio.Resources;

/// <summary>
/// Application-wide color constants for consistent styling.
/// </summary>
public static class AppColors
{
    // Status colors
    public static readonly Color SuccessGreen = Color.FromRgb(0x4C, 0xAF, 0x50);
    public static readonly Color ErrorRed = Color.FromRgb(0xF4, 0x43, 0x36);
    public static readonly Color WarningOrange = Color.FromRgb(0xFF, 0xA5, 0x00);
    public static readonly Color InfoBlue = Color.FromRgb(0x21, 0x96, 0xF3);
    public static readonly Color NeutralGray = Colors.Gray;

    // Pre-created brushes for common use cases
    public static SolidColorBrush SuccessBrush => new(SuccessGreen);
    public static SolidColorBrush ErrorBrush => new(ErrorRed);
    public static SolidColorBrush WarningBrush => new(WarningOrange);
    public static SolidColorBrush InfoBrush => new(InfoBlue);
    public static SolidColorBrush NeutralBrush => new(NeutralGray);

    // Weekend/highlight colors
    public static readonly Color WeekendBackground = Color.FromRgb(0xF5, 0xF5, 0xF5);
    public static SolidColorBrush WeekendBackgroundBrush => new(WeekendBackground);
}
