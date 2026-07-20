using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Small owner-drawn rounded-rect "chip"/pill control used to show a
    /// short piece of secondary info (currently: a monitor's native
    /// resolution) next to a row label, styled consistently with the other
    /// Fluent-ish owner-drawn controls in this codebase.
    /// </summary>
    internal sealed class Badge : Control
    {
        private const int HorizontalPadding = 7;
        private const int VerticalPadding = 3;

        private string _text = string.Empty;
        private Color _chipColor = Color.FromArgb(24, 0, 0, 0);
        private Color _textColor = Color.Black;

        public Badge()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.Default;
            TabStop = false;
        }

        [AllowNull]
        public override string Text
        {
            get => _text;
            set
            {
                value ??= string.Empty;
                if (_text == value)
                {
                    return;
                }

                _text = value;
                UpdateSize();
                Invalidate();
            }
        }

        /// <summary>The chip's own rounded-rect fill color.</summary>
        public Color ChipColor
        {
            get => _chipColor;
            set { _chipColor = value; Invalidate(); }
        }

        /// <summary>Text color drawn inside the chip.</summary>
        public Color TextColor
        {
            get => _textColor;
            set { _textColor = value; Invalidate(); }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateSize();
        }

        private void UpdateSize()
        {
            using Graphics g = CreateGraphics();
            SizeF measured = g.MeasureString(_text, Font);
            Size = new Size(
                (int)Math.Ceiling(measured.Width) + (HorizontalPadding * 2),
                (int)Math.Ceiling(measured.Height) + (VerticalPadding * 2));
        }

        // Intentionally does NOT override OnPaintBackground -- see the same
        // note in RoundedCardPanel. Leaving the base implementation in
        // place is what makes BackColor = Color.Transparent correctly show
        // this control's actual parent (the card) behind its rounded
        // corners instead of a flat/garbage fill.

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            int radius = Math.Min(bounds.Height / 2, 6);
            using GraphicsPath path = RoundedRect(bounds, radius);
            using (var brush = new SolidBrush(_chipColor))
            {
                g.FillPath(brush, path);
            }

            if (_text.Length == 0)
            {
                return;
            }

            using var textBrush = new SolidBrush(_textColor);
            var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(_text, Font, textBrush, ClientRectangle, stringFormat);
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
