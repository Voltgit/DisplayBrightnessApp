using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Borderless popup panel listing one label+slider row per detected
    /// monitor plus a "Synchronize" toggle, styled to match Windows 11's
    /// own flyouts (Quick Settings / volume / battery). Closes itself as
    /// soon as it loses focus (click elsewhere / Alt-Tab / etc).
    /// </summary>
    internal sealed class BrightnessPopupForm : Form
    {
        private const int FormWidth = 480;
        private const int EdgePadding = 22;
        private const int RowSpacing = 18;
        private const int CardPadding = 16;
        private const int RowGapHalf = 8;
        private const int BadgeSpacing = 6;
        private const int DebounceMilliseconds = 120;
        private const int ToggleWidth = 46;
        private const int ToggleHeight = 24;

        private const int TaskbarGap = 8;

        private const int OpenSlideDistance = 14;
        private const int OpenAnimationIntervalMs = 15;
        private const float OpenAnimationDurationMs = 170f;

        private const int CloseSlideDistance = 10;
        private const int CloseAnimationIntervalMs = 15;
        private const float CloseAnimationDurationMs = 140f;

        private static readonly string[] PreferredFontFamilies =
        {
            "Segoe UI Variable Text",
            "Segoe UI Variable Display",
            "Segoe UI Variable"
        };

        private sealed class MonitorRow
        {
            public required string MonitorId;
            public required Label Label;
            public required FluentSlider Slider;
            public Badge? ResolutionBadge;
            public Panel? TopDivider;
        }

        private readonly MonitorBrightnessService _service;
        private readonly List<MonitorRow> _rows = new();
        private readonly HashSet<string> _pendingMonitorIds = new();
        private readonly System.Windows.Forms.Timer _debounceTimer;
        private readonly RoundedCardPanel _card;
        private readonly Label _syncLabel;
        private readonly FluentToggleSwitch _syncCheckBox;
        private Label? _noMonitorsLabel;

        private readonly Point _anchorPoint;
        private bool _isApplyingProgrammatically;
        private Color _accentColor;

        private readonly System.Windows.Forms.Timer _openAnimTimer;
        private Point _openAnimTargetLocation;
        private float _openAnimProgress;

        private readonly System.Windows.Forms.Timer _closeAnimTimer;
        private Point _closeAnimStartLocation;
        private float _closeAnimProgress;
        private bool _isClosingAnimation;
        private bool _isReallyClosing;

        public BrightnessPopupForm(MonitorBrightnessService service, Point anchorPoint)
        {
            _service = service;
            _anchorPoint = anchorPoint;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(FormWidth, 60);
            Font = CreateUiFont(10.5f);

            _accentColor = ThemeHelper.GetAccentColor();

            // Elevated "card" surface behind the monitor rows, matching how
            // Windows 11 Settings/Quick Settings group related controls on a
            // background shade distinct from the surrounding flyout body.
            // Rows are added/removed as its children in BuildRows().
            _card = new RoundedCardPanel
            {
                Location = new Point(EdgePadding, EdgePadding),
                Width = FormWidth - (EdgePadding * 2),
                Height = CardPadding * 2
            };
            Controls.Add(_card);

            // "Synchronize" sits below the card; its actual vertical
            // position is finalized in BuildRows() once the card's height
            // (which depends on the monitor count) is known.
            _syncLabel = new Label
            {
                Text = "Synchronize",
                AutoSize = true,
                Location = new Point(EdgePadding, EdgePadding + 2)
            };
            Controls.Add(_syncLabel);

            _syncCheckBox = new FluentToggleSwitch
            {
                Size = new Size(ToggleWidth, ToggleHeight),
                Location = new Point(FormWidth - EdgePadding - ToggleWidth, EdgePadding)
            };
            _syncCheckBox.CheckedChanged += OnSyncCheckedChanged;
            Controls.Add(_syncCheckBox);

            _debounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMilliseconds };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _openAnimTimer = new System.Windows.Forms.Timer { Interval = OpenAnimationIntervalMs };
            _openAnimTimer.Tick += OnOpenAnimTick;

            _closeAnimTimer = new System.Windows.Forms.Timer { Interval = CloseAnimationIntervalMs };
            _closeAnimTimer.Tick += OnCloseAnimTick;

            ApplyTheme();

            Load += OnLoad;
            FormClosed += OnFormClosed;
        }

        /// <summary>
        /// Segoe UI Variable is Windows 11's default UI font but isn't
        /// guaranteed present on every system -- fall back to Segoe UI.
        /// </summary>
        private static Font CreateUiFont(float size, FontStyle style = FontStyle.Regular)
        {
            foreach (string familyName in PreferredFontFamilies)
            {
                foreach (FontFamily installed in FontFamily.Families)
                {
                    if (string.Equals(installed.Name, familyName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            return new Font(familyName, size, style);
                        }
                        catch
                        {
                            // Try the next candidate.
                        }
                    }
                }
            }

            return new Font("Segoe UI", size, style);
        }

        private void OnLoad(object? sender, EventArgs e)
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            BuildRows();
            PositionNearAnchor();
            BeginOpenAnimation();
        }

        /// <summary>
        /// Plays a short fade-in + slide-up-into-place opening animation
        /// (matching Windows 11's own flyouts) the first time the popup is
        /// shown. Only called from OnLoad -- RefreshAndRebuild() repositions
        /// the already-visible popup without replaying this.
        /// </summary>
        private void BeginOpenAnimation()
        {
            _openAnimTargetLocation = Location;
            _openAnimProgress = 0f;

            Opacity = 0d;
            Location = new Point(_openAnimTargetLocation.X, _openAnimTargetLocation.Y + OpenSlideDistance);

            _openAnimTimer.Start();
        }

        private void OnOpenAnimTick(object? sender, EventArgs e)
        {
            _openAnimProgress = Math.Min(1f, _openAnimProgress + (OpenAnimationIntervalMs / OpenAnimationDurationMs));

            // Ease-out cubic: quick start, gentle settle into place.
            float eased = 1f - MathF.Pow(1f - _openAnimProgress, 3f);

            Opacity = eased;
            int y = _openAnimTargetLocation.Y + (int)Math.Round(OpenSlideDistance * (1f - eased));
            Location = new Point(_openAnimTargetLocation.X, y);

            if (_openAnimProgress >= 1f)
            {
                _openAnimTimer.Stop();
                Opacity = 1d;
                Location = _openAnimTargetLocation;
            }
        }

        /// <summary>
        /// Intercepts every close request (repeat tray click, click-away
        /// deactivate, app exit) and plays a fade-out + slide-down animation
        /// before letting the form actually close. The first FormClosing
        /// call is cancelled and kicks off the animation; once it finishes
        /// it calls Close() again with _isReallyClosing set, which this time
        /// is allowed through.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isReallyClosing)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;

            if (!_isClosingAnimation)
            {
                BeginCloseAnimation();
            }
        }

        private void BeginCloseAnimation()
        {
            _isClosingAnimation = true;
            _openAnimTimer.Stop();

            _closeAnimStartLocation = Location;
            _closeAnimProgress = 0f;
            _closeAnimTimer.Start();
        }

        private void OnCloseAnimTick(object? sender, EventArgs e)
        {
            _closeAnimProgress = Math.Min(1f, _closeAnimProgress + (CloseAnimationIntervalMs / CloseAnimationDurationMs));

            // Ease-in quadratic: gentle start, quick exit.
            float eased = _closeAnimProgress * _closeAnimProgress;

            Opacity = Math.Max(0d, 1d - eased);
            int y = _closeAnimStartLocation.Y + (int)Math.Round(CloseSlideDistance * eased);
            Location = new Point(_closeAnimStartLocation.X, y);

            if (_closeAnimProgress >= 1f)
            {
                _closeAnimTimer.Stop();
                _isReallyClosing = true;
                Close();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDwmAttributes();
        }

        /// <summary>
        /// Applies Windows 11 flyout-style DWM chrome: small-radius rounded
        /// corners, immersive dark mode (so DWM-drawn shadow/etc match), and
        /// a best-effort Acrylic-style backdrop. All are wrapped -- none of
        /// this may be supported on older Windows builds, and none of it is
        /// load-bearing since a solid theme-correct background color is
        /// applied regardless.
        /// </summary>
        private void ApplyDwmAttributes()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                int cornerPreference = NativeMethods.DWMWCP_ROUNDSMALL;
                NativeMethods.DwmSetWindowAttribute(
                    Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                int darkMode = ThemeHelper.IsAppsLightTheme() ? 0 : 1;
                NativeMethods.DwmSetWindowAttribute(
                    Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
                NativeMethods.DwmSetWindowAttribute(
                    Handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            catch
            {
                // Best-effort visual enhancement only; never fatal.
            }
        }

        /// <summary>
        /// Applies theme-aware colors to the form chrome and every control,
        /// plus the current accent color. Called on initial build and again
        /// whenever Windows' theme/accent changes while the popup is open.
        /// </summary>
        private void ApplyTheme()
        {
            bool light = ThemeHelper.IsAppsLightTheme();
            Color backColor = light ? Color.FromArgb(255, 243, 243, 243) : Color.FromArgb(255, 32, 32, 32);
            Color foreColor = light ? Color.Black : Color.White;
            Color trackColor = light ? Color.FromArgb(255, 200, 200, 200) : Color.FromArgb(255, 90, 90, 90);
            Color cardColor = light ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 44, 44, 44);
            Color dividerColor = light ? Color.FromArgb(255, 229, 229, 229) : Color.FromArgb(255, 61, 61, 61);
            Color badgeChipColor = light ? Color.FromArgb(255, 233, 233, 233) : Color.FromArgb(255, 58, 58, 58);
            Color badgeTextColor = light ? Color.FromArgb(255, 96, 96, 96) : Color.FromArgb(255, 190, 190, 190);

            BackColor = backColor;
            ForeColor = foreColor;

            _card.BackColor = Color.Transparent;
            _card.CardColor = cardColor;

            _syncLabel.ForeColor = foreColor;
            _syncLabel.BackColor = Color.Transparent;

            _syncCheckBox.BackColor = Color.Transparent;
            _syncCheckBox.AccentColor = _accentColor;
            _syncCheckBox.OffTrackColor = trackColor;
            _syncCheckBox.BorderColor = trackColor;

            if (_noMonitorsLabel != null)
            {
                _noMonitorsLabel.ForeColor = foreColor;
                _noMonitorsLabel.BackColor = Color.Transparent;
            }

            foreach (MonitorRow row in _rows)
            {
                row.Label.ForeColor = foreColor;
                row.Label.BackColor = Color.Transparent;
                row.Slider.BackColor = Color.Transparent;
                row.Slider.AccentColor = _accentColor;
                row.Slider.TrackColor = trackColor;
                // The thumb's "cutout" ring should match what's actually
                // behind it now -- the card's fill, not the popup body.
                row.Slider.ThumbBorderColor = cardColor;

                if (row.ResolutionBadge != null)
                {
                    row.ResolutionBadge.BackColor = Color.Transparent;
                    row.ResolutionBadge.ChipColor = badgeChipColor;
                    row.ResolutionBadge.TextColor = badgeTextColor;
                }

                if (row.TopDivider != null)
                {
                    row.TopDivider.BackColor = dividerColor;
                }
            }

            ApplyDwmAttributes();
            Invalidate(true);
        }

        /// <summary>
        /// Places the popup just above the taskbar, horizontally centered on
        /// the point where the tray icon was clicked (heuristic -- WinForms
        /// exposes no direct "notify icon bounds" API).
        /// </summary>
        private void PositionNearAnchor()
        {
            Rectangle workingArea = Screen.FromPoint(_anchorPoint).WorkingArea;

            int x = _anchorPoint.X - (Width / 2);
            x = Math.Min(x, workingArea.Right - Width);
            x = Math.Max(workingArea.Left, x);

            int y = workingArea.Bottom - Height - TaskbarGap;
            y = Math.Max(workingArea.Top, y);

            Location = new Point(x, y);
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _debounceTimer.Stop();
            _debounceTimer.Tick -= OnDebounceTimerTick;
            _debounceTimer.Dispose();
            _openAnimTimer.Stop();
            _openAnimTimer.Tick -= OnOpenAnimTick;
            _openAnimTimer.Dispose();
            _closeAnimTimer.Stop();
            _closeAnimTimer.Tick -= OnCloseAnimTick;
            _closeAnimTimer.Dispose();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshAndRebuild));
            }
            else
            {
                RefreshAndRebuild();
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)
            {
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshTheme));
            }
            else
            {
                RefreshTheme();
            }
        }

        private void RefreshTheme()
        {
            if (IsDisposed)
            {
                return;
            }

            _accentColor = ThemeHelper.GetAccentColor();
            ApplyTheme();
        }

        private void RefreshAndRebuild()
        {
            _service.Refresh();
            BuildRows();
            PositionNearAnchor();
        }

        private void OnSyncCheckedChanged(object? sender, EventArgs e)
        {
            // No immediate side effect beyond enabling/disabling sync
            // behavior on the next slider interaction -- keep as-is.
        }

        private void BuildRows()
        {
            _debounceTimer.Stop();
            _pendingMonitorIds.Clear();

            foreach (MonitorRow row in _rows)
            {
                row.Slider.ValueChanged -= OnSliderValueChanged;
                _card.Controls.Remove(row.Label);
                _card.Controls.Remove(row.Slider);
                row.Label.Dispose();
                row.Slider.Dispose();

                if (row.ResolutionBadge != null)
                {
                    _card.Controls.Remove(row.ResolutionBadge);
                    row.ResolutionBadge.Dispose();
                }

                if (row.TopDivider != null)
                {
                    _card.Controls.Remove(row.TopDivider);
                    row.TopDivider.Dispose();
                }
            }
            _rows.Clear();

            if (_noMonitorsLabel != null)
            {
                _card.Controls.Remove(_noMonitorsLabel);
                _noMonitorsLabel.Dispose();
                _noMonitorsLabel = null;
            }

            int cardContentWidth = _card.Width - (CardPadding * 2);
            int y = CardPadding;
            bool isFirstRow = true;

            foreach (MonitorBrightnessService.MonitorInfo monitor in _service.Monitors)
            {
                Panel? divider = null;
                if (!isFirstRow)
                {
                    // A thin separator between consecutive rows only --
                    // never before the first row or after the last.
                    y += RowGapHalf;
                    divider = new Panel
                    {
                        Location = new Point(CardPadding, y),
                        Size = new Size(cardContentWidth, 1)
                    };
                    _card.Controls.Add(divider);
                    y += 1 + RowGapHalf;
                }

                var label = new Label
                {
                    Text = monitor.Name,
                    AutoSize = true,
                    Location = new Point(CardPadding, y)
                };
                _card.Controls.Add(label);

                Badge? badge = null;
                if (!string.IsNullOrEmpty(monitor.Resolution))
                {
                    badge = new Badge
                    {
                        Font = CreateUiFont(Math.Max((Font.Size - 2f) / 2f, 4f) * 1.5f),
                        Text = monitor.Resolution
                    };
                    badge.Location = new Point(
                        label.Right + BadgeSpacing,
                        label.Top + ((label.Height - badge.Height) / 2));
                    _card.Controls.Add(badge);
                }

                y += label.Height + 2;

                var slider = new FluentSlider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = monitor.Brightness,
                    Width = cardContentWidth,
                    Location = new Point(CardPadding, y)
                };
                slider.ValueChanged += OnSliderValueChanged;
                _card.Controls.Add(slider);
                y += slider.Height;

                _rows.Add(new MonitorRow
                {
                    MonitorId = monitor.Id,
                    Label = label,
                    Slider = slider,
                    ResolutionBadge = badge,
                    TopDivider = divider
                });

                isFirstRow = false;
            }

            if (_rows.Count == 0)
            {
                _noMonitorsLabel = new Label
                {
                    Text = "No controllable monitors detected.",
                    AutoSize = true,
                    Location = new Point(CardPadding, y)
                };
                _card.Controls.Add(_noMonitorsLabel);
                y += _noMonitorsLabel.Height;
            }

            y += CardPadding;
            _card.Height = Math.Max(y, CardPadding * 2);

            int syncY = _card.Bottom + RowSpacing;
            _syncLabel.Location = new Point(EdgePadding, syncY + ((ToggleHeight - _syncLabel.Height) / 2));
            _syncCheckBox.Location = new Point(FormWidth - EdgePadding - ToggleWidth, syncY);

            int bottom = Math.Max(_syncLabel.Bottom, _syncCheckBox.Bottom) + EdgePadding;
            ClientSize = new Size(FormWidth, bottom);

            ApplyTheme();
        }

        private void OnSliderValueChanged(object? sender, EventArgs e)
        {
            if (_isApplyingProgrammatically)
            {
                return;
            }

            if (sender is not FluentSlider slider)
            {
                return;
            }

            var changedRow = _rows.Find(r => r.Slider == slider);
            if (changedRow == null)
            {
                return;
            }

            int value = slider.Value;

            if (_syncCheckBox.Checked)
            {
                _isApplyingProgrammatically = true;
                try
                {
                    foreach (MonitorRow row in _rows)
                    {
                        if (row.Slider != slider)
                        {
                            row.Slider.Value = value;
                        }
                        _pendingMonitorIds.Add(row.MonitorId);
                    }
                }
                finally
                {
                    _isApplyingProgrammatically = false;
                }
            }
            else
            {
                _pendingMonitorIds.Add(changedRow.MonitorId);
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();

            if (_pendingMonitorIds.Count == 0)
            {
                return;
            }

            var toApply = new List<(string Id, int Value)>();
            foreach (string id in _pendingMonitorIds)
            {
                var row = _rows.Find(r => r.MonitorId == id);
                if (row != null)
                {
                    toApply.Add((id, row.Slider.Value));
                }
            }
            _pendingMonitorIds.Clear();

            // Push the actual DDC/CI or WMI calls off the UI thread -- each
            // can take tens of milliseconds and would otherwise stall the UI.
            Task.Run(() =>
            {
                foreach (var (id, value) in toApply)
                {
                    _service.SetBrightness(id, value);
                }
            });
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // WS_EX_TOOLWINDOW keeps the popup out of the Alt-Tab list,
                // consistent with it being a transient tray flyout.
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }
}
