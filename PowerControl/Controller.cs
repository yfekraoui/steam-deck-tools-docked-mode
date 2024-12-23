﻿using CommonHelpers;
using ExternalHelpers;
using hidapi;
using Microsoft.Win32;
using PowerControl.External;
using PowerControl.Helpers;
using RTSSSharedMemoryNET;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace PowerControl
{
    internal class Controller : IDisposable
    {
        public const String Title = "Power Control";
        public static readonly String TitleWithVersion = Title + " v" + Application.ProductVersion.ToString();
        public const int KeyPressRepeatTime = 400;
        public const int KeyPressNextRepeatTime = 90;

        Container components = new Container();
        System.Windows.Forms.NotifyIcon notifyIcon;
        StartupManager startupManager = new StartupManager(Title);

        Menu.MenuRoot rootMenu = MenuStack.Root;
        OSD osd;
        System.Windows.Forms.Timer osdDismissTimer;
        bool isOSDToggled = false;

        int isExternalDisplayConnected = -1;

        hidapi.HidDevice neptuneDevice = new hidapi.HidDevice(0x28de, 0x1205, 64);
        SDCInput neptuneDeviceState = new SDCInput();
        DateTime? neptuneDeviceNextKey;
        System.Windows.Forms.Timer neptuneTimer;
        private System.Windows.Forms.Timer displayCheckTimer;

        ProfilesController? profilesController;

        SharedData<PowerControlSetting> sharedData = SharedData<PowerControlSetting>.CreateNew();

        static Controller()
        {
            //Dependencies.ValidateHidapi(TitleWithVersion);
            //Dependencies.ValidateRTSSSharedMemoryNET(TitleWithVersion);
        }

        public Controller()
        {
            Instance.OnUninstall(() =>
            {
                startupManager.Startup = false;
            });

            Log.CleanupLogFiles(DateTime.UtcNow.AddDays(-7));
            Log.LogToFile = true;
            Log.LogToFileDebug = true;

            Instance.RunOnce(TitleWithVersion, "Global\\PowerControl");
            Instance.RunUpdater(TitleWithVersion);

            if (Instance.WantsRunOnStartup)
                startupManager.Startup = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip(components);

            var notRunningRTSSItem = contextMenu.Items.Add("&RTSS is not running");
            notRunningRTSSItem.Enabled = false;
            contextMenu.Opening += delegate { notRunningRTSSItem.Visible = Dependencies.EnsureRTSS(null) && !OSDHelpers.IsLoaded; };

            rootMenu.Init();
            rootMenu.Visible = false;
            rootMenu.Update();
            rootMenu.CreateMenu(contextMenu);
            rootMenu.VisibleChanged += delegate { updateOSD(); };
            contextMenu.Items.Add(new ToolStripSeparator());

            if (Settings.Default.EnableExperimentalFeatures)
            {
                var installEDIDItem = contextMenu.Items.Add("Install &Resolutions");
                installEDIDItem.Click += delegate { Helpers.AMD.EDID.SetEDID(Resources.CRU_SteamDeck); };
                var replaceEDIDItem = contextMenu.Items.Add("Replace &Resolutions");
                replaceEDIDItem.Click += delegate { Helpers.AMD.EDID.SetEDID(new byte[0]); Helpers.AMD.EDID.SetEDID(Resources.CRU_SteamDeck); };
                var uninstallEDIDItem = contextMenu.Items.Add("Revert &Resolutions");
                uninstallEDIDItem.Click += delegate { Helpers.AMD.EDID.SetEDID(new byte[0]); };
                contextMenu.Opening += delegate
                {
                    if (ExternalHelpers.DisplayConfig.IsInternalConnected == true)
                    {
                        var edid = Helpers.AMD.EDID.GetEDID() ?? new byte[0];
                        var edidInstalled = Resources.CRU_SteamDeck.SequenceEqual(edid);
                        installEDIDItem.Visible = edid.Length <= 128;
                        replaceEDIDItem.Visible = !edidInstalled && edid.Length > 128;
                        uninstallEDIDItem.Visible = edid.Length > 128;
                    }
                    else
                    {
                        installEDIDItem.Visible = false;
                        replaceEDIDItem.Visible = false;
                        uninstallEDIDItem.Visible = false;
                    }
                };
                contextMenu.Items.Add(new ToolStripSeparator());
            }

            if (startupManager.IsAvailable)
            {
                var startupItem = new ToolStripMenuItem("Run On Startup");
                startupItem.Checked = startupManager.Startup;
                startupItem.Click += delegate
                {
                    startupManager.Startup = !startupManager.Startup;
                    startupItem.Checked = startupManager.Startup;
                };
                contextMenu.Items.Add(startupItem);
            }

            var missingRTSSItem = contextMenu.Items.Add("&Install missing RTSS");
            missingRTSSItem.Click += delegate { Dependencies.OpenLink(Dependencies.RTSSURL); };
            contextMenu.Opening += delegate { missingRTSSItem.Visible = !Dependencies.EnsureRTSS(null); };

            var showGameProfilesItem = contextMenu.Items.Add("Show Game &Profiles");
            showGameProfilesItem.Click += delegate { Dependencies.OpenLink(Helper.ProfileSettings.UserProfilesPath); };
            contextMenu.Items.Add(new ToolStripSeparator());

            var checkForUpdatesItem = contextMenu.Items.Add("&Check for Updates");
            checkForUpdatesItem.Click += delegate { Instance.RunUpdater(TitleWithVersion, true); };

            var helpItem = contextMenu.Items.Add("&Help");
            helpItem.Click += delegate { Dependencies.OpenLink(Dependencies.SDTURL); };
            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = contextMenu.Items.Add("&Exit");
            exitItem.Click += ExitItem_Click;

            notifyIcon = new System.Windows.Forms.NotifyIcon(components);
            notifyIcon.Icon = WindowsDarkMode.IsDarkModeEnabled ? Resources.traffic_light_outline_light : Resources.traffic_light_outline;
            notifyIcon.Text = TitleWithVersion;
            notifyIcon.Visible = true;
            notifyIcon.ContextMenuStrip = contextMenu;

            // Fix first time context menu position
            contextMenu.Show();
            contextMenu.Close();

            osdDismissTimer = new System.Windows.Forms.Timer(components);
            osdDismissTimer.Interval = 3000;
            osdDismissTimer.Tick += delegate (object? sender, EventArgs e)
            {
                if (!isOSDToggled)
                {
                    hideOSD();
                }
            };

            var osdTimer = new System.Windows.Forms.Timer(components);
            osdTimer.Tick += OsdTimer_Tick;
            osdTimer.Interval = 250;
            osdTimer.Enabled = true;

            displayCheckTimer = new System.Windows.Forms.Timer();
            displayCheckTimer.Interval = 1000;
            displayCheckTimer.Tick += DisplayCheckTimer_Tick;
            displayCheckTimer.Start();

            profilesController = new ProfilesController();

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuUpKey, () =>
            {
                if (!OSDHelpers.IsOSDForeground())
                    return;
                rootMenu.Next(-1);
                setDismissTimer();
                dismissNeptuneInput();
            }, true);

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuDownKey, () =>
            {
                if (!OSDHelpers.IsOSDForeground())
                    return;
                rootMenu.Next(1);
                setDismissTimer();
                dismissNeptuneInput();
            }, true);

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuLeftKey, () =>
            {
                if (!OSDHelpers.IsOSDForeground())
                    return;
                rootMenu.SelectNext(-1);
                setDismissTimer();
                dismissNeptuneInput();
            });

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuRightKey, () =>
            {
                if (!OSDHelpers.IsOSDForeground())
                    return;
                rootMenu.SelectNext(1);
                setDismissTimer();
                dismissNeptuneInput();
            });

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuToggle, () =>
            {
                isOSDToggled = !rootMenu.Visible;

                if (!OSDHelpers.IsOSDForeground())
                    return;

                if (isOSDToggled)
                {
                    showOSD();
                }
                else
                {
                    hideOSD();
                }
            }, true);

            if (Settings.Default.EnableNeptuneController)
            {
                neptuneTimer = new System.Windows.Forms.Timer(components);
                neptuneTimer.Interval = 1000 / 60;
                neptuneTimer.Tick += NeptuneTimer_Tick;
                neptuneTimer.Enabled = true;

                neptuneDevice.OnInputReceived += NeptuneDevice_OnInputReceived;
                neptuneDevice.OpenDevice();
                neptuneDevice.BeginRead();
            }

            if (Settings.Default.EnableVolumeControls)
            {
                GlobalHotKey.RegisterHotKey("VolumeUp", () =>
                {
                    if (neptuneDeviceState.buttons5.HasFlag(SDCButton5.BTN_QUICK_ACCESS))
                        rootMenu.Select("Brightness");
                    else
                        rootMenu.Select("Volume");
                    rootMenu.SelectNext(1);
                    setDismissTimer();
                    dismissNeptuneInput();
                });

                GlobalHotKey.RegisterHotKey("VolumeDown", () =>
                {
                    if (neptuneDeviceState.buttons5.HasFlag(SDCButton5.BTN_QUICK_ACCESS))
                        rootMenu.Select("Brightness");
                    else
                        rootMenu.Select("Volume");
                    rootMenu.SelectNext(-1);
                    setDismissTimer();
                    dismissNeptuneInput();
                });
            }

            //Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(OnNetworkAddressChanged);
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {

            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    break;

                case PowerModes.Resume:
                    break;
            }
        }

        private void DisplayCheckTimer_Tick(object? sender, EventArgs e)
        {
            bool currentExternalDisplayState = ExternalHelpers.DisplayConfig.IsExternalConnected.GetValueOrDefault(false);
            int newState = currentExternalDisplayState ? 1 : 0;

            if (newState != isExternalDisplayConnected)
            {
                isExternalDisplayConnected = newState;
                SystemEvents_DisplaySettingsChanged(this, EventArgs.Empty); // Déclenche manuellement l'événement
            }
        }

        private void OsdTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                notifyIcon.Text = TitleWithVersion + ". RTSS Version: " + OSD.Version;
                notifyIcon.Icon = WindowsDarkMode.IsDarkModeEnabled ? Resources.traffic_light_outline_light : Resources.traffic_light_outline;
            }
            catch
            {
                notifyIcon.Text = TitleWithVersion + ". RTSS Not Available.";
                notifyIcon.Icon = Resources.traffic_light_outline_red;
            }

            var watchedProfiles = profilesController?.WatchedProfiles ?? new string[0];
            if (watchedProfiles.Any())
                notifyIcon.Text += ". Profile: " + string.Join(", ", watchedProfiles);

            updateOSD();
        }

        private Task NeptuneDevice_OnInputReceived(hidapi.HidDeviceInputReceivedEventArgs e)
        {
            var input = SDCInput.FromBuffer(e.Buffer);

            var filteredInput = new SDCInput()
            {
                buttons0 = input.buttons0,
                buttons1 = input.buttons1,
                buttons2 = input.buttons2,
                buttons3 = input.buttons3,
                buttons4 = input.buttons4,
                buttons5 = input.buttons5
            };

            if (!neptuneDeviceState.Equals(filteredInput))
            {
                neptuneDeviceState = filteredInput;
                neptuneDeviceNextKey = null;
            }

            // Consume only some events to avoid under-running SWICD
            if (!neptuneDeviceState.buttons5.HasFlag(SDCButton5.BTN_QUICK_ACCESS))
                Thread.Sleep(50);

            return new Task(() => { });
        }

        private void dismissNeptuneInput()
        {
            neptuneDeviceNextKey = DateTime.UtcNow.AddDays(1);
        }

        private void NeptuneTimer_Tick(object? sender, EventArgs e)
        {
            var input = neptuneDeviceState;

            if (neptuneDeviceNextKey == null)
                neptuneDeviceNextKey = DateTime.UtcNow.AddMilliseconds(KeyPressRepeatTime);
            else if (neptuneDeviceNextKey < DateTime.UtcNow)
                neptuneDeviceNextKey = DateTime.UtcNow.AddMilliseconds(KeyPressNextRepeatTime);
            else
                return; // otherwise it did not yet trigger

            // Reset sequence: 3 dots + L4|R4|L5|R5
            if (input.buttons0 == SDCButton0.BTN_L5 &&
                input.buttons1 == SDCButton1.BTN_R5 &&
                input.buttons2 == 0 &&
                input.buttons3 == 0 &&
                input.buttons4 == (SDCButton4.BTN_L4 | SDCButton4.BTN_R4) &&
                input.buttons5 == SDCButton5.BTN_QUICK_ACCESS)
            {
                dismissNeptuneInput();
                rootMenu.Show();
                rootMenu.Reset();
                notifyIcon.ShowBalloonTip(3000, TitleWithVersion, "Settings were reset to default.", ToolTipIcon.Info);
                return;
            }

            // Display reset sequence
            if (input.buttons0 == (SDCButton0.BTN_L1 | SDCButton0.BTN_R1) &&
                input.buttons1 == 0 &&
                input.buttons2 == 0 &&
                input.buttons3 == 0 &&
                input.buttons4 == 0 &&
                input.buttons5 == SDCButton5.BTN_QUICK_ACCESS)
            {
                dismissNeptuneInput();
                DisplayResolutionController.ResetCurrentResolution();
                notifyIcon.ShowBalloonTip(3000, TitleWithVersion, "Resolution was reset.", ToolTipIcon.Info);
                return;
            }

            if (!neptuneDeviceState.buttons5.HasFlag(SDCButton5.BTN_QUICK_ACCESS) || !OSDHelpers.IsOSDForeground())
            {
                // schedule next repeat far in the future
                dismissNeptuneInput();
                hideOSD();
                return;
            }

            rootMenu.Show();
            setDismissTimer(false);

            if (input.buttons1 != 0 || input.buttons2 != 0 || input.buttons3 != 0 || input.buttons4 != 0)
            {
                return;
            }
            else if (input.buttons0 == SDCButton0.BTN_DPAD_LEFT)
            {
                rootMenu.SelectNext(-1);
            }
            else if (input.buttons0 == SDCButton0.BTN_DPAD_RIGHT)
            {
                rootMenu.SelectNext(1);
            }
            else if (input.buttons0 == SDCButton0.BTN_DPAD_UP)
            {
                rootMenu.Next(-1);
            }
            else if (input.buttons0 == SDCButton0.BTN_DPAD_DOWN)
            {
                rootMenu.Next(1);
            }
        }

        private void setDismissTimer(bool enabled = true)
        {
            osdDismissTimer.Stop();
            if (enabled)
                osdDismissTimer.Start();
        }

        private void hideOSD()
        {
            if (!rootMenu.Visible)
                return;

            Trace.WriteLine("Hide OSD");
            rootMenu.Visible = false;
            osdDismissTimer.Stop();
            updateOSD();
        }

        private void showOSD()
        {
            if (rootMenu.Visible)
                return;

            Trace.WriteLine("Show OSD");
            rootMenu.Update();
            rootMenu.Visible = true;
            updateOSD();
        }

        public void updateOSD()
        {
            sharedData.SetValue(new PowerControlSetting()
            {
                Current = rootMenu.Visible ? PowerControlVisible.Yes : PowerControlVisible.No
            });

            if (!rootMenu.Visible)
            {
                osdClose();
                return;
            }

            try
            {
                // recreate OSD if index 0
                if (OSDHelpers.OSDIndex("Power Control") == 0 && OSD.GetOSDCount() > 1)
                    osdClose();
                if (osd == null)
                {
                    osd = new OSD("Power Control");
                    Trace.WriteLine("Show OSD");
                }
                osd.Update(rootMenu.Render(null));
            }
            catch (SystemException)
            {
            }
        }

        private void ExitItem_Click(object? sender, EventArgs e)
        {
            try
            {
                CleanupResources(); // Nettoyage des ressources
                Environment.Exit(0); // Forcer l'arrêt du processus
            }
            catch (Exception ex)
            {
                Log.TraceLine($"Error during exit: {ex.Message}");
            }
        }

        public void Dispose()
        {
            using (profilesController) { }
            CleanupResources();
            components.Dispose();
            osdClose();
        }

        private void osdClose()
        {
            try
            {
                if (osd != null)
                {
                    osd.Dispose();
                    Trace.WriteLine("Close OSD");
                }
                osd = null;
            }
            catch (SystemException)
            {
            }
        }

        private bool IsInternetAvailableViaEthernet()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Vérifiez si l'interface est de type Ethernet et active
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet && ni.OperationalStatus == OperationalStatus.Up)
                {
                    // Récupérez les propriétés IP de l'interface
                    IPInterfaceProperties ipProperties = ni.GetIPProperties();

                    // Vérifiez s'il y a une passerelle par défaut pour cette interface
                    if (ipProperties.GatewayAddresses.Count > 0)
                    {
                        foreach (UnicastIPAddressInformation ipInfo in ipProperties.UnicastAddresses)
                        {
                            // Vérifiez que l'adresse IP est IPv4 (excluez IPv6, si nécessaire)
                            if (ipInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                try
                                {
                                    using (Ping ping = new Ping())
                                    {
                                        PingOptions options = new PingOptions { DontFragment = true };
                                        byte[] buffer = new byte[32];
                                        PingReply reply = ping.Send("8.8.8.8", 3000, buffer, options);

                                        if (reply != null && reply.Status == IPStatus.Success)
                                        {
                                            // Vérifiez si l'adresse IP source du ping correspond à l'interface Ethernet
                                            if (reply.Address.Equals(ipInfo.Address))
                                            {
                                                Log.TraceLine($"Internet is available via Ethernet interface: {ni.Name}");
                                                return true; // Internet disponible via Ethernet
                                            }
                                        }
                                    }
                                }
                                catch (PingException ex)
                                {
                                    Log.TraceLine($"Ping failed for Ethernet interface {ni.Name}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            return false; // Pas d'accès Internet via Ethernet
        }



        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            Log.TraceLine("SystemEvents_DisplaySettingsChanged: External display state changed to {0}", isExternalDisplayConnected);

            if (isExternalDisplayConnected == 1)
            {
                Log.TraceLine("External display connected!");
                System.Diagnostics.Process.Start(@"C:\SteamDeck32\DisplaySwitch.exe", "/external");
                System.Diagnostics.Process.Start(@"C:\Windows\System32\pnputil.exe", "/scan-devices");
                System.Diagnostics.Process.Start("radiocontrol.exe", "Bluetooth on");
            }
            else if (isExternalDisplayConnected == 0)
            {
                Log.TraceLine("External display disconnected!");
                System.Diagnostics.Process.Start(@"C:\SteamDeck32\DisplaySwitch.exe", "/internal");
                System.Diagnostics.Process.Start("radiocontrol.exe", "Bluetooth off");
            }
            profilesController.ApplyAutostartProfile();
            
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                rootMenu.Update();
            }));
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            if (!IsEthernetCableConnected()) {
                return;
            }
            Log.TraceLine("Network address changed event triggered.");

            // Vérifie si un câble Ethernet est branché et s'il y a Internet via Ethernet
            Thread.Sleep(3000);
            bool isInternetAvailableViaEthernet = IsInternetAvailableViaEthernet();
            Log.TraceLine("Internet available via Ethernet: " + isInternetAvailableViaEthernet);

            // Vérifie l'état actuel du Wi-Fi
            bool isWiFiEnabled = IsWiFiEnabled();
            Log.TraceLine("Current Wi-Fi state: " + (isWiFiEnabled ? "Enabled" : "Disabled"));

            // Gestion du Wi-Fi en fonction de l'état Ethernet et de son état actuel
            if (isInternetAvailableViaEthernet && isWiFiEnabled)
            {
                Log.TraceLine("Disabling Wi-Fi as Ethernet is active with Internet access.");
                System.Diagnostics.Process.Start("radiocontrol.exe", "Wi-Fi off");
            }
            else if (!isInternetAvailableViaEthernet && !isWiFiEnabled)
            {
                Log.TraceLine("Enabling Wi-Fi as Ethernet is not active or lacks Internet access.");
                System.Diagnostics.Process.Start("radiocontrol.exe", "Wi-Fi on");
            }
        }

        private bool IsEthernetCableConnected()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Vérifiez si l'interface est de type Ethernet
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    // Vérifiez si le câble Ethernet est branché
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        return true; // Câble Ethernet connecté
                    }
                }
            }
            return false; // Aucun câble Ethernet détecté
        }

        private bool IsWiFiEnabled()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    ni.OperationalStatus == OperationalStatus.Up)
                {
                    return true; // Wi-Fi activé
                }
            }
            return false; // Wi-Fi désactivé
        }
    


        private void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            try
            {
                notifyIcon.BalloonTipTitle = title;
                notifyIcon.BalloonTipText = message;
                notifyIcon.BalloonTipIcon = icon;
                notifyIcon.ShowBalloonTip(3000); // La notification sera affichée pendant 3 secondes
            }
            catch (Exception ex)
            {
                Log.TraceLine("Failed to show notification: " + ex.Message);
            }
        }

        private void ExecuteCommand(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Log.TraceLine("Command failed with error: " + error);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.TraceLine("An error occurred while executing the command: " + ex.Message);
            }
        }

        private void CleanupResources()
        {
            try
            {
                // Arrêtez et disposez tous les timers
                displayCheckTimer?.Stop();
                displayCheckTimer?.Dispose();

                osdDismissTimer?.Stop();
                osdDismissTimer?.Dispose();

                if (neptuneTimer != null)
                {
                    neptuneTimer.Stop();
                    neptuneTimer.Dispose();
                }

                // Libérez le dispositif HID si utilisé
                neptuneDevice?.Dispose();

                // Libérez d'autres ressources comme les profils
                profilesController?.Dispose();

                // Nettoyez l'icône de la barre des tâches
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            catch (Exception ex)
            {
                Log.TraceLine($"Error during cleanup: {ex.Message}");
            }
        }
    }
}
