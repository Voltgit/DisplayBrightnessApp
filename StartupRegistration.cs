using System;
using Microsoft.Win32;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Self-registers the app to launch on Windows login by writing a value
    /// under HKCU\...\Run. There is no UI toggle for this by design -- it is
    /// always kept in sync (idempotently) with the current executable path
    /// on every launch.
    /// </summary>
    internal static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DisplayBrightnessApp";

        /// <summary>
        /// Ensures the Run key points at the current executable. Only writes
        /// to the registry if the value is missing or stale, to avoid
        /// needless writes on every launch. Never throws.
        /// </summary>
        internal static void EnsureRegistered()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return;
                }

                string desiredValue = $"\"{exePath}\"";

                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

                if (key == null)
                {
                    return;
                }

                object? existing = key.GetValue(ValueName);
                if (existing is string existingValue &&
                    string.Equals(existingValue, desiredValue, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                key.SetValue(ValueName, desiredValue, RegistryValueKind.String);
            }
            catch
            {
                // A registry failure (permissions, corrupt hive, etc.) must
                // never prevent the app from starting.
            }
        }
    }
}
