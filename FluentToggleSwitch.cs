using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Owner-drawn pill-shaped two-state toggle styled like Windows 11
    /// Settings toggles, replacing the stock CheckBox for "Synchronize".
    /// </summary>
    internal sealed class FluentToggleSwitch : Control
    {
        private bool _checked;

        private Color _accentColor = Color.FromArgb(255, 0, 120, 215);
        private Color _offTrackColor = Color.FromArgb(255, 150, 150, 150);
        private Color _borderColor = Color.FromArgb(255, 150, 150, 150);
        private Color _thumbColor = Color.White;

        public event EventHandler? CheckedChanged;

        public FluentToggleSwitch()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Size = new Size(40, 20);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                {
                    return;
                }

                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Color AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; Invalidate(); }
        }

        public Color OffTrackColor
        {
            get => _offTrackColor;
            set { _offTrackColor = value; Invalidate(); }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        public Color ThumbColor
        {
            get => _thumbColor;
            set { _thumbColor = value; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData is Keys.Space or Keys.Enter)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = trackRect.Height / 2;

            using GraphicsPath path = RoundedRect(trackRect, radius);

            if (_checked)
            {
                using var trackBrush = new SolidBrush(_accentColor);
                g.FillPath(trackBrush, path);
            }

            using (var trackPen = new Pen(_checked ? _accentColor : _borderColor, 1.5f))
            {
                g.DrawPath(trackPen, path);
            }

            int thumbDiameter = Height - 8;
            int thumbY = 4;
            int thumbX = _checked ? Width - thumbDiameter - 5 : 5;
            var thumbRect = new Rectangle(thumbX, thumbY, thumbDiameter, thumbDiameter);

            using var thumbBrush = new SolidBrush(_checked ? _thumbColor : _offTrackColor);
            g.FillEllipse(thumbBrush, thumbRect);
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
