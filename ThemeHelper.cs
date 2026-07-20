using System;
using System.Drawing;
using Microsoft.Win32;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Reads Windows' current app/system theme mode and accent color so UI
    /// can mirror native Windows 11 flyout styling.
    /// </summary>
    internal static class ThemeHelper
    {
        private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private static readonly Color FallbackAccentColor = Color.FromArgb(255, 0, 120, 215);

        /// <summary>
        /// Whether apps (this popup's own chrome) should use the light theme.
        /// Backed by AppsUseLightTheme; missing/unreadable defaults to light (true).
        /// </summary>
        internal static bool IsAppsLightTheme() => ReadLightThemeValue("AppsUseLightTheme");

        /// <summary>
        /// Whether the taskbar/system surfaces use the light theme. Backed by
        /// SystemUsesLightTheme, which can differ from AppsUseLightTheme --
        /// used to pick tray icon glyph color, not popup chrome color.
        /// </summary>
        internal static bool IsSystemLightTheme() => ReadLightThemeValue("SystemUsesLightTheme");

        private static bool ReadLightThemeValue(string valueName)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
                object? value = key?.GetValue(valueName);
                if (value is int intValue)
                {
                    return intValue != 0;
                }
            }
            catch
            {
                // Fall through to the default below.
            }

            return true;
        }

        /// <summary>
        /// Gets the user's current Windows accent/colorization color via DWM.
        /// Falls back to the Windows default blue accent if unavailable.
        /// </summary>
        internal static Color GetAccentColor()
        {
            try
            {
                if (NativeMethods.DwmGetColorizationColor(out uint colorization, out _) == 0)
                {
                    byte r = (byte)((colorization >> 16) & 0xFF);
                    byte g = (byte)((colorization >> 8) & 0xFF);
                    byte b = (byte)(colorization & 0xFF);
                    return Color.FromArgb(255, r, g, b);
                }
            }
            catch
            {
                // Fall through to the default below.
            }

            return FallbackAccentColor;
        }
    }
}
