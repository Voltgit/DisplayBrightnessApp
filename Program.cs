using System;
using System.Windows.Forms;

namespace DisplayBrightnessApp
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application. Starts a tray-only
        /// ApplicationContext -- there is no main form shown on startup.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            StartupRegistration.EnsureRegistered();

            using var context = new TrayApplicationContext();
            Application.Run(context);
        }
    }
}
