using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Draws a minimalist Fluent-style monitor glyph at runtime for the tray
    /// icon, so the app doesn't need to ship/license an external icon asset.
    /// </summary>
    internal static class TrayIconFactory
    {
        /// <summary>
        /// Creates a 32x32 monochrome monitor glyph icon. Pass true when the
        /// taskbar is light-themed (dark glyph needed for contrast), false
        /// when the taskbar is dark-themed (near-white glyph).
        /// </summary>
        internal static Icon CreateMonitorIcon(bool forLightTaskbar)
        {
            Color strokeColor = forLightTaskbar ? Color.FromArgb(255, 26, 26, 26) : Color.White;

            using var bitmap = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var pen = new Pen(strokeColor, 2f) { LineJoin = LineJoin.Round };

                // Screen outline.
                var screenRect = new Rectangle(4, 4, 24, 17);
                using (GraphicsPath screenPath = RoundedRect(screenRect, 3))
                {
                    g.DrawPath(pen, screenPath);
                }

                // Stand (short vertical line beneath the screen).
                g.DrawLine(pen, 16f, 21f, 16f, 25f);

                // Base (slightly wider short horizontal line).
                g.DrawLine(pen, 11f, 27f, 21f, 27f);
            }

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                using Icon temp = Icon.FromHandle(hIcon);
                // Icon.FromHandle wraps the handle without owning it -- clone
                // to get an Icon with its own copy before we destroy hIcon.
                return (Icon)temp.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
