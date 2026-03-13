using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Controls;
using System.Text.Json;

namespace Suspended.Backend
{
    internal class Handler
    {
        private PowerPolicyController powerPolicyController;
        private ModernStandbyMonitor modernStandbymonitor;
        private Communication _communication;
        private WindowProcessManager GameProcessManager;
        private readonly object gameListLock = new();

        private List<GameInfo> gameList;

        public enum ScalingModeMethod : int
        {
            DISPLAY_SCALING_MAINTAIN_ASPECT_RATIO = 0,
            GPU_SCALING_MAINTAIN_ASPECT_RATIO = 1,
            GPU_SCALING_STRETCH = 2,
            GPU_SCALING_CENTER = 3,
            RETRO_SCALING_INTEGER = 4,
            RETRO_SCALING_NEAREST_NEIGHBOUR = 5,
            UNKNOWN = 6
        }

        public Handler()
        {
            powerPolicyController = new PowerPolicyController();
            modernStandbymonitor = new ModernStandbyMonitor();

            GameProcessManager = new WindowProcessManager(TimeSpan.FromMilliseconds(1000));
            GameProcessManager.OnWindowAppeared += NewGameWindowAppeared;
            GameProcessManager.OnWindowClosed += GameWindowDisappeared;
            GameProcessManager.OnForegroundChanged += ForegroundGameWindowChanged;
            GameProcessManager.OnProcessSuspended += GameProcessSuspended;
            GameProcessManager.OnProcessResumed += GameProcessResumed;

            int suspendOnFocusLoss = SettingsManager.Get<int>("SuspendOnFocusLoss");
            if (suspendOnFocusLoss == 1)
                GameProcessManager.suspendOnFocusLost = true;
            else
                GameProcessManager.suspendOnFocusLost = false;

            GameProcessManager.StartRefresh();
        }

        public void Register(Communication comm)
        {
            _communication = comm;
            comm.ConnectedEvent += OnConnected;
            comm.ReceivedEvent += OnReceived;
        }

        void OnConnected(object sender, EventArgs e)
        {
            (sender as Communication).Send("connected");
        }

        void OnReceived(object sender, string message)
        {
            var comm = sender as Communication;
            string[] args = message.Split(' ');
            Console.WriteLine($"[Server Handler] OnReceived {message}");
            if (args.Length == 0)
                return;
            switch (args[0])
            {
                case "get-auto-suspend":
                    {
                        int autosuspend = SettingsManager.Get<int>("AutoSuspend");
                        Console.WriteLine($"[Server Handler] Responding with AutoSuspend Value {autosuspend}");
                        (sender as Communication).Send($"auto-suspend" + ' ' + $"{autosuspend}");
                    }
                    break;
                case "set-auto-suspend":
                    {
                        if (int.TryParse(args[1], out int autosuspend))
                        {
                            SettingsManager.Set("AutoSuspend", autosuspend);
                            modernStandbymonitor.UpdateAutoSuspendSetting(autosuspend);
                            Console.WriteLine($"[Server Handler] Setting AutoSuspend to {autosuspend}");
                        }
                    }
                    break;
                case "resume-active-game":
                    {
                        // EN: Global Resume Logic: if focus app is suspended, resume it. 
                        // Otherwise, resume the first suspended game we find in our list.
                        if (GameProcessManager.IsForegroundAppTracked && GameProcessManager.IsForegroundAppSuspended)
                        {
                            GameSuspendController.ResumeForegroundApp();
                            Console.WriteLine($"[Server Handler] Resumed Foreground App");
                        }
                        else
                        {
                            var suspendedGame = GameProcessManager.GetGamesList().FirstOrDefault(g => g.IsSuspended);
                            if (suspendedGame.ProcessId != 0)
                            {
                                GameSuspendController.ResumeApp(suspendedGame.ProcessId);
                                Console.WriteLine($"[Server Handler] Global Resume: Resumed PID {suspendedGame.ProcessId}");
                            }
                        }
                    }
                    break;
                case "suspend-active-game":
                    {
                        GameSuspendController.SuspendForegroundApp();
                        Console.WriteLine($"[Server Handler] Suspended Foreground App");
                    }
                    break;

                case "get-go-back-to-sleep":
                    {
                        int gobacktosleep = SettingsManager.Get<int>("GoBackToSleep");
                        Console.WriteLine($"[Server Handler] Responding with Go back to sleep Value {gobacktosleep}");
                        (sender as Communication).Send($"go-back-to-sleep" + ' ' + $"{gobacktosleep}");
                    }
                    break;
                case "set-go-back-to-sleep":
                    {
                        if (int.TryParse(args[1], out int gobacktosleep))
                        {
                            SettingsManager.Set("GoBackToSleep", gobacktosleep);
                            modernStandbymonitor.UpdateGoBackToSleepSetting(gobacktosleep);
                            Console.WriteLine($"[Server Handler] Setting Go back to sleep to {gobacktosleep}");
                        }
                    }
                    break;

                case "get-power-button-action":
                    {
                        if (powerPolicyController == null)
                        {
                            powerPolicyController = new PowerPolicyController();
                        }
                        
                        // EN: Get from settings first, if not set, get from system
                        // FR: Récupérer des paramètres d'abord, sinon du système
                        int savedAction = SettingsManager.Get<int>("PowerButtonAction");
                        
                        Console.WriteLine($"[Server Handler] Responding with Power Button Action {savedAction}");
                        (sender as Communication).Send("power-button-action" + ' ' + savedAction);
                    }
                    break;
                case "set-power-button-action":
                    {
                        if (powerPolicyController == null)
                        {
                            powerPolicyController = new PowerPolicyController();
                        }
                        Console.WriteLine($"[Server Handler] Setting Power Button Action to {args[1]}");
                        if (int.TryParse(args[1], out int actionValue))
                        {
                            SettingsManager.Set("PowerButtonAction", actionValue);
                            if (Enum.IsDefined(typeof(PowerPolicyController.PowerButtonAction), actionValue))
                            {
                                powerPolicyController.SetPowerButtonAction((PowerPolicyController.PowerButtonAction)actionValue);
                            }
                        }
                    }
                    break;

                case "get-enhanced-sleep":
                    {
                        int enhancedSleep = SettingsManager.Get<int>("EnhancedSleep");
                        Console.WriteLine($"[Server Handler] Responding with Enhanced sleep Value {enhancedSleep}");
                        (sender as Communication).Send($"enhanced-sleep" + ' ' + $"{enhancedSleep}");
                    }
                    break;
                case "set-enhanced-sleep":
                    {
                        if (int.TryParse(args[1], out int enhancedSleep))
                        {
                            SettingsManager.Set("EnhancedSleep", enhancedSleep);
                            Console.WriteLine($"[Server Handler] Setting Enhanced Sleep to {enhancedSleep}");
                            if (powerPolicyController == null)
                            {
                                powerPolicyController = new PowerPolicyController();
                            }
                            if(enhancedSleep == 0)
                            {
                                powerPolicyController.RestoreAll();
                            }
                            else
                            {
                                powerPolicyController.ApplyAll();
                            }
                        }
                    }
                    break;
                case "resume-game":
                    {
                        Console.WriteLine($"[Server Handler] Resume-game with PID {args[1]}");

                        if (int.TryParse(args[1], out int processId))
                        {
                            GameSuspendController.ResumeApp(processId);
                            Console.WriteLine($"[Server Handler] ResumedForeground App");
                        }
                    }
                    break;
                case "suspend-game":
                    {
                        Console.WriteLine($"[Server Handler] Suspend game with PID {args[1]}");

                        if (int.TryParse(args[1], out int processId))
                        {
                            GameSuspendController.SuspendApp(processId);
                            Console.WriteLine($"[Server Handler] Suspended Foreground App");
                        }
                    }
                    break;

                case "get-game-list":
                    {
                        Console.WriteLine($"[Server Handler] Get Games List");
                        string json;
                        lock (gameListLock)
                        {
                            json = GetFilteredGameListJson();
                        }
                        Console.WriteLine($"[Server Handler] Replying with the following List {json}");
                        (sender as Communication).Send("game-list " + json);
                    }
                    break;

                case "get-backend-info":
                    {
                        string path = GameProcessManager.LocalStatePath;
                        Console.WriteLine($"[Server Handler] Backend info requested. Path: {path}");
                        (sender as Communication).Send($"backend-info {path}");
                    }
                    break;

                case "set-refresh-list":
                    {
                        Console.WriteLine($"[Server Handler] Refresh Game List State Periodically {args[1]}");
                        if (int.TryParse(args[1], out int enableRefresh))
                        {
                            if (enableRefresh == 1)
                            {
                                GameProcessManager.SetRefreshRate(TimeSpan.FromMilliseconds(500));
                                GameProcessManager.StartRefresh();
                                Console.WriteLine($"[Server Handler] Started Game List Refresh");
                            }
                            else
                            {
                                // refresh slower
                                GameProcessManager.SetRefreshRate(TimeSpan.FromMilliseconds(3000));
                                GameProcessManager.StartRefresh();
                                Console.WriteLine($"[Server Handler] Stopped Game List Refresh");
                            }
                        }
                    }
                    break;

                case "get-suspend-focus-loss":
                    {
                        int suspendOnFocusLoss = SettingsManager.Get<int>("SuspendOnFocusLoss");
                        Console.WriteLine($"[Server Handler] Responding with Suspend On Focus Loss {suspendOnFocusLoss}");
                        (sender as Communication).Send($"suspend-focus-loss" + ' ' + $"{suspendOnFocusLoss}");
                    }
                    break;
                case "set-suspend-focus-loss":
                    {
                        if (int.TryParse(args[1], out int suspendOnFocusLoss))
                        {
                            SettingsManager.Set("SuspendOnFocusLoss", suspendOnFocusLoss);
                            if (suspendOnFocusLoss == 1)
                                GameProcessManager.suspendOnFocusLost = true;
                            else
                                GameProcessManager.suspendOnFocusLost = false;
                            Console.WriteLine($"[Server Handler] Setting Suspend On Focus Loss to {suspendOnFocusLoss}");
                        }
                    }
                    break;
                case "restart-service":
                    {
                        Console.WriteLine("[Server Handler] Restart service requested. Killing Game Bar processes...");
                        string[] gamebarProcs = { "GameBar", "GameBarFT", "GameBarPresenceWriter", "XboxGameBar", "XboxGameBarWidgets" };
                        foreach (var name in gamebarProcs)
                        {
                            var procs = System.Diagnostics.Process.GetProcessesByName(name);
                            foreach (var p in procs)
                            {
                                try { p.Kill(); } catch { }
                            }
                        }
                        
                        Console.WriteLine("[Server Handler] Game Bar processes killed. Exiting backend...");
                        Environment.Exit(0);
                    }
                    break;

                case "get-foreground-suspended":
                    {
                        Console.WriteLine($"[Server Handler] Responding with current foreground is suspeneded {GameProcessManager.IsForegroundAppSuspended}");
                        (sender as Communication).Send($"foreground-suspended" + ' ' + $"{GameProcessManager.IsForegroundAppSuspended}");
                    }
                    break;
                case "get-foreground-tracked":
                    {
                        Console.WriteLine($"[Server Handler] Responding with current foreground is tracked {GameProcessManager.IsForegroundAppTracked}");
                        (sender as Communication).Send($"foreground-tracked" + ' ' + $"{GameProcessManager.IsForegroundAppTracked}");
                    }
                    break;
                default:
                    break;
            }
        }

        public void sendLaunchGameBarWidget()
        {
            if (_communication != null) { 
                Console.WriteLine($"[Server Handler] Send launch-gamebar-widget");
                _communication.Send("launch-gamebar-widget");
            }
        }

        private void NewGameWindowAppeared(WindowInfo win)
        {
            Console.WriteLine($"[Server Handler] NewGameWindowAppeared PID={win.ProcessId}, Name='{win.ProcessName}'");
            if (_communication != null)
            {
                string json;
                lock (gameListLock) { json = GetFilteredGameListJson(); }
                _communication.Send("game-list " + json);
            }
        }

        private void GameWindowDisappeared(WindowInfo win)
        {
            Console.WriteLine($"[Server Handler] GameWindowDisappeared PID={win.ProcessId}, Name='{win.ProcessName}'");
            if (_communication != null)
            {
                string json;
                lock (gameListLock) { json = GetFilteredGameListJson(); }
                _communication.Send("game-list " + json);
            }
        }

        private void ForegroundGameWindowChanged(WindowInfo win)
        {
            Console.WriteLine($"[Server Handler] ForegroundGameWindowChanged PID={win.ProcessId}, Hwnd={win.Handle}, Name='{win.ProcessName}' , Title='{win.Title}', IsSuspended='{win.IsSuspended}', Icon Path= {win.ProcessIconPath} ");
            _communication.Send("foreground-suspended " + GameProcessManager.IsForegroundAppSuspended);
            _communication.Send("foreground-tracked " + GameProcessManager.IsForegroundAppTracked);
        }

        private void GameProcessSuspended(WindowInfo win)
        {
            Console.WriteLine($"[Server Handler] GameProcessSuspended PID={win.ProcessId}, Name='{win.ProcessName}'");
            if (_communication != null)
            {
                string json;
                lock (gameListLock) { json = GetFilteredGameListJson(); }
                _communication.Send("game-list " + json);
            }
        }

        private void GameProcessResumed(WindowInfo win)
        {
            Console.WriteLine($"[Server Handler] GameProcessResumed PID={win.ProcessId}, Name='{win.ProcessName}'");
            if (_communication != null)
            {
                string json;
                lock (gameListLock) { json = GetFilteredGameListJson(); }
                _communication.Send("game-list " + json);
            }
        }

        // EN: Returns a filtered JSON list of game processes (excludes whitelisted system apps)
        // FR: Retourne la liste filtrée des processus jeux (exclut les apps système)
        private string GetFilteredGameListJson()
        {
            gameList = GameProcessManager.GetGamesList();
            var filtered = gameList.Where(g =>
                !GameSuspendController.WhitelistedProcesses.Any(w =>
                    string.Equals(w, g.ProcessName, StringComparison.OrdinalIgnoreCase))
            ).ToList();
            return System.Text.Json.JsonSerializer.Serialize(filtered);
        }
    }
}
