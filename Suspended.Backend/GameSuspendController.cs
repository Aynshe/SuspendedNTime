using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using System.Drawing;
using System.Xml.Linq;

namespace Suspended.Backend
{

    public static class GameSuspendController
    {

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtResumeProcess(IntPtr processHandle);

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MINIMIZE = 6;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_FORCEMINIMIZE = 11;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Whitelisted processes that should never be suspended
        private static readonly string[] WhitelistedProcesses =
        {
            "ApplicationFrameHost",
            "dwm",
            "explorer",
            "perfmon",
            "SystemSettings",
            "Taskmgr",
            "TextInputHost",
            "WinStore.App",
            "steamwebhelper",
            "EpicGamesLauncher",
            "Tooth",
            "Suspended",
            "WindowsTerminal",
            "devenv",
            "msedge",
            "Code",
            "Discord",
            "NVIDIA Overlay",
            "NVIDIA App",
            "Notepad",
            "XboxPcApp",
            "Gamebar_Widget",
            "MSI Center M",
            "IntelGraphicsSoftware"
        };

        public static List<IntPtr> GetAllVisibleWindowsForProcessTree(int rootPid)
        {
            var pids = new HashSet<int> { rootPid };
            
            var queue = new Queue<int>();
            queue.Enqueue(rootPid);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int child in GetChildProcesses(current))
                {
                    if (pids.Add(child))
                        queue.Enqueue(child);
                }
            }

            var hwnds = new List<IntPtr>();
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint windowPid);
                if (pids.Contains((int)windowPid) && IsWindowVisible(h))
                {
                    hwnds.Add(h);
                }
                return true; 
            }, IntPtr.Zero);

            foreach (int pid in pids)
            {
                try
                {
                    using (var p = Process.GetProcessById(pid))
                    {
                        if (p.MainWindowHandle != IntPtr.Zero && !hwnds.Contains(p.MainWindowHandle))
                            hwnds.Add(p.MainWindowHandle);
                    }
                }
                catch { }
            }

            return hwnds;
        }

        public static bool IsProcessSuspended(Process process)
        {
            try
            {
                if (process.Threads.Count == 0) return false;
                
                int suspendedCount = 0;
                foreach (ProcessThread thread in process.Threads)
                {
                    if (thread.ThreadState == ThreadState.Wait &&
                        thread.WaitReason == ThreadWaitReason.Suspended)
                    {
                        suspendedCount++;
                    }
                }
                
                // Si la très grande majorité des threads (plus de 80%) sont endormis, 
                // alors le processus a été suspendu par NtSuspendProcess.
                return ((double)suspendedCount / process.Threads.Count) >= 0.80;
            }
            catch
            {
                // Process might have exited or is protected
            }

            return false;
        }

        private static bool IsWhitelisted(string processName)
        {
            return WhitelistedProcesses.Any(p =>
                string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));
        }

        private static void ForceForegroundWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            IntPtr hForeground = GetForegroundWindow();
            if (hForeground == hWnd) return;

            uint currentThreadId = GetCurrentThreadId();
            uint foregroundThreadId = GetWindowThreadProcessId(hForeground, IntPtr.Zero);
            uint targetThreadId = GetWindowThreadProcessId(hWnd, IntPtr.Zero);

            bool attachedForeground = false;
            bool attachedTarget = false;

            if (foregroundThreadId != 0 && currentThreadId != foregroundThreadId)
            {
                attachedForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (targetThreadId != 0 && currentThreadId != targetThreadId && targetThreadId != foregroundThreadId)
            {
                attachedTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            // EN: Force the window to become topmost briefly, then revert it, while bringing it to top
            // FR: Forcer la fenêtre à devenir TopMost temporairement, la ramener, puis enlever TopMost
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            BringWindowToTop(hWnd);
            ShowWindowAsync(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);

            if (attachedForeground) AttachThreadInput(currentThreadId, foregroundThreadId, false);
            if (attachedTarget) AttachThreadInput(currentThreadId, targetThreadId, false);
        }

        public static async Task SuspendForegroundApp()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("[GameSuspendController] No foreground window detected.");
                return;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    if (IsWhitelisted(process.ProcessName)) return;

                    CaptureGameScreenAndXml(process.ProcessName);

                    var hwnds = GetAllVisibleWindowsForProcessTree(process.Id);
                    if (!hwnds.Contains(hwnd)) hwnds.Add(hwnd); // Include the foreground one implicitly
                    
                    foreach (var h in hwnds)
                    {
                        ShowWindowAsync(h, SW_MINIMIZE);
                    }
                    await Task.Delay(500);

                    Console.WriteLine($"[GameSuspendController] Suspending process: {process.ProcessName} ({processId})");
                    SuspendProcessTree(process.Id);
                    
                    CreateSuspendKey(process.ProcessName, process.Id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to suspend process: {ex.Message}");
            }
        }
        public static async Task SuspendApp(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (IsWhitelisted(process.ProcessName)) return;

                    CaptureGameScreenAndXml(process.ProcessName);

                    var hwnds = GetAllVisibleWindowsForProcessTree(processId);
                    foreach (var h in hwnds)
                    {
                        ShowWindowAsync(h, SW_MINIMIZE);
                    }
                    if (hwnds.Count > 0) await Task.Delay(500);

                    Console.WriteLine($"[GameSuspendController] Suspending process: {process.ProcessName} ({processId})");
                    SuspendProcessTree(processId);
                    
                    CreateSuspendKey(process.ProcessName, processId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to suspend process: {ex.Message}");
            }
        }

        public static async Task ResumeForegroundApp()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint processId);

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    if (IsWhitelisted(process.ProcessName)) return;

                    Console.WriteLine($"[GameSuspendController] Resuming process: {process.ProcessName} ({processId})");
                    ResumeProcessTree(process.Id);

                    RemoveSuspendKey(process.ProcessName);

                    await Task.Delay(200);
                    
                    var hwnds = GetAllVisibleWindowsForProcessTree(process.Id);
                    if (!hwnds.Contains(hwnd)) hwnds.Add(hwnd);
                    
                    foreach (var h in hwnds)
                    {
                        ForceForegroundWindow(h);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to resume process: {ex.Message}");
            }
        }

        public static async Task ResumeApp(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (IsWhitelisted(process.ProcessName)) return;

                    Console.WriteLine($"[GameSuspendController] Resuming process: {process.ProcessName} ({processId})");
                    ResumeProcessTree(processId);

                    RemoveSuspendKey(process.ProcessName);

                    await Task.Delay(200);
                    var hwnds = GetAllVisibleWindowsForProcessTree(processId);
                    foreach (var h in hwnds)
                    {
                        ForceForegroundWindow(h);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to resume process: {ex.Message}");
            }
        }

        private static List<int> GetChildProcesses(int parentPid)
        {
            var childPids = new List<int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE)
                return childPids;

            try
            {
                PROCESSENTRY32 entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snapshot, ref entry))
                    return childPids;

                do
                {
                    if (entry.th32ParentProcessID == parentPid)
                        childPids.Add((int)entry.th32ProcessID);
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return childPids;
        }

        public static void SuspendProcessTree(int pid)
        {
            if (pid == 0) return;

            SuspendOrResumeProcess(pid, suspend: true);

            foreach (var childPid in GetChildProcesses(pid))
                SuspendProcessTree(childPid);
        }

        public static void ResumeProcessTree(int pid)
        {
            if (pid == 0) return;

            SuspendOrResumeProcess(pid, suspend: false);

            foreach (var childPid in GetChildProcesses(pid))
                ResumeProcessTree(childPid);
        }


        private static void SuspendOrResumeProcess(int pid, bool suspend)
        {
            IntPtr hProc = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (hProc == IntPtr.Zero)
            {
                Console.WriteLine($"[ProcessTreeController] Cannot open PID {pid}. Error: {Marshal.GetLastWin32Error()}");
                return;
            }

            try
            {
                uint result = suspend ? NtSuspendProcess(hProc) : NtResumeProcess(hProc);
                Console.WriteLine($"[{(suspend ? "Suspend" : "Resume")}] PID {pid} success: {result == 0}");
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        private static string GetRetroBatInstallPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\RetroBat"))
                {
                    if (key != null)
                    {
                        object path = key.GetValue("LatestKnownInstallPath");
                        if (path != null)
                        {
                            return path.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to read RetroBat registry: {ex.Message}");
            }
            return null;
        }

        public static void CaptureGameScreenAndXml(string processName)
        {
            string retroBatPath = GetRetroBatInstallPath();
            if (string.IsNullOrEmpty(retroBatPath)) return;

            string targetDir = Path.Combine(retroBatPath, "user", "SuspendedNTime");
            string imageFileName = $"{processName}-marquee.png";
            
            TakeScreenshot(targetDir, imageFileName);
            AddToGamelistXml(processName, targetDir, imageFileName);
        }

        private static void CreateSuspendKey(string processName, int processId)
        {
            string retroBatPath = GetRetroBatInstallPath();
            if (string.IsNullOrEmpty(retroBatPath)) return;

            try
            {
                string targetDir = Path.Combine(retroBatPath, "user", "SuspendedNTime");
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                string keyFilePath = Path.Combine(targetDir, $"{processName}.key");
                string executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                File.WriteAllText(keyFilePath, $"{processId}\n{executablePath}");
                Console.WriteLine($"[GameSuspendController] Created suspend key: {keyFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to create suspend key: {ex.Message}");
            }
        }

        private static void TakeScreenshot(string targetDir, string imageFileName)
        {
            try
            {
                string imagesDir = Path.Combine(targetDir, "images");
                if (!Directory.Exists(imagesDir)) Directory.CreateDirectory(imagesDir);
                
                string imagePath = Path.Combine(imagesDir, imageFileName);
                
                Rectangle bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
                    }
                    bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to take screenshot: {ex.Message}");
            }
        }

        private static void AddToGamelistXml(string processName, string targetDir, string imageFileName)
        {
            try
            {
                string xmlPath = Path.Combine(targetDir, "gamelist.xml");
                XDocument doc;
                if (File.Exists(xmlPath))
                {
                    try { doc = XDocument.Load(xmlPath); }
                    catch { doc = new XDocument(new XElement("gameList")); }
                }
                else
                {
                    doc = new XDocument(new XElement("gameList"));
                }

                string keyPath = $"./{processName}.key";
                var gameList = doc.Element("gameList");
                
                var existingGame = gameList.Elements("game").FirstOrDefault(g => (string)g.Element("path") == keyPath);
                if (existingGame != null)
                    existingGame.Remove();

                var newGame = new XElement("game",
                    new XElement("path", keyPath),
                    new XElement("name", processName),
                    new XElement("releasedate", DateTime.Now.ToString("yyyyMMddTHHmmss")),
                    new XElement("desc", $"Jeu suspendu : {processName}"),
                    new XElement("marquee", $"./images/{imageFileName}"),
                    new XElement("fanart", ""),
                    new XElement("image", ""),
                    new XElement("thumbnail", "")
                );
                gameList.Add(newGame);
                doc.Save(xmlPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to update XML: {ex.Message}");
            }
        }

        private static void RemoveFromGamelistXml(string processName, string targetDir)
        {
            try
            {
                string xmlPath = Path.Combine(targetDir, "gamelist.xml");
                if (!File.Exists(xmlPath)) return;

                XDocument doc = XDocument.Load(xmlPath);
                string keyPath = $"./{processName}.key";
                var gameList = doc.Element("gameList");
                
                var existingGame = gameList?.Elements("game").FirstOrDefault(g => (string)g.Element("path") == keyPath);
                if (existingGame != null)
                {
                    existingGame.Remove();
                    doc.Save(xmlPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to update XML: {ex.Message}");
            }
        }

        private static void RemoveSuspendKey(string processName)
        {
            string retroBatPath = GetRetroBatInstallPath();
            if (string.IsNullOrEmpty(retroBatPath)) return;

            try
            {
                string keyFilePath = Path.Combine(retroBatPath, "user", "SuspendedNTime", $"{processName}.key");
                if (File.Exists(keyFilePath))
                {
                    File.Delete(keyFilePath);
                    Console.WriteLine($"[GameSuspendController] Removed suspend key: {keyFilePath}");
                }

                string targetDir = Path.Combine(retroBatPath, "user", "SuspendedNTime");
                RemoveFromGamelistXml(processName, targetDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSuspendController] Failed to remove suspend key: {ex.Message}");
            }
        }
    }
}
