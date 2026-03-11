using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using WindowsInput;
using WindowsInput.Native;

namespace Suspended.Backend
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        private static Mutex _mutex;
        private static Handler handler;
        const string PROGRAM_NAME = "SuspendedNTime.Service";

        [STAThread]
        static void Main(string[] args)
        {
            // Hide console window only in Release mode (optional)
#if !DEBUG
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);
#endif

            // Safe args access
            string arg0 = args?.Length > 0 ? args[0] : null;

            // EN: Handle -resume argument BEFORE the mutex check,
            // because the backend may already be running (listening for combos)
            // and the mutex would kill this instance before it could resume the game.
            // FR: Traiter l'argument -resume AVANT la vérification du mutex,
            // car le backend peut déjà tourner et bloquerait cette instance.
            if (args != null && args.Length >= 2 && args[0].ToLowerInvariant() == "-resume")
            {
                Console.WriteLine($"[Program] Resuming process {args[1]}");
                if (int.TryParse(args[1], out int pid))
                {
                    // Call synchronous since we will exit right after
                    Task.Run(() => GameSuspendController.ResumeApp(pid)).Wait();
                }
                return;
            }

            _mutex = new Mutex(true, "Suspended.Backend");
            if (!_mutex.WaitOne(TimeSpan.Zero, true))
            {
                //MessageBox.Show("Suspended Backend is already running.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Start your backend communication
            Console.WriteLine($"{PROGRAM_NAME}");
            Console.WriteLine($"[Program] Started");

            Application.SetCompatibleTextRenderingDefault(false);

            // Prepare cancellation for background services
            var cts = new CancellationTokenSource();

            // Create ApplicationContext early so we have a UI context for non-blocking startup tasks
            var trayContext = new TrayAppContext(cts);

            // Do potentially blocking init here
            SettingsManager.Initialize();

            string packageSid;
            if (!string.IsNullOrEmpty(arg0) && arg0.StartsWith("S-1-"))
                packageSid = arg0;
            else
                packageSid = ApplicationData.Current.LocalSettings.Values["PackageSid"] as string;

            var comm = new Communication(packageSid);
            handler = new Handler();

            handler.Register(comm);
            // Start the comm loop and keep a reference so we can cancel it later
            var commTask = Task.Run(comm.Run);

            // Start combo listener on UI thread if it relies on message pump, otherwise you can run it on background.
            var comboListener = new XboxComboListener();

            comboListener.ComboPressed += () =>
            {
                Console.WriteLine("View + X pressed!");
                LaunchSuspendedGameBar();
                LaunchSuspendedGameBarWidget();
            };

            comboListener.ComboReleased += () =>
            {
                Console.WriteLine("View + A released.");
            };

            comboListener.Start();

            // Now run WinForms message loop (keeps process alive, handles tray icon)
            Application.Run(trayContext);
        }

        public class TrayAppContext : ApplicationContext
        {
            private NotifyIcon trayIcon;
            private readonly CancellationTokenSource _cts;

            public TrayAppContext(CancellationTokenSource cts)
            {
                _cts = cts ?? new CancellationTokenSource();

                trayIcon = new NotifyIcon()
                {
                    Icon = Properties.Resources.suspended,
                    Text = "SuspendedNTime Service",
                    Visible = true,
                    ContextMenuStrip = new ContextMenuStrip()
                    {
                        Items =
                        {
                            //new ToolStripMenuItem("Show/Hide", null, OnOpen),
                            new ToolStripMenuItem("Exit", null, OnExit)
                        }
                    }
                };

                trayIcon.DoubleClick += OnOpen;
            }

            void OnOpen(object sender, EventArgs e)
            {

                var handle = GetConsoleWindow();
                if (handle == IntPtr.Zero)
                    return;
                // Check visibility
                bool isVisible = IsWindowVisible(handle);

                if (isVisible)
                {
                    ShowWindow(handle, SW_HIDE);
                    Console.WriteLine("Console hidden.");
                }
                else
                {
                    ShowWindow(handle, SW_SHOW);
                    Console.WriteLine("Console shown.");
                }
            }

            void OnExit(object sender, EventArgs e)
            {
                trayIcon.Visible = false;
                // signal cancellation to background tasks
                _cts.Cancel();
                Application.Exit();
            }
        }

        private static void OnExitClicked(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private static void LaunchSuspendedGameBarWidget()
        {
            // defensive-null check
            try { handler?.sendLaunchGameBarWidget(); }
            catch (Exception ex) { Console.WriteLine($"[LaunchWidget] {ex}"); }
        }

        private static void LaunchSuspendedGameBar ()
        {

            // URIs doesn't work for gamebar, and neither does it work from power shell, which is very strange !
            // Is this a bug from Microsoft, because this page claims it works:
            // https://learn.microsoft.com/en-us/gaming/game-bar/api/xgb-widgetcontrol
            /*bool result = await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-gamebar:/activate/BassemNomany.BassemNomany.SuspendedNTime_ah2yj8jdj20z4.SuspendedNTime_Suspended.XboxGameBarUI_Suspended.XboxGameBarUI"));
            if(result)
            {
                Console.WriteLine("LaunchSuspendedGameBar () successful!");
            } else
            {
                Console.WriteLine("LaunchSuspendedGameBar () failed!");
            }*/
            try
            {
                var inputSimulator = new InputSimulator();
                inputSimulator.Keyboard.KeyDown(VirtualKeyCode.LWIN);
                inputSimulator.Keyboard.KeyPress(VirtualKeyCode.VK_G);
                inputSimulator.Keyboard.KeyUp(VirtualKeyCode.VK_G);
                inputSimulator.Keyboard.KeyUp(VirtualKeyCode.LWIN);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LaunchGameBar] {ex}");
            }
        }

    }
}
