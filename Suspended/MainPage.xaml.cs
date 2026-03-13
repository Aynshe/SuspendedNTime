using Microsoft.Gaming.XboxGameBar;
using SharpDX.Direct3D9;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.ServiceModel.Channels;
using System.Text.Json;
using System.Windows.Input;
using Windows.ApplicationModel.AppExtensions;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Security.Authentication.Web;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Suspended
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a <see cref="Frame">.
    /// </summary>
    public sealed partial class MainPage : IDisposable
    {


        private static MainPageModel _base = new MainPageModel();
        private MainPageModelWrapper _model;

        public MainPage()
        {
            this.InitializeComponent();

            _model = _base.GetWrapper(this.Dispatcher);
            this.DataContext = _model;

            Backend.Instance.MessageReceivedEvent += Backend_OnMessageReceived;
            Backend.Instance.ConnectionChangedEvent += Backend_OnConnectionChanged;
            Backend.Instance.ClosedOrFailedEvent += Backend_OnClosedOrFailed;
            if (Backend.Instance.IsConnected)
            {
                ConnectedInitialize();
                Backend.Instance.Send("get-backend-info");
            }
            else
                PanelSwitch(false);
        }

        public void Dispose()
        {
            Backend.Instance.MessageReceivedEvent -= Backend_OnMessageReceived;
            Backend.Instance.ConnectionChangedEvent -= Backend_OnConnectionChanged;
            Backend.Instance.ClosedOrFailedEvent -= Backend_OnClosedOrFailed;
        }

        private bool _isDataInitialized = false;
        private void ConnectedInitialize()
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => PanelSwitch(true));
            
            if (!_isDataInitialized)
            {
                Backend.Instance.Send("get-auto-suspend");
                Backend.Instance.Send("get-go-back-to-sleep");
                Backend.Instance.Send("get-power-button-action");
                Backend.Instance.Send("get-enhanced-sleep");
                Backend.Instance.Send("get-suspend-focus-loss");
                Backend.Instance.Send("get-foreground-suspended");
                Backend.Instance.Send("get-foreground-tracked");
                Backend.Instance.Send("get-game-list");
                Backend.Instance.Send("init");
                _isDataInitialized = true;
            }
            else
            {
                // EN: Just refresh the list if already initialized once
                // FR: Rafraîchir juste la liste si déjà initialisé
                Backend.Instance.Send("get-game-list");
                Backend.Instance.Send("get-foreground-suspended");
                Backend.Instance.Send("get-foreground-tracked");
            }
        }

        private void PanelSwitch(bool isBackendAlive)
        {
            if (isBackendAlive)
            {
                StartingBackgroundserviceTextBlock.Visibility = Visibility.Collapsed;
                LaunchBackendButton.IsEnabled = false;
            }
            else
            {
                StartingBackgroundserviceTextBlock.Visibility = Visibility.Visible;
                LaunchBackendButton.IsEnabled = true;
                _isDataInitialized = false; // Reset on disconnect
            }
        }

        private void Backend_OnMessageReceived(object sender, string message)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Backend_OnMessageReceived_Impl(sender, message));
        }

        private void Backend_OnMessageReceived_Impl(object sender, string message)
        {
            // EN: If we receive any message, we are connected. Ensure UI is updated.
            // FR: Si on reçoit un message, on est connecté. S'assurer que l'UI est à jour.
            if (StartingBackgroundserviceTextBlock.Visibility == Visibility.Visible)
            {
                PanelSwitch(true);
            }

            var backend = sender as Backend;
            string[] args = message.Split(' ');
            if (args.Length == 0)
                return;
            switch (args[0])
            {
                case "connected":
                    ConnectedInitialize();
                    break;
                case "autostart":
                    _model.SetAutoStartVar(bool.Parse(args[1]));
                    break;
                case "backend-info":
                    Debug.WriteLine($"Backend Path: {message.Substring("backend-info ".Length)}");
                    break;
                case "auto-suspend":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Auto Suspend Enabled to {args[1]}");
                    _model.AutoSuspendEnabled = Convert.ToBoolean(int.Parse(args[1]));
                    AutoSuspendToggle.IsOn = _model.AutoSuspendEnabled;
                    break;
                case "go-back-to-sleep":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Go back to sleep Enabled to {args[1]}");
                    _model.GoBackToSleepEnabled = Convert.ToBoolean(int.Parse(args[1]));
                    GoBackToSleepToggle.IsOn = _model.GoBackToSleepEnabled;
                    break;
                case "power-button-action":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Power Button Action {args[1]}");
                    _model.PowerButtonAction = int.Parse(args[1]);
                    PowerButtonActionComboBox.SelectedValue = _model.PowerButtonAction;
                    break;
                case "suspend-focus-loss":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Suspend On Focus Loss to {args[1]}");
                    _model.SuspendOnFocusLoss = Convert.ToBoolean(int.Parse(args[1]));
                    AutoSuspendFocusToggle.IsOn = _model.SuspendOnFocusLoss;
                    break;
                case "game-list":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating GameList");
                    
                    args = message.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length < 2)
                    {
                        Trace.WriteLine("Malformed message: missing JSON payload");
                        break;
                    }
                    //Trace.WriteLine("Raw payload: " + args[1]);
                    
                    try
                    {
                        if (args.Length < 2)
                            return;
                        var games = System.Text.Json.JsonSerializer.Deserialize<List<GameInfo>>(args[1]);
                        _model.GamesList = new ObservableCollection<GameInfo>(games);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to parse Games List: {ex.Message}");
                    }
                    break;
                case "enhanced-sleep":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Enhanced Sleep {args[1]}");
                    _model.EnhancedSleepEnabled = Convert.ToBoolean(int.Parse(args[1]));
                    EnhancedSleepToggle.IsOn = _model.EnhancedSleepEnabled;
                    break;
                case "foreground-suspended":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Foregroudn Suspended {args[1]}");
                    _model.ForegroundGameSuspended = Convert.ToBoolean(args[1]);
                    break;
                case "foreground-tracked":
                    Trace.WriteLine($"[MainPage.xaml.cs] Updating UI Foreground Tracked {args[1]}");
                    _model.IsForegroundTracked = Convert.ToBoolean(args[1]);
                    break;
            }
        }

        private async void launchGameBarWidget()
        {
            var app = (App)Application.Current;
            var widgetControl = app._xboxGameBarWidgetControl;

            if (widgetControl != null)
            {
                Trace.WriteLine($"[MainPage.xaml.cs] widgetControl.ActivateAsync");
                await widgetControl.ActivateAsync("Suspended.XboxGameBarUI");
            }
        }

        private void Backend_OnConnectionChanged(object sender, bool isConnected)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => 
            {
                PanelSwitch(isConnected);
                if (isConnected)
                {
                    ConnectedInitialize();
                }
            });
        }

        private void Backend_OnClosedOrFailed(object _, EventArgs args)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => PanelSwitch(false));
        }

        private void LaunchBackendButton_OnClick(object sender, RoutedEventArgs e)
        {
            _ = Backend.LaunchBackend();
        }

        private void OnRefreshButtonClick(object sender, RoutedEventArgs e)
        {
            // EN: Manually request the game list from the backend
            // FR: Demande manuellement la liste des jeux au backend
            if (Backend.Instance.IsConnected)
            {
                Backend.Instance.Send("get-game-list");
                Trace.WriteLine("[MainPage.xaml.cs] Manual Refresh requested.");
            }
        }

        private async void OnRestartBackendButtonClick(object sender, RoutedEventArgs e)
        {
            // EN: Ask the backend to kill Game Bar processes and exit.
            // FR: Demande au backend de tuer les processus Game Bar et de quitter.
            try
            {
                Backend.Instance.Send("restart-service");
                
                // EN: Give it a moment to process before the widget itself might be killed by the system
                await System.Threading.Tasks.Task.Delay(500); 
                
                // EN: We can also try to hide the widget UI or wait for the system to kill us
                // but usually the backend killing GameBar.exe will terminate us.
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[RestartBackend] Error: {ex.Message}");
            }
        }

        private async void OnResumeButtonClick(object sender, RoutedEventArgs e)
        {
            // EN: Show a confirmation dialog before resuming the game
            // FR: Affiche une boîte de dialogue de confirmation avant de restaurer le jeu
            ContentDialog resumeDialog = new ContentDialog
            {
                Title = "Resume Game?",
                Content = "Do you want to resume the suspended game?",
                PrimaryButtonText = "Resume",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            // EN: We must set XamlRoot for ContentDialog in latest UWP/WinUI
            // FR: Il faut définir XamlRoot pour le ContentDialog
            resumeDialog.XamlRoot = this.Content.XamlRoot;

            ContentDialogResult result = await resumeDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                _model.ResumeActiveGame();
            }
        }

        private void OnSuspendButtonClick(object sender, RoutedEventArgs e)
        {
            _model.SuspendActiveGame();
        }

        private void AutoSuspedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // handle Auto Suspend toggle changes
            // handle Enhanced Sleep toggle changes
            if (sender is ToggleSwitch toggleSwitch)
            {
                _model.SetAutoSuspendEnabledVar(toggleSwitch.IsOn);
            }
        }

        private void AutoSuspedFocusToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggleSwitch)
            {
                _model.SetSuspendOnFocusLossVar(toggleSwitch.IsOn);
            }
        }


        private async void GamesListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GameInfo game)
            {
                if (game.IsSuspended)
                {
                    Backend.Instance.Send($"resume-game { Convert.ToInt32(game.ProcessId)}");
                }
                else
                {
                    Backend.Instance.Send($"suspend-game {Convert.ToInt32(game.ProcessId)}");
                }

            }
        }

        private void PowerButtonActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle Power Button Presses
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            {
                // Extract the Tag (0, 1, or 2)
                if (item.Tag is double tagValue)
                {
                    if (DataContext is MainPageModelWrapper model)
                    {
                        _model.SetPowerButtonActionVar(tagValue);
                    }
                }
            }
        }

        private void EnhancedSleepToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // handle Enhanced Sleep toggle changes
            if (sender is ToggleSwitch toggleSwitch)
            {
                _model.SetEnhancedSleepEnabledVar(toggleSwitch.IsOn);
            }
        }

        private void GoBackToSleepToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // handle Go back to sleep changes
            if (sender is ToggleSwitch toggleSwitch)
            {
                _model.SetGoBackToSleepEnabledVar(toggleSwitch.IsOn);
            }
        }

        private void GameIcon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (sender is Image img && img.Source is BitmapImage bmp)
            {
                Debug.WriteLine($"[GameIcon] Failed to load icon: {bmp.UriSource?.ToString()} - Error: {e.ErrorMessage}");
            }
        }
    }
}
