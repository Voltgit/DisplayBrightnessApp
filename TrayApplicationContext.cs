using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Owns the tray icon, its right-click "Exit" menu, and opens/positions
    /// the brightness popup on left-click. There is no main form -- this
    /// ApplicationContext is the entire lifetime of the app.
    /// </summary>
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly MonitorBrightnessService _monitorService;
        private BrightnessPopupForm? _popup;
        private Icon? _trayIcon;

        public TrayApplicationContext()
        {
            _monitorService = new MonitorBrightnessService();

            var exitMenuItem = new ToolStripMenuItem("Exit");
            exitMenuItem.Click += (_, _) => ExitApplication();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(exitMenuItem);

            _trayIcon = TrayIconFactory.CreateMonitorIcon(ThemeHelper.IsSystemLightTheme());

            _notifyIcon = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "Display Brightness",
                Visible = true,
                ContextMenuStrip = contextMenu
            };
            _notifyIcon.MouseClick += OnNotifyIconMouseClick;

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ToggleBrightnessPopup();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)
            {
                return;
            }

            RegenerateTrayIcon();
        }

        private void RegenerateTrayIcon()
        {
            Icon newIcon = TrayIconFactory.CreateMonitorIcon(ThemeHelper.IsSystemLightTheme());
            Icon? oldIcon = _trayIcon;

            _trayIcon = newIcon;
            _notifyIcon.Icon = newIcon;

            oldIcon?.Dispose();
        }

        private void ToggleBrightnessPopup()
        {
            if (_popup is { IsDisposed: false })
            {
                // Close() now plays an animated fade/slide-out before the
                // form actually closes -- the FormClosed handler wired up
                // below nulls _popup once that finishes for real, so a
                // repeat click while it's still closing just re-requests
                // the close rather than orphaning a visible popup.
                _popup.Close();
                return;
            }

            _monitorService.Refresh();

            var popup = new BrightnessPopupForm(_monitorService, Cursor.Position);
            _popup = popup;
            popup.FormClosed += (_, _) =>
            {
                if (_popup == popup)
                {
                    _popup = null;
                }
            };

            popup.Show();

            // Explorer (not this process) owns the tray click, so the new
            // window isn't reliably given focus by Show()/Activate() alone --
            // without this, WinForms can fire OnDeactivate synchronously and
            // the popup closes itself before the user ever sees it.
            if (!popup.IsDisposed)
            {
                NativeMethods.SetForegroundWindow(popup.Handle);
                popup.Activate();
            }
        }

        private void ExitApplication()
        {
            _notifyIcon.Visible = false;
            _popup?.Close();
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayIcon?.Dispose();
            _trayIcon = null;
            _monitorService.Dispose();
            base.ExitThreadCore();
        }
    }
}
