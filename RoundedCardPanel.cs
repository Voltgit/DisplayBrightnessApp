using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Owner-drawn Panel subclass that fills itself with a rounded-rect
    /// "elevated card" background, matching how Windows 11 Settings / Quick
    /// Settings visually group related controls on a shade distinct from
    /// the surrounding flyout body. Hosts the per-monitor rows as normal
    /// child controls -- callers just add/remove/dispose children as usual.
    /// </summary>
    internal sealed class RoundedCardPanel : Panel
    {
        private const int CornerRadius = 8;

        private Color _cardColor = Color.White;

        public RoundedCardPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        /// <summary>The card's own fill color (distinct from the popup body's BackColor behind it).</summary>
        public Color CardColor
        {
            get => _cardColor;
            set { _cardColor = value; Invalidate(); }
        }

        // Intentionally does NOT override OnPaintBackground: the base
        // implementation is what makes BackColor = Color.Transparent work
        // (it recursively renders the parent's actual painted appearance
        // into this control's background buffer -- the same built-in
        // mechanism FluentSlider/labels rely on via ApplyTheme's
        // BackColor = Color.Transparent). That's exactly what we want here:
        // the four corners outside the rounded rect need to show the
        // popup's real background, not a flat guess at its color.

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using GraphicsPath path = RoundedRect(bounds, CornerRadius);
            using var brush = new SolidBrush(_cardColor);
            g.FillPath(brush, path);
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
