using System;
using System.IO;
using System.Windows;
using System.Drawing;
using System.Windows.Forms;

namespace AmlaDeveloperAssistantApp
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon trayIcon;
        private MainWindow window;

        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // ✅ SINGLE INSTANCE CHECK
            bool createdNew;
            _mutex = new Mutex(true, "AmlaDeveloperAssistantApp", out createdNew);

            if (!createdNew)
            {
                // App already running → exit this instance
                Shutdown();
                return;
            }

            base.OnStartup(e);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = new Icon("app.ico");

            // Load icon from project directory
            string iconPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "app.ico"
            );
                       
            trayIcon.Visible = true;
            trayIcon.Text = "Amla Developer Assistant";

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Assistant", null, OnOpen);
            menu.Items.Add("Exit", null, OnExit);

            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += OnOpen;
        }

        private void OnOpen(object sender, EventArgs e)
        {
            if (window == null || !window.IsLoaded)
            {
                window = new MainWindow();

                window.Closed += (s, args) =>
                {
                    window = null;
                };

                window.Show();
            }
            else
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;

                window.Activate();
                window.Topmost = true;   // bring front
                window.Topmost = false;  // reset
                window.Focus();
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Shutdown();
        } 
    }
}
