using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Owner-drawn horizontal slider styled like Windows 11's Fluent
    /// sliders (thin track, accent-colored fill, circular thumb). Mirrors
    /// the subset of TrackBar's public surface (Minimum/Maximum/Value,
    /// ValueChanged, Width/Location) that BrightnessPopupForm relies on.
    /// </summary>
    internal sealed class FluentSlider : Control
    {
        private const int ThumbRadius = 11;
        private const int ThumbHoverGrowth = 3;

        // The track must leave room for the largest the thumb ever gets
        // (base radius + hover growth) on both sides, otherwise the thumb
        // clips against the control's edge when hovering at the min/max end.
        private const int TrackInset = ThumbRadius + ThumbHoverGrowth;
        private const int HoverAnimationIntervalMs = 15;
        private const float HoverAnimationDurationMs = 130f;

        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private bool _dragging;

        private Color _accentColor = Color.FromArgb(255, 0, 120, 215);
        private Color _trackColor = Color.FromArgb(255, 200, 200, 200);
        private Color _thumbBorderColor = Color.White;

        private readonly System.Windows.Forms.Timer _hoverTimer;
        private float _hoverProgress;
        private bool _hovering;

        public event EventHandler? ValueChanged;

        public FluentSlider()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Height = 30;
            TabStop = true;

            _hoverTimer = new System.Windows.Forms.Timer { Interval = HoverAnimationIntervalMs };
            _hoverTimer.Tick += OnHoverTimerTick;
        }

        public int Minimum
        {
            get => _minimum;
            set
            {
                if (_minimum == value)
                {
                    return;
                }

                _minimum = value;
                if (_maximum < _minimum)
                {
                    _maximum = _minimum;
                }

                Value = Math.Clamp(_value, _minimum, _maximum);
                Invalidate();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                if (_maximum == value)
                {
                    return;
                }

                _maximum = value;
                if (_minimum > _maximum)
                {
                    _minimum = _maximum;
                }

                Value = Math.Clamp(_value, _minimum, _maximum);
                Invalidate();
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Clamp(value, _minimum, _maximum);
                if (clamped == _value)
                {
                    return;
                }

                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Color AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; Invalidate(); }
        }

        public Color TrackColor
        {
            get => _trackColor;
            set { _trackColor = value; Invalidate(); }
        }

        public Color ThumbBorderColor
        {
            get => _thumbBorderColor;
            set { _thumbBorderColor = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovering = true;
            _hoverTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovering = false;
            _hoverTimer.Start();
        }

        private void OnHoverTimerTick(object? sender, EventArgs e)
        {
            float step = HoverAnimationIntervalMs / HoverAnimationDurationMs;

            if (_hovering)
            {
                _hoverProgress = Math.Min(1f, _hoverProgress + step);
                if (_hoverProgress >= 1f)
                {
                    _hoverTimer.Stop();
                }
            }
            else
            {
                _hoverProgress = Math.Max(0f, _hoverProgress - step);
                if (_hoverProgress <= 0f)
                {
                    _hoverTimer.Stop();
                }
            }

            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hoverTimer.Tick -= OnHoverTimerTick;
                _hoverTimer.Stop();
                _hoverTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Focus();
            _dragging = true;
            Capture = true;
            UpdateValueFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                UpdateValueFromMouse(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value -= 1;
                    e.Handled = true;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value += 1;
                    e.Handled = true;
                    break;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        private void UpdateValueFromMouse(int x)
        {
            int usableWidth = Width - (TrackInset * 2);
            if (usableWidth <= 0)
            {
                return;
            }

            double ratio = Math.Clamp((x - TrackInset) / (double)usableWidth, 0.0, 1.0);
            Value = _minimum + (int)Math.Round(ratio * (_maximum - _minimum));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int trackY = Height / 2;
            int left = TrackInset;
            int right = Width - TrackInset;
            if (right <= left)
            {
                return;
            }

            double ratio = _maximum > _minimum ? (double)(_value - _minimum) / (_maximum - _minimum) : 0;
            int thumbX = left + (int)Math.Round(ratio * (right - left));

            using (var trackPen = new Pen(_trackColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(trackPen, left, trackY, right, trackY);
            }

            if (thumbX > left)
            {
                using var fillPen = new Pen(_accentColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(fillPen, left, trackY, thumbX, trackY);
            }

            int animatedRadius = ThumbRadius + (int)Math.Round(ThumbHoverGrowth * _hoverProgress);
            var thumbRect = new Rectangle(thumbX - animatedRadius, trackY - animatedRadius, animatedRadius * 2, animatedRadius * 2);
            using (var thumbBrush = new SolidBrush(_accentColor))
            {
                g.FillEllipse(thumbBrush, thumbRect);
            }

            using (var thumbBorderPen = new Pen(_thumbBorderColor, 2f))
            {
                g.DrawEllipse(thumbBorderPen, thumbRect);
            }
        }
    }
}
