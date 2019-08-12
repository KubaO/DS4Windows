using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Net;
using System.Drawing;
using System.Diagnostics;
using System.Xml;
using System.Text;
using Microsoft.Win32.TaskScheduler;
using System.Security.Principal;
using System.Threading;
using System.Drawing.Drawing2D;
using System.Linq;
using TaskRunner = System.Threading.Tasks.Task;
using NonFormTimer = System.Timers.Timer;
using static DS4Windows.Global;
using System.Security;
using System.Management;
using System.Runtime.CompilerServices;

namespace DS4Windows.Forms
{
    [SuppressUnmanagedCodeSecurity]
    public partial class DS4Form : Form
    {
        public string[] cmdArguments;
        delegate void LogDebugDelegate(DateTime Time, String Data, bool warning);
        delegate void NotificationDelegate(object sender, DebugEventArgs args);
        delegate void DeviceStatusChangedDelegate(object sender, DeviceStatusChangeEventArgs args);
        delegate void DeviceSerialChangedDelegate(object sender, SerialChangeArgs args);
        private Label[] Pads, Batteries;
        private ComboBox[] cbs;
        private Button[] ebns;
        private Button[] lights;
        private PictureBox[] statPB;
        private ToolStripMenuItem[] shortcuts;
        private ToolStripMenuItem[] disconnectShortcuts;
        protected CheckBox[] linkedProfileCB;
        NonFormTimer hotkeysTimer = null;// new NonFormTimer();
        NonFormTimer autoProfilesTimer = null;// new NonFormTimer();
        double dpix, dpiy;

        List<ProgramPathItem> programpaths = new List<ProgramPathItem>();
        List<string> profilenames = new List<string>();
        List<string>[] proprofiles;
        List<bool> turnOffTempProfiles;
        ProgramPathItem tempProfileProgram = null;

        public static int autoProfileDebugLogLevel = 0;  // 0=Dont log debug messages about active process and window titles to GUI Log screen. 1=Show debug log messages 
        private static IntPtr prevForegroundWnd = IntPtr.Zero;
        private static uint   prevForegroundProcessID = 0;
        private static string prevForegroundWndTitleName = string.Empty;
        private static string prevForegroundProcessName = string.Empty;
        private static StringBuilder autoProfileCheckTextBuilder = null;

        private IGlobalConfig Config = API.Config;
        private bool systemShutdown = false;
        private bool wasrunning = false;
        Options opt;
        private bool optPop;
        public Size oldsize;
        bool contextclose;
        bool turnOffTemp;
        bool runningBat;
        private bool changingService;
        private IntPtr regHandle = new IntPtr();
        private ManagementEventWatcher managementEvWatcher;
        private DS4Forms.LanguagePackComboBox languagePackComboBox1;
        private AdvancedColorDialog advColorDialog;
        Dictionary<Control, string> hoverTextDict = new Dictionary<Control, string>();       
        // 0 index is used for application version text. 1 - 4 indices are used for controller status
        string[] notifyText = new string[5]
            { "DS4Windows v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion,
            string.Empty, string.Empty, string.Empty, string.Empty };

        private const string UPDATER_VERSION = "1.3.1";
        private const int WM_QUERYENDSESSION = 0x11;
        private const int WM_CLOSE = 0x10;
        public  const int WM_COPYDATA = 0x004A;

        private readonly string DS4_UPDATER_FN = $"{API.ExePath}\\DS4Updater.exe";
        private readonly string VERSION_FN = $"{API.AppDataPath}\\version.txt";


        internal string updaterExe = Environment.Is64BitProcess ? "DS4Updater.exe" : "DS4Updater_x86.exe";

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("psapi.dll")]
        private static extern uint GetModuleFileNameEx(IntPtr hWnd, IntPtr hModule, StringBuilder lpFileName, int nSize);

        [DllImport("user32.dll", CharSet= CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nSize);

        public DS4Form(string[] args)
        {
            API.FindConfigLocation();

            if (API.IsFirstRun)
            {
                new SaveWhere(API.MultiSaveSpots).ShowDialog();
            }

            Config.Load();
            API.SetCulture(Config.UseLang);

            InitializeComponent();
            advColorDialog = new AdvancedColorDialog();

            languagePackComboBox1 = new DS4Forms.LanguagePackComboBox {
                AutoValidate = System.Windows.Forms.AutoValidate.Disable,
                BackColor = System.Drawing.SystemColors.Window,
                CausesValidation = false,
                Name = "languagePackComboBox1"
            };
            languagePackComboBox1.SelectedValueChanged += languagePackComboBox1_SelectedValueChanged;
            langPanel.Controls.Add(languagePackComboBox1);

            bnEditC1.Tag = 0;
            bnEditC2.Tag = 1;
            bnEditC3.Tag = 2;
            bnEditC4.Tag = 3;

            StartWindowsCheckBox.CheckedChanged -= StartWindowsCheckBox_CheckedChanged;

            saveProfiles.Filter = Properties.Resources.XMLFiles + "|*.xml";
            openProfiles.Filter = Properties.Resources.XMLFiles + "|*.xml";
            cmdArguments = args;

            Pads = new Label[4] { lbPad1, lbPad2, lbPad3, lbPad4 };
            Batteries = new Label[4] { lbBatt1, lbBatt2, lbBatt3, lbBatt4 };
            cbs = new ComboBox[4] { cBController1, cBController2, cBController3, cBController4 };
            ebns = new Button[4] { bnEditC1, bnEditC2, bnEditC3, bnEditC4 };
            lights = new Button[4] { bnLight1, bnLight2, bnLight3, bnLight4 };
            statPB = new PictureBox[4] { pBStatus1, pBStatus2, pBStatus3, pBStatus4 };
            shortcuts = new ToolStripMenuItem[4] { (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[0],
                (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[1],
                (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[2],
                (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[3] };
            disconnectShortcuts = new ToolStripMenuItem[4]
            {
                discon1toolStripMenuItem, discon2ToolStripMenuItem,
                discon3ToolStripMenuItem, discon4ToolStripMenuItem
            };

            linkedProfileCB = new CheckBox[4] { linkCB1, linkCB2, linkCB3, linkCB4 };

            WqlEventQuery q = new WqlEventQuery();
            ManagementScope scope = new ManagementScope("root\\CIMV2");
            q.EventClassName = "Win32_PowerManagementEvent";
            managementEvWatcher = new ManagementEventWatcher(scope, q);
            managementEvWatcher.EventArrived += PowerEventArrive;
            managementEvWatcher.Start();

            tSOptions.Visible = false;

            TaskRunner.Run(() => CheckDrivers());

            if (string.IsNullOrEmpty(API.AppDataPath))
            {
                Close();
                return;
            }

            Graphics g = CreateGraphics();
            try
            {
                dpix = g.DpiX / 100f * 1.041666666667f;
                dpiy = g.DpiY / 100f * 1.041666666667f;
            }
            finally
            {
                g.Dispose();
            }

            blankControllerTab();

            Directory.CreateDirectory(API.AppDataPath);
            if (!API.Config.Save()) //if can't write to file
            {
                if (MessageBox.Show("Cannot write at current location\nCopy Settings to appdata?", "DS4Windows",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(API.AppDataPath);
                        File.Copy(API.ProfileExePath, API.ProfileDataPath);
                        File.Copy(API.AutoProfileExePath, API.AutoProfileDataPath);
                        Directory.CreateDirectory($"{API.AppDataPath}\\Profiles");
                        foreach (string s in Directory.GetFiles($"{API.ExePath}\\Profiles"))
                        {
                            File.Copy(s, $"{API.AppDataPath}\\Profiles\\{Path.GetFileName(s)}");
                        }
                    }
                    catch { }
                    MessageBox.Show("Copy complete, please relaunch DS4Windows and remove settings from Program Directory", "DS4Windows");
                }
                else
                {
                    MessageBox.Show("DS4Windows cannot edit settings here, This will now close", "DS4Windows");
                }

#if false
                // FIXME: it was being set to null. Why?!
                API.AppDataPath = null;
#endif
                Close();
                return;
            }

            cBUseWhiteIcon.Checked = Config.UseWhiteIcon;
            Icon = Properties.Resources.DS4W;
            notifyIcon1.Icon = Config.UseWhiteIcon ? Properties.Resources.DS4W___White : Properties.Resources.DS4W;
            populateNotifyText();
            foreach (ToolStripMenuItem t in shortcuts)
                t.DropDownItemClicked += Profile_Changed_Menu;

            hideDS4CheckBox.CheckedChanged -= hideDS4CheckBox_CheckedChanged;
            hideDS4CheckBox.Checked = Config.UseExclusiveMode;
            hideDS4CheckBox.CheckedChanged += hideDS4CheckBox_CheckedChanged;

            cBDisconnectBT.Checked = Config.DisconnectBTAtStop;
            cBQuickCharge.Checked = Config.QuickCharge;
            cBCustomSteam.Checked = Config.UseCustomSteamFolder;
            tBSteamFolder.Text = Config.CustomSteamFolder;
            // New settings
            Size = Config.FormSize;
            Location = Config.FormLocation;
            startMinimizedCheckBox.CheckedChanged -= startMinimizedCheckBox_CheckedChanged;
            startMinimizedCheckBox.Checked = Config.StartMinimized;
            startMinimizedCheckBox.CheckedChanged += startMinimizedCheckBox_CheckedChanged;

            mintoTaskCheckBox.Checked = Config.MinToTaskbar;
            mintoTaskCheckBox.CheckedChanged += MintoTaskCheckBox_CheckedChanged;

            cBCloseMini.Checked = Config.CloseMinimizes;

            cBFlashWhenLate.Checked = Config.FlashWhenLate;
            nUDLatency.Value = Config.FlashWhenLateAt;

            if (!Config.LoadActions()) //if first no actions have been made yet, create PS+Option to D/C and save it to every profile
            {
                Config.CreateStdActions();
            }

            bool start = true;
            bool mini = false;
            for (int i = 0, argslen = cmdArguments.Length; i < argslen; i++)
            {
                if (cmdArguments[i] == "-stop")
                    start = false;
                else if (cmdArguments[i] == "-m")
                    mini = true;

                if (mini && start)
                    break;
            }

            if (startMinimizedCheckBox.Checked || mini)
            {
                WindowState = FormWindowState.Minimized;
            }

            RefreshProfiles();
            /*opt = new Options(this);
            opt.Icon = this.Icon;
            opt.TopLevel = false;
            opt.Dock = DockStyle.None;
            opt.FormBorderStyle = FormBorderStyle.None;
            */
            //tabProfiles.Controls.Add(opt);

            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            string version = fvi.FileVersion;
            LogDebug(DateTime.Now, "DS4Windows version " + version, false);

            Global.BatteryStatusChange += BatteryStatusUpdate;
            Global.ControllerRemoved += ControllerRemovedChange;
            Global.DeviceStatusChange += DeviceStatusChanged;
            Global.DeviceSerialChange += DeviceSerialChanged;

            Enable_Controls(0, false);
            Enable_Controls(1, false);
            Enable_Controls(2, false);
            Enable_Controls(3, false);
            btnStartStop.Text = Properties.Resources.StartText;

            startToolStripMenuItem.Text = btnStartStop.Text;
            cBoxNotifications.SelectedIndex = Config.Notifications;
            //cBSwipeProfiles.Checked = SwipeProfiles;
            int checkwhen = Config.CheckWhen;
            cBUpdate.Checked = checkwhen > 0;
            if (checkwhen > 23)
            {
                cBUpdateTime.SelectedIndex = 1;
                nUDUpdateTime.Value = checkwhen / 24;
            }
            else
            {
                cBUpdateTime.SelectedIndex = 0;
                nUDUpdateTime.Value = checkwhen;
            }

            if (File.Exists($"{API.ExePath}\\Updater.exe")) {
                TaskRunner.Run(async delegate {
                    await TaskRunner.Delay(2000);
                    File.Delete($"{API.ExePath}\\Updater.exe");
                });
            }

            bool isElevated = API.IsAdministrator;
            if (!isElevated)
            {
                Image tempImg = new Bitmap(uacPictureBox.Width, uacPictureBox.Height);
                AddUACShieldToImage(tempImg);
                uacPictureBox.BackgroundImage = tempImg;
                uacPictureBox.Visible = true;
                new ToolTip().SetToolTip(uacPictureBox, Properties.Resources.UACTask);
                runStartTaskRadio.Enabled = false;
            }
            else
            {
                runStartTaskRadio.Enabled = true;
            }

            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk"))
            {
                StartWindowsCheckBox.Checked = true;
                runStartupPanel.Visible = true;

                string lnkpath = WinProgs.ResolveShortcutAndArgument(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                string onlylnkpath = WinProgs.ResolveShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                if (!lnkpath.EndsWith("-runtask"))
                {
                    runStartProgRadio.Checked = true;
                }
                else
                {
                    runStartTaskRadio.Checked = true;
                }

                if (onlylnkpath != Process.GetCurrentProcess().MainModule.FileName)
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                    appShortcutToStartup();
                    changeStartupRoutine();
                }
            }

            StartWindowsCheckBox.CheckedChanged += new EventHandler(StartWindowsCheckBox_CheckedChanged);
            new ToolTip().SetToolTip(StartWindowsCheckBox, Properties.Resources.RunAtStartup);

            ckUdpServ.Checked = nUDUdpPortNum.Enabled = tBUdpListenAddress.Enabled = Config.UseUDPServer;
            nUDUdpPortNum.Value = Config.UDPServerPort;
            tBUdpListenAddress.Text = Config.UDPServerListenAddress;
            new ToolTip().SetToolTip(ckUdpServ, Properties.Resources.UdpServer);

            ckUdpServ.CheckedChanged += CkUdpServ_CheckedChanged;
            nUDUdpPortNum.Leave += NUDUdpPortNum_Leave;
            tBUdpListenAddress.Leave += TBUdpListenAddress_Leave;
            
            populateHoverTextDict();

            cBController1.KeyPress += CBController_KeyPress;
            cBController2.KeyPress += CBController_KeyPress;
            cBController3.KeyPress += CBController_KeyPress;
            cBController4.KeyPress += CBController_KeyPress;

            foreach (Control control in fLPSettings.Controls)
            {
                string tempst;
                if (control.HasChildren)
                {
                    foreach (Control ctrl in control.Controls)
                    {
                        if (hoverTextDict.TryGetValue(ctrl, out tempst))
                        {
                            ctrl.MouseHover += Items_MouseHover;
                        }
                        else
                        {
                            ctrl.MouseHover += ClearLastMessage;
                        }
                    }
                }
                else
                {
                    if (hoverTextDict.TryGetValue(control, out tempst))
                        control.MouseHover += Items_MouseHover;
                    else
                        control.MouseHover += ClearLastMessage;
                }
            }

            this.Resize += Form_Resize;
            this.LocationChanged += TrackLocationChanged;
            if (!(Config.StartMinimized || mini))
                Form_Resize(null, null);

            Program.CreateIPCClassNameMMF(this.Handle);

            Program.rootHub.Debug += On_Debug;

            AppLogger.GuiLog += On_Debug;
            AppLogger.TrayIconLog += ShowNotification;
            Config.LoadLinkedProfiles();

            TaskRunner.Delay(50).ContinueWith((t) =>
            {
                if (checkwhen > 0 && DateTime.Now >= Config.LastChecked + TimeSpan.FromHours(checkwhen))
                {
                    this.BeginInvoke((System.Action)(() =>
                    {
                        // Sorry other devs, gonna have to find your own server
                        Uri url = new Uri("https://raw.githubusercontent.com/Ryochan7/DS4Windows/jay/DS4Windows/newest.txt");
                        WebClient wc = new WebClient();
                        wc.DownloadFileAsync(url, $"{API.AppDataPath}\\version.txt");
                        wc.DownloadFileCompleted += (sender, e) => { TaskRunner.Run(() => Check_Version(sender, e)); };
                        Config.LastChecked = DateTime.Now;
                    }));
                }

                UpdateTheUpdater();
            });

            if (btnStartStop.Enabled && start)
            {
                TaskRunner.Delay(100).ContinueWith((t) => {
                    this.BeginInvoke((System.Action)(() => BtnStartStop_Clicked()));
                });
            }

            Thread timerThread = new Thread(() =>
            {
                hotkeysTimer = new NonFormTimer() {AutoReset = false};
                //hotkeysTimer.Elapsed += Hotkeys;
                if (Config.SwipeProfiles)
                {
                    ChangeHotkeysStatus(true);
                    //hotkeysTimer.Start();
                }

                autoProfilesTimer = new NonFormTimer {Interval = 1000, AutoReset = false};
                //autoProfilesTimer.Elapsed += CheckAutoProfiles;

                LoadP();

                this.BeginInvoke((System.Action)(() =>
                {
                    cBSwipeProfiles.Checked = Config.SwipeProfiles;
                }));
            }) { IsBackground = true, Priority = ThreadPriority.Lowest };
            timerThread.Start();
        }

        private void TBUdpListenAddress_Leave(object sender, EventArgs e)
        {
            API.Config.UDPServerListenAddress = tBUdpListenAddress.Text.Trim();
        }

        private void populateHoverTextDict()
        {
            hoverTextDict.Clear();
            hoverTextDict[cBSwipeProfiles] = Properties.Resources.TwoFingerSwipe;
            hoverTextDict[cBQuickCharge] = Properties.Resources.QuickCharge;
            hoverTextDict[cBCloseMini] = Properties.Resources.CloseMinimize;
            hoverTextDict[uacPictureBox] = Properties.Resources.UACTask;
            hoverTextDict[StartWindowsCheckBox] = Properties.Resources.RunAtStartup;
        }

        private void AddUACShieldToImage(Image image)
        {
            Bitmap shield = SystemIcons.Shield.ToBitmap();
            shield.MakeTransparent();

            Graphics g = Graphics.FromImage(image);
            g.CompositingMode = CompositingMode.SourceOver;
            double aspectRatio = shield.Width / (double)shield.Height;
            int finalWidth = Convert.ToInt32(image.Height * aspectRatio);
            int finalHeight = Convert.ToInt32(image.Width / aspectRatio);
            g.DrawImage(shield, new Rectangle(0, 0, finalWidth, finalHeight));
        }

        private void ClearLastMessage(object sender, EventArgs e)
        {
            lbLastMessage.Text = "";
            lbLastMessage.ForeColor = SystemColors.GrayText;
        }

        private void ChangeAutoProfilesStatus(bool state)
        {
            if (state)
            {
                autoProfilesTimer.Elapsed += CheckAutoProfiles;
                autoProfilesTimer.Start();
            }
            else
            {
                autoProfilesTimer.Stop();
                autoProfilesTimer.Elapsed -= CheckAutoProfiles;
            }
        }

        private void ChangeHotkeysStatus(bool state)
        {
            if (state)
            {
                hotkeysTimer.Elapsed += Hotkeys;
                hotkeysTimer.Start();
            }
            else
            {
                hotkeysTimer.Stop();
                hotkeysTimer.Elapsed -= Hotkeys;
            }
        }

        private void blankControllerTab()
        {
            for (int Index = 0, PadsLen = Pads.Length;
                Index < PadsLen; Index++)
            {
                if (Index < API.DS4_CONTROLLER_COUNT)
                {
                    statPB[Index].Visible = false;
                    toolTip1.SetToolTip(statPB[Index], "");
                    Batteries[Index].Text = Properties.Resources.NA;
                    Pads[Index].Text = Properties.Resources.Disconnected;
                    Enable_Controls(Index, false);
                }
            }

            lbNoControllers.Visible = true;
            tLPControllers.Visible = false;
        }

        private void UpdateTheUpdater()
        {
            if (File.Exists($"{API.ExePath}\\Update Files\\DS4Updater.exe")) {
                TaskRunner.Run(async delegate {
                    while (Process.GetProcessesByName("DS4Updater").Length > 0)
                        await TaskRunner.Delay(250);
                    File.Delete(  DS4_UPDATER_FN);
                    File.Move($"{API.ExePath}\\Update Files\\DS4Updater.exe", DS4_UPDATER_FN);
                    Directory.Delete($"{API.ExePath}\\Update Files");
                });
            }
        }

        public static bool GetTopWindowName(out string topProcessName, out string topWndTitleName, bool autoProfileTimerCheck = false)
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                // Top window unknown or cannot acquire a handle. Return FALSE and return unknown process and wndTitle values
                prevForegroundWnd = IntPtr.Zero;
                prevForegroundProcessID = 0;
                topProcessName = topWndTitleName = String.Empty;
                return false;
            }

            //
            // If this function was called from "auto-profile watcher timer" then check cached "previous hWnd handle". If the current hWnd is the same
            // as during the previous check then return cached previous wnd and name values (ie. foreground app and window are assumed to be the same, so no need to re-query names).
            // This should optimize the auto-profile timer check process and causes less burden to .NET GC collector because StringBuffer is not re-allocated every second.
            //
            // Note! hWnd handles may be re-cycled but not during the lifetime of the window. This "cache" optimization still works because when an old window is closed
            // then foreground window changes to something else and the cached prevForgroundWnd variable is updated to store the new hWnd handle.
            // It doesn't matter even when the previously cached handle is recycled by WinOS to represent some other window (it is no longer used as a cached value anyway).
            //
            if(autoProfileTimerCheck)
            {
                if(hWnd == prevForegroundWnd)
                {
                    // The active window is still the same. Return cached process and wndTitle values and FALSE to indicate caller that no changes since the last call of this method
                    topProcessName = prevForegroundProcessName;
                    topWndTitleName = prevForegroundWndTitleName;
                    return false;
                }

                prevForegroundWnd = hWnd;
            }

            IntPtr hProcess = IntPtr.Zero;
            GetWindowThreadProcessId(hWnd, out var lpdwProcessId);

            if (autoProfileTimerCheck)
            {
                if (autoProfileCheckTextBuilder == null) autoProfileCheckTextBuilder = new StringBuilder(1000);

                if (lpdwProcessId == prevForegroundProcessID)
                {
                    topProcessName = prevForegroundProcessName;
                }
                else
                {
                    prevForegroundProcessID = lpdwProcessId;

                    hProcess = OpenProcess(0x0410, false, lpdwProcessId);
                    if (hProcess != IntPtr.Zero) GetModuleFileNameEx(hProcess, IntPtr.Zero, autoProfileCheckTextBuilder, autoProfileCheckTextBuilder.Capacity);
                    else autoProfileCheckTextBuilder.Clear();

                    prevForegroundProcessName = topProcessName = autoProfileCheckTextBuilder.Replace('/', '\\').ToString().ToLower();
                }

                GetWindowText(hWnd, autoProfileCheckTextBuilder, autoProfileCheckTextBuilder.Capacity);
                prevForegroundWndTitleName = topWndTitleName = autoProfileCheckTextBuilder.ToString().ToLower();
            }
            else
            {
                // Caller function was not the autoprofile timer check thread, so create a new buffer to make this call thread safe and always query process and window title names.
                // Note! At the moment DS4Win app doesn't call this method with autoProfileTimerCheck=false option, but this is here just for potential future usage.
                StringBuilder text = new StringBuilder(1000);

                hProcess = OpenProcess(0x0410, false, lpdwProcessId);
                if (hProcess != IntPtr.Zero) GetModuleFileNameEx(hProcess, IntPtr.Zero, text, text.Capacity);
                else text.Clear();
                topProcessName = text.ToString();

                GetWindowText(hWnd, text, text.Capacity);
                topWndTitleName = text.ToString();
            }

            if (hProcess != IntPtr.Zero) CloseHandle(hProcess);

            if(DS4Form.autoProfileDebugLogLevel > 0 )
                AppLogger.LogToGui($"DEBUG: Auto-Profile. PID={lpdwProcessId}  Path={topProcessName} | WND={hWnd}  Title={topWndTitleName}", false);

            return true;
        }

        private void PowerEventArrive(object sender, EventArrivedEventArgs e)
        {
            short evType = Convert.ToInt16(e.NewEvent.GetPropertyValue("EventType"));
            switch (evType)
            {
                // Wakeup from Suspend
                case 7:
                {
                    if (btnStartStop.Text == Properties.Resources.StartText && wasrunning)
                    {
                        DS4LightBar.shuttingdown = false;
                        wasrunning = false;
                        Program.rootHub.suspending = false;
                        Thread.Sleep(16000);
                        //this.Invoke((System.Action)(() => BtnStartStop_Clicked()));
                        changingService = true;
                        SynchronizationContext uiContext = null;
                        Invoke((System.Action)(() => {
                            uiContext = SynchronizationContext.Current;
                            btnStartStop.Enabled = false;
                        }));

                        Program.rootHub.Start(uiContext);

                        Invoke((System.Action)(() => {
                            ServiceStartupFinish();
                        }));

                        changingService = false;
                    }

                    break;
                }
                // Entering Suspend
                case 4:
                {
                    if (btnStartStop.Text == Properties.Resources.StopText)
                    {
                        DS4LightBar.shuttingdown = true;
                        Program.rootHub.suspending = true;
                        changingService = true;

                        Program.rootHub.Stop(true);

                        Invoke((System.Action)(() => {
                            ServiceShutdownFinish();
                            btnStartStop.Enabled = false;
                        }));
                        changingService = false;
                        wasrunning = true;
                    }

                    break;
                }
                default:
                    break;
            }
        }

        void Hotkeys(object sender, EventArgs e)
        {
            hotkeysTimer.Stop();

            if (Config.SwipeProfiles)
            {
                for (int i = 0; i < 4; i++)
                {
                    var slide = Program.RootHub(i).TouchpadSlide();
                    if (slide == DeviceControlService.TouchpadSlideDir.left)
                    {
                        int ind = i;
                        this.BeginInvoke((System.Action)(() =>
                        {
                            if (cbs[ind].SelectedIndex <= 0)
                                cbs[ind].SelectedIndex = cbs[ind].Items.Count - 2;
                            else
                                cbs[ind].SelectedIndex--;
                        }));
                    }
                    else if (slide == DeviceControlService.TouchpadSlideDir.right)
                    {
                        int ind = i;
                        this.BeginInvoke((System.Action)(() =>
                        {
                            if (cbs[ind].SelectedIndex == cbs[ind].Items.Count - 2)
                                cbs[ind].SelectedIndex = 0;
                            else
                                cbs[ind].SelectedIndex++;
                        }));
                    }

                    if (DeviceControlService.isSlideLeftRight(slide))
                    {
                        int ind = i;
                        this.BeginInvoke((System.Action)(() =>
                        {
                            ShowNotification(this, Properties.Resources.UsingProfile.Replace("*number*", (ind + 1).ToString()).Replace("*Profile name*", cbs[ind].Text));
                        }));
                    }
                }
            }

            if (bat != null && bat.HasExited && runningBat)
            {
                Process.Start("explorer.exe");
                bat = null;
                runningBat = false;
            }

            hotkeysTimer.Start();
        }

        private void CheckAutoProfiles(object sender, EventArgs e)
        {
            string[] newProfileName = new string[4] { String.Empty, String.Empty, String.Empty, String.Empty };
            bool turnOffDS4WinApp = false;
            ProgramPathItem matchingProgramPathItem = null;

            autoProfilesTimer.Stop();

            if (GetTopWindowName(out var topProcessName, out var topWindowTitle, true))
            {
                // Find a profile match based on autoprofile program path and wnd title list.
                // The same program may set different profiles for each of the controllers, so we need an array of newProfileName[controllerIdx] values.
                for (int i = 0, pathsLen = programpaths.Count; i < pathsLen; i++)
                {
                    if (programpaths[i].IsMatch(topProcessName, topWindowTitle))
                    {
                        if (DS4Form.autoProfileDebugLogLevel > 0)
                            AppLogger.LogToGui($"DEBUG: Auto-Profile. Rule#{i+1}  Path={programpaths[i].path}  Title={programpaths[i].title}", false);

                        for (int j = 0; j < 4; j++)
                        {
                            if (proprofiles[j][i] != "(none)" && proprofiles[j][i] != Properties.Resources.noneProfile)
                            {
                                newProfileName[j] = proprofiles[j][i]; // j is controller index, i is filename
                            }
                        }

                        // Matching autoprofile rule found
                        turnOffDS4WinApp = turnOffTempProfiles[i];
                        matchingProgramPathItem = programpaths[i];
                        break;
                    }
                }

                if (matchingProgramPathItem != null)
                {
                    // Program match found. Check if the new profile is different than current profile of the controller. Load the new profile only if it is not already loaded.
                    for (int j = 0; j < 4; j++)
                    {
                        var cfg = API.Cfg(j);
                        var aux = API.Aux(j);
                        if (newProfileName[j] != String.Empty)
                        {
                            if ((aux.UseTempProfile && newProfileName[j] != aux.TempProfileName) || (!aux.UseTempProfile && newProfileName[j] != cfg.ProfilePath))
                            {
                                if (DS4Form.autoProfileDebugLogLevel > 0)
                                    AppLogger.LogToGui($"DEBUG: Auto-Profile. LoadProfile Controller {j+1}={newProfileName[j]}", false);

                                cfg.LoadTempProfile(newProfileName[j], true, Program.RootHub(j)); // j is controller index, i is filename
                            }
                            else
                            {
                                if (DS4Form.autoProfileDebugLogLevel > 0)
                                    AppLogger.LogToGui($"DEBUG: Auto-Profile. LoadProfile Controller {j + 1}={newProfileName[j]} (already loaded)", false);
                            }
                        }
                    }
                    
                    if (turnOffDS4WinApp)
                    {
                        turnOffTemp = true;
                        if (btnStartStop.Text == Properties.Resources.StopText)
                        {
                            //autoProfilesTimer.Stop();
                            //hotkeysTimer.Stop();
                            ChangeAutoProfilesStatus(false);
                            ChangeHotkeysStatus(false);

                            this.Invoke((System.Action)(() =>
                            {
                                this.changingService = true;
                                BtnStartStop_Clicked();
                            }));

                            while (this.changingService)
                            {
                                Thread.SpinWait(500);
                            }

                            this.Invoke((System.Action)(() =>
                            {
                            //hotkeysTimer.Start();
                            ChangeHotkeysStatus(true);
                                ChangeAutoProfilesStatus(true);
                            //autoProfilesTimer.Start();
                        }));
                        }
                    }

                    tempProfileProgram = matchingProgramPathItem;
                }
                else if (tempProfileProgram != null)
                {
                    // The current active program doesn't match any of the programs in autoprofile path list. 
                    // Unload temp profile if controller is not using the default profile already.
                    tempProfileProgram = null;
                    for (int j = 0; j < 4; j++)
                    {
                        var cfg = API.Cfg(j);
                        if (API.Aux(j).UseTempProfile)
                        {
                            if (DS4Form.autoProfileDebugLogLevel > 0)
                                AppLogger.LogToGui($"DEBUG: Auto-Profile. RestoreProfile Controller {j + 1}={cfg.ProfilePath} (default)", false);

                            cfg.LoadProfile(false, Program.RootHub(j));
                        }
                    }

                    if (turnOffTemp)
                    {
                        turnOffTemp = false;
                        if (btnStartStop.Text == Properties.Resources.StartText)
                        {
                            this.BeginInvoke((System.Action)(() =>
                            {
                                BtnStartStop_Clicked();
                            }));
                        }
                    }
                }
            }

            autoProfilesTimer.Start();
            //GC.Collect();
        }

        public void LoadP()
        {
            XmlDocument doc = new XmlDocument();
            proprofiles = new List<string>[4] { new List<string>(), new List<string>(),
                new List<string>(), new List<string>() };
            turnOffTempProfiles = new List<bool>();
            programpaths.Clear();
            if (!File.Exists(API.AutoProfileDataPath))
                return;

            doc.Load(API.AutoProfileDataPath);
            XmlNodeList programslist = doc.SelectNodes("Programs/Program");
            foreach (XmlNode x in programslist)
                programpaths.Add(new ProgramPathItem(x.Attributes["path"]?.Value, x.Attributes["title"]?.Value));

            int nodeIdx=0;
            foreach (ProgramPathItem pathItem in programpaths)
            {
                XmlNode item;

                nodeIdx++;
                for (int i = 0; i < 4; i++)
                {
                    item = doc.SelectSingleNode($"/Programs/Program[{nodeIdx}]/Controller{i+1}");
                    if (item != null)
                        proprofiles[i].Add(item.InnerText);
                    else
                        proprofiles[i].Add("(none)");
                }

                item = doc.SelectSingleNode($"/Programs/Program[{nodeIdx}]/TurnOff");

                if (item != null && bool.TryParse(item.InnerText, out var turnOff))
                    turnOffTempProfiles.Add(turnOff);
                else
                    turnOffTempProfiles.Add(false);
            }

            int pathCount = programpaths.Count;
            bool timerEnabled = autoProfilesTimer.Enabled;
            if (pathCount > 0 && !timerEnabled)
            {
                ChangeAutoProfilesStatus(true);
                //autoProfilesTimer.Start();
            }
            else if (pathCount == 0 && timerEnabled)
            {
                //autoProfilesTimer.Stop();
                ChangeAutoProfilesStatus(false);
            }
        }

        string originalsettingstext;
        private void CheckDrivers()
        {
            originalsettingstext = tabSettings.Text;
            bool deriverinstalled = false;
            deriverinstalled = API.IsViGEmBusInstalled();
            if (!deriverinstalled)
            {
                Process p = new Process();
                p.StartInfo.FileName = Assembly.GetExecutingAssembly().Location;
                p.StartInfo.Arguments = "driverinstall";
                p.StartInfo.Verb = "runas";
                try { p.Start(); }
                catch { }
            }
        }

        private void Check_Version(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            string version = fvi.FileVersion;
            string newversion = File.ReadAllText(VERSION_FN).Trim();
            bool launchUpdate = false;
            if (!string.IsNullOrWhiteSpace(newversion) && version.Replace(',', '.').CompareTo(newversion) != 0) {
                if ((DialogResult) this.Invoke(new Func<DialogResult>(() => {
                    return MessageBox.Show(Properties.Resources.DownloadVersion.Replace("*number*", newversion),
                        Properties.Resources.DS4Update, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                })) == DialogResult.Yes) {
                    launchUpdate = true;
                    if (!File.Exists(DS4_UPDATER_FN) ||
                        (FileVersionInfo.GetVersionInfo(DS4_UPDATER_FN).FileVersion.CompareTo(UPDATER_VERSION) != 0)) {
                        Uri url2 = new Uri($"https://github.com/Ryochan7/DS4Updater/releases/download/v{UPDATER_VERSION}/{updaterExe}");
                        WebClient wc2 = new WebClient();
                        if (API.AppDataPath == API.ExePath) {
                            wc2.DownloadFile(url2, DS4_UPDATER_FN);
                        }
                        else {
                            this.BeginInvoke((System.Action) (() => MessageBox.Show(Properties.Resources.PleaseDownloadUpdater)));
                            Process.Start($"https://github.com/Ryochan7/DS4Updater/releases/download/v{UPDATER_VERSION}/{updaterExe}");
                            launchUpdate = false;
                        }
                    }

                    if (launchUpdate) {
                        Process p = new Process();
                        p.StartInfo.FileName = DS4_UPDATER_FN;
                        p.StartInfo.Arguments = "-autolaunch";
                        if (API.ExePathNeedsAdmin)
                            p.StartInfo.Verb = "runas";

                        try {
                            p.Start();
                            Close();
                        }
                        catch { }
                    }
                }
                else
                    File.Delete(VERSION_FN);
            }
            else
                File.Delete(VERSION_FN);
        }

        public void RefreshProfiles()
        {
            var cfg0 = API.Cfg(0);
            var profilePath = $"{API.AppDataPath}\\Profiles\\";
            try
            {
                profilenames.Clear();
                string[] profiles = Directory.GetFiles(profilePath);
                foreach (string s in profiles)
                {
                    if (s.EndsWith(".xml"))
                        profilenames.Add(Path.GetFileNameWithoutExtension(s));
                }

                lBProfiles.Items.Clear();
                lBProfiles.Items.AddRange(profilenames.ToArray());
                if (lBProfiles.Items.Count == 0) {
                    cfg0.SaveProfile("Default");
                    cfg0.ProfilePath = cfg0.OlderProfilePath = "Default";
                    RefreshProfiles();
                    return;
                }
                for (int i = 0; i < 4; i++)
                {
                    var cfg = API.Cfg(i);
                    cbs[i].Items.Clear();
                    shortcuts[i].DropDownItems.Clear();
                    cbs[i].Items.AddRange(profilenames.ToArray());
                    foreach (string s in profilenames)
                        shortcuts[i].DropDownItems.Add(s);

                    for (int j = 0, itemCount = cbs[i].Items.Count; j < itemCount; j++) {
                        if (cbs[i].Items[j].ToString() == Path.GetFileNameWithoutExtension(cfg.ProfilePath))
                        {
                            cbs[i].SelectedIndex = j;
                            ((ToolStripMenuItem)shortcuts[i].DropDownItems[j]).Checked = true;
                            cfg.ProfilePath = cfg.OlderProfilePath = cbs[i].Text;
                            shortcuts[i].Text = Properties.Resources.ContextEdit.Replace("*number*", (i + 1).ToString());
                            ebns[i].Text = Properties.Resources.EditProfile;
                            break;
                        }
                        else
                        {
                            cbs[i].Text = "(" + Properties.Resources.NoProfileLoaded + ")";
                            shortcuts[i].Text = Properties.Resources.ContextNew.Replace("*number*", (i + 1).ToString());
                            ebns[i].Text = Properties.Resources.New;
                        }
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                Directory.CreateDirectory(profilePath);
                cfg0.SaveProfile("Default");
                cfg0.ProfilePath = cfg0.OlderProfilePath = "Default";
                RefreshProfiles();
                return;
            }
            finally
            {
                if (!(cbs[0].Items.Count > 0 && cbs[0].Items[cbs[0].Items.Count - 1].ToString() == "+" + Properties.Resources.PlusNewProfile))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        cbs[i].Items.Add("+" + Properties.Resources.PlusNewProfile);
                        shortcuts[i].DropDownItems.Add("-");
                        shortcuts[i].DropDownItems.Add("+" + Properties.Resources.PlusNewProfile);
                    }
                    RefreshAutoProfilesPage();
                }
            }
        }


        public void RefreshAutoProfilesPage()
        {
            tabAutoProfiles.Controls.Clear();
            WinProgs WP = new WinProgs(profilenames.ToArray(), this) {
                TopLevel = false,
                FormBorderStyle = FormBorderStyle.None,
                Visible = true,
                Dock = DockStyle.Fill
            };
            tabAutoProfiles.Controls.Add(WP);
        }

        protected void LogDebug(DateTime Time, String Data, bool warning)
        {
            if (this.InvokeRequired)
            {
                LogDebugDelegate d = new LogDebugDelegate(LogDebug);
                try
                {
                    // Make sure to invoke method asynchronously instead of waiting for result
                    this.BeginInvoke(d, new object[] { Time, Data, warning });
                    //this.Invoke(d, new object[] { Time, Data, warning });
                }
                catch { }
            }
            else
            {
                String Posted = Time.ToString("G");
                lvDebug.Items.Add(new ListViewItem(new String[] { Posted, Data })).EnsureVisible();
                if (warning) lvDebug.Items[lvDebug.Items.Count - 1].ForeColor = Color.Red;
                // Added alternative
                lbLastMessage.Text = Data;
                lbLastMessage.ForeColor = (warning ? Color.Red : SystemColors.GrayText);
            }
        }

        protected void ShowNotification(object sender, DebugEventArgs args)
        {
            if (this.InvokeRequired)
            {
                NotificationDelegate d = new NotificationDelegate(ShowNotification);

                try
                {
                    // Make sure to invoke method asynchronously instead of waiting for result
                    this.BeginInvoke(d, new object[] { sender, args });
                }
                catch { }
            }
            else
            {
                if (Form.ActiveForm != this && (Config.Notifications == 2 || (Config.Notifications == 1 && args.Warning) || sender != null))
                {
                    this.notifyIcon1.BalloonTipText = args.Data;
                    notifyIcon1.BalloonTipTitle = "DS4Windows";
                    notifyIcon1.ShowBalloonTip(1);
                }
            }
        }

        protected void ShowNotification(object sender, string text)
        {
            if (Form.ActiveForm != this && Config.Notifications == 2)
            {
                this.notifyIcon1.BalloonTipText = text;
                notifyIcon1.BalloonTipTitle = "DS4Windows";
                notifyIcon1.ShowBalloonTip(1);
            }
        }

        protected void Form_Resize(object sender, EventArgs e)
        {
            bool minToTask = Config.MinToTaskbar;
            if (FormWindowState.Minimized == WindowState && !minToTask)
            {
                Hide();
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
            }

            else if (FormWindowState.Normal == WindowState && !minToTask)
            {
                //mAllowVisible = true;
                Show();
                ShowInTaskbar = true;
                FormBorderStyle = FormBorderStyle.Sizable;
            }

            if (WindowState != FormWindowState.Minimized)
                Config.FormSize = Size;

            chData.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        private void TrackLocationChanged(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized)
                Config.FormLocation = Location;
        }

        private void BtnStartStop_Click(object sender, EventArgs e)
        {
            BtnStartStop_Clicked();
        }

        private void ServiceStartup(bool log)
        {
            var uiContext = SynchronizationContext.Current;
            changingService = true;
            btnStartStop.Enabled = false;
            TaskRunner.Run(() =>
            {
                //Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                Program.rootHub.Start(uiContext, log);
                Invoke((System.Action)(() => { ServiceStartupFinish(); }));
                changingService = false;
            });
        }

        private void ServiceStartupFinish()
        {
            if (Config.SwipeProfiles && !hotkeysTimer.Enabled)
            {
                ChangeHotkeysStatus(true);
                //hotkeysTimer.Start();
            }

            if (programpaths.Count > 0 && !autoProfilesTimer.Enabled)
            {
                ChangeAutoProfilesStatus(true);
                //autoProfilesTimer.Start();
            }

            startToolStripMenuItem.Text = btnStartStop.Text = Properties.Resources.StopText;
            btnStartStop.Enabled = true;
        }

        private void ServiceShutdown(bool log)
        {
            changingService = true;
            btnStartStop.Enabled = false;
            TaskRunner.Run(() =>
            {
                Program.rootHub.Stop(log);
                Invoke((System.Action)(() => { ServiceShutdownFinish(); }));
                changingService = false;
            });
        }

        private void ServiceShutdownFinish()
        {
            ChangeAutoProfilesStatus(false);
            ChangeHotkeysStatus(false);
            //hotkeysTimer.Stop();
            //autoProfilesTimer.Stop();
            startToolStripMenuItem.Text = btnStartStop.Text = Properties.Resources.StartText;
            btnStartStop.Enabled = true;
            blankControllerTab();
            populateFullNotifyText();
        }

        public void BtnStartStop_Clicked(bool log = true)
        {
            if (btnStartStop.Text == Properties.Resources.StartText)
            {
                ServiceStartup(log);
            }
            else if (btnStartStop.Text == Properties.Resources.StopText)
            {
                blankControllerTab();
                ServiceShutdown(log);
            }
        }

        protected void BtnClear_Click(object sender, EventArgs e)
        {
            lvDebug.Items.Clear();
            lbLastMessage.Text = string.Empty;
        }

        private bool inHotPlug = false;
        private int hotplugCounter = 0;
        private object hotplugCounterLock = new object();
        private const int DBT_DEVNODES_CHANGED = 0x0007;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Util.WM_CREATE:
                {
                    Guid hidGuid = new Guid();
                    NativeMethods.HidD_GetHidGuid(ref hidGuid);
                    bool result = Util.RegisterNotify(this.Handle, hidGuid, ref regHandle);
                    if (!result)
                    {
                        ScpForm_Closing(this,
                            new FormClosingEventArgs(CloseReason.ApplicationExitCall, false));
                    }
                    break;
                }
                case Util.WM_DEVICECHANGE:
                {
                    if (API.RunHotPlug)
                    {
                        Int32 Type = m.WParam.ToInt32();
                        if (Type == DBT_DEVICEARRIVAL ||
                            Type == DBT_DEVICEREMOVECOMPLETE)
                        {
                            lock (hotplugCounterLock)
                            {
                                hotplugCounter++;
                            }

                            if (!inHotPlug)
                            {
                                inHotPlug = true;
                                TaskRunner.Run(() => { InnerHotplug2(); });
                            }
                        }
                    }
                    break;
                }
                case WM_QUERYENDSESSION:
                {
                    systemShutdown = true;
                    break;
                }
                case WM_COPYDATA:
                {
                        // Received InterProcessCommunication (IPC) message. DS4Win command is embedded as a string value in lpData buffer
                        Program.COPYDATASTRUCT cds  = (Program.COPYDATASTRUCT)m.GetLParam(typeof(Program.COPYDATASTRUCT));
                        if (cds.cbData >= 4 && cds.cbData <= 256)
                        {
                            int tdevice = -1;

                            byte[] buffer = new byte[cds.cbData];
                            Marshal.Copy(cds.lpData, buffer, 0, cds.cbData);
                            string[] strData = Encoding.ASCII.GetString(buffer).Split('.');

                            if (strData.Length >= 1)
                            {
                                strData[0] = strData[0].ToLower();

                                if (strData[0] == "start")
                                    ServiceStartup(true);
                                else if (strData[0] == "stop")
                                    ServiceShutdown(true);
                                else if (strData[0] == "shutdown")
                                    ScpForm_Closing(this, new FormClosingEventArgs(CloseReason.ApplicationExitCall, false));
                                else if ( (strData[0] == "loadprofile" || strData[0] == "loadtempprofile") && strData.Length >= 3)
                                {
                                    // Command syntax: LoadProfile.device#.profileName (fex LoadProfile.1.GameSnake or LoadTempProfile.1.WebBrowserSet)
                                    if(int.TryParse(strData[1], out tdevice)) tdevice--;

                                    if (tdevice >= 0 && tdevice < API.DS4_CONTROLLER_COUNT && File.Exists($"{API.AppDataPath}\\Profiles\\{strData[2]}.xml"))
                                    {
                                        var cfg = API.Cfg(tdevice);
                                        if (strData[0] == "loadprofile")
                                        {
                                            cfg.ProfilePath = strData[2];
                                            cfg.LoadProfile(true, Program.RootHub(tdevice));
                                        }
                                        else
                                        {
                                            cfg.LoadTempProfile(strData[2], true, Program.RootHub(tdevice));
                                        }

                                        Program.rootHub.LogDebug(Properties.Resources.UsingProfile.Replace("*number*", (tdevice + 1).ToString()).Replace("*Profile name*", strData[2]));
                                    }
                                }
                            }
                        }
                        break;
                }
                default: break;
            }

            // If this is WM_QUERYENDSESSION, the closing event should be
            // raised in the base WndProc.
            base.WndProc(ref m);
        }

        private void InnerHotplug2()
        {
            inHotPlug = true;

            bool loopHotplug = false;
            lock (hotplugCounterLock)
            {
                loopHotplug = hotplugCounter > 0;
            }

            while (loopHotplug == true)
            {
                Thread.Sleep(1500);
                Program.rootHub.HotPlug();
                //TaskRunner.Run(() => { Program.rootHub.HotPlug(uiContext); });
                lock (hotplugCounterLock)
                {
                    hotplugCounter--;
                    loopHotplug = hotplugCounter > 0;
                }
            }

            inHotPlug = false;
        }

        protected void BatteryStatusUpdate(object sender, BatteryReportArgs args)
        {
            int level = args.level;
            int index = args.index;
            string battery;
            if (args.isCharging)
            {
                if (level >= 100)
                    battery = Properties.Resources.Full;
                else
                    battery = $"{level}%+";
            }
            else
            {
                battery = $"{level}%";
            }

            Batteries[index].Text = battery;

            // Update device battery level display for tray icon
            generateDeviceNotifyText(index);
            populateNotifyText();
        }

        protected void populateFullNotifyText()
        {
            for (int i = 0; i < API.DS4_CONTROLLER_COUNT; i++)
            {
                string temp = Program.RootHub(i).getShortDS4ControllerInfo();
                if (temp != Properties.Resources.NoneText)
                {
                    notifyText[i + 1] = (i + 1) + ": " + temp;
                }
                else
                {
                    notifyText[i + 1] = string.Empty;
                }
            }

            populateNotifyText();
        }

        protected void generateDeviceNotifyText(int index)
        {
            string temp = Program.RootHub(index).getShortDS4ControllerInfo();
            if (temp != Properties.Resources.NoneText)
            {
                notifyText[index + 1] = (index + 1) + ": " + temp;
            }
            else
            {
                notifyText[index + 1] = string.Empty;
            }
        }

        protected void populateNotifyText()
        {
            string tooltip = notifyText[0];
            for (int i = 1; i < 5; i++)
            {
                string temp = notifyText[i];
                if (!string.IsNullOrEmpty(temp))
                {
                    tooltip += "\n" + notifyText[i];
                }
            }

            notifyIcon1.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip; // Carefully stay under the 63 character limit.
        }

        protected void DeviceSerialChanged(object sender, SerialChangeArgs args)
        {
            if (this.InvokeRequired)
            {
                DeviceSerialChangedDelegate d = new DeviceSerialChangedDelegate(DeviceSerialChanged);
                this.BeginInvoke(d, new object[] { sender, args });
            }
            else
            {
                int devIndex = args.index;
                var cfg = API.Cfg(devIndex);
                string serial = args.serial;
                DS4Device device = (devIndex >= 0 && devIndex < API.DS4_CONTROLLER_COUNT) ?
                    Program.RootHub(devIndex).DS4Controller : null;
                if (device != null)
                {
                    Pads[devIndex].Text = serial;
                    linkedProfileCB[devIndex].Enabled = device.IsSynced;
 
                    if (device.isValidSerial() && Config.ContainsLinkedProfile(device.MacAddress))
                    {
                        cfg.ProfilePath = Config.GetLinkedProfile(device.MacAddress);
                        int profileIndex = cbs[devIndex].FindString(cfg.ProfilePath);
                        if (profileIndex >= 0)
                        {
                            cbs[devIndex].SelectedIndex = profileIndex;
                        }
                    }
                    else
                    {
                        cfg.ProfilePath = cfg.OlderProfilePath;                        
                    }

                    linkedProfileCB[devIndex].Checked = false;
                }
            }
        }

        protected void DeviceStatusChanged(object sender, DeviceStatusChangeEventArgs args)
        {
            if (this.InvokeRequired)
            {
                DeviceStatusChangedDelegate d = new DeviceStatusChangedDelegate(DeviceStatusChanged);
                this.BeginInvoke(d, new object[] { sender, args });
            }
            else
            {
                //string tooltip = "DS4Windows v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
                int Index = args.index;
                if (Index >= 0 && Index < API.DS4_CONTROLLER_COUNT)
                {
                    var cfg = API.Cfg(Index);
                    Pads[Index].Text = Program.RootHub(Index).getDS4MacAddress();

                    linkedProfileCB[Index].CheckedChanged -= linkCB_CheckedChanged;
                    if (DS4Device.isValidSerial(Pads[Index].Text))
                    {
                        linkedProfileCB[Index].Checked = Config.ContainsLinkedProfile(Pads[Index].Text);
                        linkedProfileCB[Index].Enabled = true;
                    }
                    else
                    {
                        linkedProfileCB[Index].Checked = false;
                        linkedProfileCB[Index].Enabled = false;
                    }

                    linkedProfileCB[Index].CheckedChanged += linkCB_CheckedChanged;

                    switch (Program.RootHub(Index).getDS4Status())
                    {
                        case "USB": statPB[Index].Visible = true; statPB[Index].Image = Properties.Resources.USB; toolTip1.SetToolTip(statPB[Index], ""); break;
                        case "BT": statPB[Index].Visible = true; statPB[Index].Image = Properties.Resources.BT; toolTip1.SetToolTip(statPB[Index], "Right click to disconnect"); break;
                        case "SONYWA": statPB[Index].Visible = true; statPB[Index].Image = Properties.Resources.BT; toolTip1.SetToolTip(statPB[Index], "Right click to disconnect"); break;
                        default: statPB[Index].Visible = false; toolTip1.SetToolTip(statPB[Index], ""); break;
                    }

                    Batteries[Index].Text = Program.RootHub(Index).getDS4Battery();
                    int profileIndex = cbs[Index].FindStringExact(cfg.ProfilePath);
                    if (profileIndex >= 0)
                    {
                        cbs[Index].SelectedValueChanged -= Profile_Changed;
                        cbs[Index].SelectedIndex = profileIndex;
                        cbs[Index].SelectedValueChanged += Profile_Changed;
                    }

                    if (cfg.UseCustomColor)
                        lights[Index].BackColor = cfg.CustomColor.ToColorA;
                    else
                        lights[Index].BackColor = cfg.MainColor.ToColorA;

                    if (Pads[Index].Text != String.Empty)
                    {
                        if (runningBat)
                        {
                            SendKeys.Send("A");
                            runningBat = false;
                        }

                        Pads[Index].Enabled = true;
                        if (Pads[Index].Text != Properties.Resources.Connecting)
                        {
                            Enable_Controls(Index, true);
                        }
                    }
                    else
                    {
                        Pads[Index].Text = Properties.Resources.Disconnected;
                        Enable_Controls(Index, false);
                    }

                    generateDeviceNotifyText(Index);
                    populateNotifyText();
                }

                bool nocontrollers = Program.NoControllers();
                lbNoControllers.Visible = nocontrollers;
                tLPControllers.Visible = !nocontrollers;
            }
        }

        protected void ControllerRemovedChange(object sender, ControllerRemovedArgs args)
        {
            int devIndex = args.index;
            Pads[devIndex].Text = Properties.Resources.Disconnected;
            Enable_Controls(devIndex, false);
            statPB[devIndex].Visible = false;
            toolTip1.SetToolTip(statPB[devIndex], "");

            bool nocontrollers = Program.NoControllers();
            lbNoControllers.Visible = nocontrollers;
            tLPControllers.Visible = !nocontrollers;

            // Update device battery level display for tray icon
            generateDeviceNotifyText(devIndex);
            populateNotifyText();
        }

        private void pBStatus_MouseClick(object sender, MouseEventArgs e)
        {
            int i = Convert.ToInt32(((PictureBox)sender).Tag);
            var dCS = Program.RootHub(i);
            DS4Device d = dCS.DS4Controller;
            if (d != null)
            {
                if (e.Button == MouseButtons.Right && dCS.getDS4Status() == "BT" && !d.IsCharging)
                {
                    d.DisconnectBT();
                }
                else if (e.Button == MouseButtons.Right &&
                    dCS.getDS4Status() == "SONYWA" && !d.IsCharging)
                {
                    d.DisconnectDongle();
                }
            }
        }

        private void Enable_Controls(int device, bool on)
        {
            DS4Device dev = Program.RootHub(device).DS4Controller;
            ConnectionType conType = ConnectionType.USB;
            if (dev != null)
                conType = dev.ConnectionType;

            Pads[device].Visible = on;
            ebns[device].Visible = on;
            lights[device].Visible = on;
            cbs[device].Visible = on;
            shortcuts[device].Visible = on;
            Batteries[device].Visible = on;
            linkedProfileCB[device].Visible = on;
            disconnectShortcuts[device].Visible = on && conType != ConnectionType.USB;
        }

        protected void On_Debug(object sender, DebugEventArgs e)
        {
            LogDebug(e.Time, e.Data, e.Warning);
        }

        private void lBProfiles_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
                ShowOptions(4, lBProfiles.SelectedItem.ToString());
        }


        private void lBProfiles_KeyDown(object sender, KeyEventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0 && optPop && !opt.Visible)
            {
                if (e.KeyValue == 13)
                    ShowOptions(4, lBProfiles.SelectedItem.ToString());
                else if (e.KeyValue == 46)
                    tsBDeleteProfle_Click(this, e);
                else if (e.KeyValue == 68 && e.Modifiers == Keys.Control)
                    tSBDupProfile_Click(this, e);
            }
        }

        private void assignToController1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[0].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void assignToController2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[1].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void assignToController3ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[2].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void assignToController4ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[3].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void tsBNewProfile_Click(object sender, EventArgs e) //Also used for context menu
        {
            ShowOptions(4, "");
        }

        private void tsBNEditProfile_Click(object sender, EventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
                ShowOptions(4, lBProfiles.SelectedItem.ToString());
        }

        private void tsBDeleteProfle_Click(object sender, EventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
            {
                string filename = lBProfiles.SelectedItem.ToString();
                if (MessageBox.Show(Properties.Resources.ProfileCannotRestore.Replace("*Profile name*", "\"" + filename + "\""),
                    Properties.Resources.DeleteProfile,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    File.Delete($"{API.AppDataPath}\\Profiles\\{filename}.xml");
                    RefreshProfiles();
                }
            }
        }

        private void tSBDupProfile_Click(object sender, EventArgs e)
        {
            string filename = "";
            if (lBProfiles.SelectedIndex >= 0)
            {
                filename = lBProfiles.SelectedItem.ToString();
                DupBox MTB = new DupBox(filename, this) {
                    TopLevel = false,
                    Dock = DockStyle.Top,
                    Visible = true,
                    FormBorderStyle = FormBorderStyle.None
                };
                tabProfiles.Controls.Add(MTB);
                lBProfiles.SendToBack();
                toolStrip1.SendToBack();
                toolStrip1.Enabled = false;
                lBProfiles.Enabled = false;
                MTB.FormClosed += delegate { toolStrip1.Enabled = true; lBProfiles.Enabled = true; };
            }
        }

        private void tSBImportProfile_Click(object sender, EventArgs e)
        {
            if (API.AppDataPath == Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName)
                openProfiles.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Tool" + @"\Profiles\";
            else
                openProfiles.InitialDirectory = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName + @"\Profiles\";

            if (openProfiles.ShowDialog() == DialogResult.OK)
            {
                string[] files = openProfiles.FileNames;
                for (int i = 0, arlen = files.Length; i < arlen; i++)
                    File.Copy(openProfiles.FileNames[i], $"{API.AppDataPath}\\Profiles\\{Path.GetFileName(files[i])}", true);

                RefreshProfiles();
            }
        }

        private void tSBExportProfile_Click(object sender, EventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
            {
                Stream stream;
                Stream profile = new StreamReader($"{API.AppDataPath}\\Profiles\\{lBProfiles.SelectedItem.ToString()}.xml").BaseStream;                
                if (saveProfiles.ShowDialog() == DialogResult.OK)
                {
                    if ((stream = saveProfiles.OpenFile()) != null)
                    {
                        profile.CopyTo(stream);
                        profile.Close();
                        stream.Close();
                    }
                }
            }
        }

        private void ShowOptions(int devID, string profile)
        {
            Show();

            lBProfiles.Visible = false;

            WindowState = FormWindowState.Normal;
            toolStrip1.Enabled = false;
            tSOptions.Visible = true;
            toolStrip1.Visible = false;
            if (profile != "")
                tSTBProfile.Text = profile;
            else
                tSTBProfile.Text = "<" + Properties.Resources.TypeProfileName + ">";

            opt = new Options(this) {
                Icon = this.Icon,
                TopLevel = false,
                Dock = DockStyle.Fill,
                FormBorderStyle = FormBorderStyle.None
            };
            tabProfiles.Controls.Add(opt);
            optPop = true;
            //opt.Dock = DockStyle.Fill;
            //lBProfiles.SendToBack();
            //toolStrip1.SendToBack();
            //tSOptions.SendToBack();
            opt.BringToFront();
            oldsize = Size;
            {
                if (Size.Height < (int)(90 * dpiy) + Options.mSize.Height)
                    Size = new Size(Size.Width, (int)(90 * dpiy) + Options.mSize.Height);

                if (Size.Width < (int)(20 * dpix) + Options.mSize.Width)
                    Size = new Size((int)(20 * dpix) + Options.mSize.Width, Size.Height);
            }

            opt.Reload(devID, profile);

            opt.inputtimer.Start();
            opt.Visible = true;

            tabMain.SelectedIndex = 1;
            opt.SetFlowAutoScroll();
        }

        public void OptionsClosed()
        {
            RefreshProfiles();

            if (!lbNoControllers.Visible)
                tabMain.SelectedIndex = 0;

            Size = oldsize;
            oldsize = new Size(0, 0);
            tSBKeepSize.Text = Properties.Resources.KeepThisSize;
            tSBKeepSize.Image = Properties.Resources.size;
            tSBKeepSize.Enabled = true;
            tSOptions.Visible = false;
            toolStrip1.Visible = true;
            toolStrip1.Enabled = true;
            lbLastMessage.ForeColor = SystemColors.GrayText;

            opt.Dock = DockStyle.None;
            tabProfiles.Controls.Remove(opt);
            optPop = false; opt = null;

            lBProfiles.Visible = true;
        }

        private void editButtons_Click(object sender, EventArgs e)
        {
            Button bn = (Button)sender;
            //int i = Int32.Parse(bn.Tag.ToString());
            int i = Convert.ToInt32(bn.Tag);
            string profileText = cbs[i].Text;
            if (profileText != "(" + Properties.Resources.NoProfileLoaded + ")")
                ShowOptions(i, profileText);
            else
                ShowOptions(i, "");
        }

        private void editMenu_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
            ToolStripMenuItem em = (ToolStripMenuItem)sender;
            int i = Convert.ToInt32(em.Tag);
            if (em.Text == Properties.Resources.ContextNew.Replace("*number*", (i + 1).ToString()))
                ShowOptions(i, "");
            else
            {
                for (int t = 0, itemCount = em.DropDownItems.Count - 2; t < itemCount; t++)
                {
                    if (((ToolStripMenuItem)em.DropDownItems[t]).Checked)
                        ShowOptions(i, ((ToolStripMenuItem)em.DropDownItems[t]).Text);
                }
            }
        }

        private void lnkControllers_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("control", "joy.cpl");
        }

        private void hideDS4CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Prevent the Game Controllers window from throwing an error when controllers are un/hidden
            Process[] rundll64 = Process.GetProcessesByName("rundll64");
            foreach (Process rundll64Instance in rundll64)
            {
                foreach (ProcessModule module in rundll64Instance.Modules)
                {
                    if (module.FileName.Contains("joy.cpl"))
                        module.Dispose();
                }
            }

            bool exclusiveMode = hideDS4CheckBox.Checked;
            Config.UseExclusiveMode = exclusiveMode;

            hideDS4CheckBox.Enabled = false;
            Config.Save();
            BtnStartStop_Clicked(false);
            finishHideDS4Check();
        }

        private async void finishHideDS4Check()
        {
            await TaskRunner.Factory.StartNew(() =>
            {
                while (changingService)
                {
                    Thread.Sleep(10);
                }
            });

            BtnStartStop_Clicked(false);

            await TaskRunner.Factory.StartNew(() =>
            {
                while (changingService)
                {
                    Thread.Sleep(10);
                }
            });

            hideDS4CheckBox.Enabled = true;
        }

        private void startMinimizedCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            Config.StartMinimized = startMinimizedCheckBox.Checked;
            Config.Save();
        }

        private void lvDebug_ItemActivate(object sender, EventArgs e)
        {
            MessageBox.Show(((ListView)sender).FocusedItem.SubItems[1].Text, "Log");
        }

        private void Profile_Changed(object sender, EventArgs e) //cbs[i] changed
        {
            ComboBox cb = (ComboBox)sender;
            int tdevice = Convert.ToInt32(cb.Tag);
            var cfg = API.Cfg(tdevice);
            var dCS = Program.RootHub(tdevice);
            if (cb.Items[cb.Items.Count - 1].ToString() == "+" + Properties.Resources.PlusNewProfile)
            {
                if (cb.SelectedIndex < cb.Items.Count - 1)
                {
                    for (int i = 0, arlen = shortcuts[tdevice].DropDownItems.Count; i < arlen; i++)
                    {
                        if (!(shortcuts[tdevice].DropDownItems[i] is ToolStripSeparator))
                            ((ToolStripMenuItem)shortcuts[tdevice].DropDownItems[i]).Checked = false;
                    }

                    ((ToolStripMenuItem)shortcuts[tdevice].DropDownItems[cb.SelectedIndex]).Checked = true;
                    LogDebug(DateTime.Now, Properties.Resources.UsingProfile.Replace("*number*", (tdevice + 1).ToString()).Replace("*Profile name*", cb.Text), false);
                    shortcuts[tdevice].Text = Properties.Resources.ContextEdit.Replace("*number*", (tdevice + 1).ToString());
                    cfg.ProfilePath = cb.Items[cb.SelectedIndex].ToString();
                    Config.Save();
                    cfg.LoadProfile(true, dCS);
                    if (cfg.UseCustomColor)
                        lights[tdevice].BackColor = cfg.CustomColor.ToColorA;
                    else
                        lights[tdevice].BackColor = cfg.MainColor.ToColorA;

                    if (linkedProfileCB[tdevice].Checked)
                    {
                        DS4Device device = dCS.DS4Controller;
                        if (device?.isValidSerial() ?? false)
                        {
                            Config.SetLinkedProfile(device.MacAddress, cfg.ProfilePath);
                            Config.SaveLinkedProfiles();
                        }
                    }
                    else
                    {
                        cfg.OlderProfilePath = cfg.ProfilePath;
                    }
                }
                else if (cb.SelectedIndex == cb.Items.Count - 1 && cb.Items.Count > 1) //if +New Profile selected
                    ShowOptions(tdevice, "");

                if (cb.Text == "(" + Properties.Resources.NoProfileLoaded + ")")
                    ebns[tdevice].Text = Properties.Resources.New;
                else
                    ebns[tdevice].Text = Properties.Resources.EditProfile;
            }

            OnDeviceStatusChanged(this, tdevice); //to update profile name in notify icon
        }

        private void Profile_Changed_Menu(object sender, ToolStripItemClickedEventArgs e)
        {
            ToolStripMenuItem tS = (ToolStripMenuItem)sender;
            int tdevice = Convert.ToInt32(tS.Tag);
            if (!(e.ClickedItem is ToolStripSeparator))
            {
                if (e.ClickedItem != tS.DropDownItems[tS.DropDownItems.Count - 1]) //if +New Profile not selected 
                    cbs[tdevice].SelectedIndex = tS.DropDownItems.IndexOf(e.ClickedItem);
                else //if +New Profile selected
                    ShowOptions(tdevice, "");
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            contextclose = true;
            Close();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Show();
            Focus();
            WindowState = FormWindowState.Normal;
        }

        private void startToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BtnStartStop_Clicked();
        }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                contextclose = true;
                Close();
            }
        }

        private void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void llbHelp_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Hotkeys hotkeysForm = new Hotkeys() {
                Icon = this.Icon,
                Text = llbHelp.Text
            };
            hotkeysForm.ShowDialog();
        }

    private void StartWindowsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            bool isChecked = StartWindowsCheckBox.Checked;
            if (isChecked && !File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk"))
            {
                appShortcutToStartup();
            }
            else if (!isChecked)
            {
                File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
            }

            if (isChecked)
            {
                runStartupPanel.Visible = true;
            }
            else
            {
                runStartupPanel.Visible = false;
                runStartTaskRadio.Checked = false;
                runStartProgRadio.Checked = true;
            }

            changeStartupRoutine();
        }

        private void appShortcutToStartup()
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                var lnk = shell.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                try
                {
                    string app = Assembly.GetExecutingAssembly().Location;
                    lnk.TargetPath = Assembly.GetExecutingAssembly().Location;

                    if (runStartProgRadio.Checked)
                    {
                        lnk.Arguments = "-m";
                    }
                    else if (runStartTaskRadio.Checked)
                    {
                        lnk.Arguments = "-runtask";
                    }

                    //lnk.TargetPath = Assembly.GetExecutingAssembly().Location;
                    //lnk.Arguments = "-m";
                    lnk.IconLocation = app.Replace('\\', '/');
                    lnk.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(lnk);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private void tabMain_SelectedIndexChanged(object sender, EventArgs e)
        {
            TabPage currentTab = tabMain.SelectedTab;
            lbLastMessage.Visible = currentTab != tabLog;
            if (currentTab == tabLog)
                chData.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

            if (currentTab == tabSettings)
            {
                lbLastMessage.ForeColor = SystemColors.GrayText;
                lbLastMessage.Text = Properties.Resources.HoverOverItems;
            }
            else if (lvDebug.Items.Count > 0)
                lbLastMessage.Text = lvDebug.Items[lvDebug.Items.Count - 1].SubItems[1].Text;
            else
                lbLastMessage.Text = "";
        }

        private void Items_MouseHover(object sender, EventArgs e)
        {
            lbLastMessage.Text = Properties.Resources.HoverOverItems;
            lbLastMessage.ForeColor = SystemColors.GrayText;
            if (hoverTextDict.TryGetValue((Control) sender, out var hoverText))
            {
                lbLastMessage.Text = hoverText;
                lbLastMessage.ForeColor = Color.Black;
            }
        }

        private void lBProfiles_MouseDown(object sender, MouseEventArgs e)
        {
            lBProfiles.SelectedIndex = lBProfiles.IndexFromPoint(e.X, e.Y);
            if (e.Button == MouseButtons.Right)
            {
                if (lBProfiles.SelectedIndex < 0)
                {
                    cMProfile.ShowImageMargin = false;
                    assignToController1ToolStripMenuItem.Visible = false;
                    assignToController2ToolStripMenuItem.Visible = false;
                    assignToController3ToolStripMenuItem.Visible = false;
                    assignToController4ToolStripMenuItem.Visible = false;
                    deleteToolStripMenuItem.Visible = false;
                    editToolStripMenuItem.Visible = false;
                    duplicateToolStripMenuItem.Visible = false;
                    exportToolStripMenuItem.Visible = false;
                }
                else
                {
                    cMProfile.ShowImageMargin = true;
                    assignToController1ToolStripMenuItem.Visible = true;
                    assignToController2ToolStripMenuItem.Visible = true;
                    assignToController3ToolStripMenuItem.Visible = true;
                    assignToController4ToolStripMenuItem.Visible = true;
                    ToolStripMenuItem[] assigns = { assignToController1ToolStripMenuItem, 
                                                      assignToController2ToolStripMenuItem,
                                                      assignToController3ToolStripMenuItem, 
                                                      assignToController4ToolStripMenuItem };

                    for (int i = 0; i < 4; i++)
                    {
                        if (lBProfiles.SelectedIndex == cbs[i].SelectedIndex)
                            assigns[i].Checked = true;
                        else
                            assigns[i].Checked = false;
                    }

                    deleteToolStripMenuItem.Visible = true;
                    editToolStripMenuItem.Visible = true;
                    duplicateToolStripMenuItem.Visible = true;
                    exportToolStripMenuItem.Visible = true;
                }
            }
        }

        private void tBProfile_TextChanged(object sender, EventArgs e)
        {
            if (tSTBProfile.Text != null && tSTBProfile.Text != "" &&
                !tSTBProfile.Text.Contains("\\") && !tSTBProfile.Text.Contains("/") &&
                !tSTBProfile.Text.Contains(":") && !tSTBProfile.Text.Contains("*") &&
                !tSTBProfile.Text.Contains("?") && !tSTBProfile.Text.Contains("\"") &&
                !tSTBProfile.Text.Contains("<") && !tSTBProfile.Text.Contains(">") &&
                !tSTBProfile.Text.Contains("|"))
                tSTBProfile.ForeColor = SystemColors.WindowText;
            else
                tSTBProfile.ForeColor = SystemColors.GrayText;
        }

        private void tBProfile_Enter(object sender, EventArgs e)
        {
            if (tSTBProfile.Text == "<" + Properties.Resources.TypeProfileName + ">")
                tSTBProfile.Text = "";
        }

        private void tBProfile_Leave(object sender, EventArgs e)
        {
            if (tSTBProfile.Text == "")
                tSTBProfile.Text = "<" + Properties.Resources.TypeProfileName + ">";
        }

        private void tSBCancel_Click(object sender, EventArgs e)
        {
            if (optPop && opt.Visible)
                opt.Close();
        }

        private void tSBSaveProfile_Click(object sender, EventArgs e)
        {
            var cfg = opt.cfg;
            if (optPop && opt.Visible)
            {
                opt.saving = true;
                opt.Set();

                if (tSTBProfile.Text != null && tSTBProfile.Text != "" &&
                    !tSTBProfile.Text.Contains("\\") && !tSTBProfile.Text.Contains("/") &&
                    !tSTBProfile.Text.Contains(":") && !tSTBProfile.Text.Contains("*") &&
                    !tSTBProfile.Text.Contains("?") && !tSTBProfile.Text.Contains("\"") &&
                    !tSTBProfile.Text.Contains("<") && !tSTBProfile.Text.Contains(">") &&
                    !tSTBProfile.Text.Contains("|"))
                {
                    File.Delete($"{API.AppDataPath}\\Profiles\\{opt.filename}.xml");
                    cfg.ProfilePath = tSTBProfile.Text;
                    cfg.SaveProfile(tSTBProfile.Text);
                    Config.Save();
                    opt.Close();
                }
                else
                    MessageBox.Show(Properties.Resources.ValidName, Properties.Resources.NotValid, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void tSBKeepSize_Click(object sender, EventArgs e)
        {
            oldsize = Size;
            tSBKeepSize.Text = Properties.Resources.WillKeep;
            tSBKeepSize.Image = Properties.Resources._checked;
            tSBKeepSize.Enabled = false;
        }

        private void cBUpdate_CheckedChanged(object sender, EventArgs e)
        {
            if (!cBUpdate.Checked)
            {
                nUDUpdateTime.Value = 0;
                pNUpdate.Enabled = false;
            }
            else
            {
                nUDUpdateTime.Value = 1;
                cBUpdateTime.SelectedIndex = 0;
                pNUpdate.Enabled = true;
            }
        }

        private void cBCustomSteam_CheckedChanged(object sender, EventArgs e)
        {
            Config.UseCustomSteamFolder = cBCustomSteam.Checked;
            tBSteamFolder.Enabled = cBCustomSteam.Checked;
        }

        private void tBSteamFolder_TextChanged(object sender, EventArgs e)
        {
            Config.CustomSteamFolder = tBSteamFolder.Text;
        }

        private void nUDUpdateTime_ValueChanged(object sender, EventArgs e)
        {
            int currentIndex = cBUpdateTime.SelectedIndex;
            if (currentIndex == 0)
                Config.CheckWhen = (int)nUDUpdateTime.Value;
            else if (currentIndex == 1)
                Config.CheckWhen = (int)nUDUpdateTime.Value * 24;

            if (nUDUpdateTime.Value < 1)
                cBUpdate.Checked = false;

            if (nUDUpdateTime.Value == 1)
            {
                int index = currentIndex;
                cBUpdateTime.BeginUpdate();
                cBUpdateTime.Items.Clear();
                cBUpdateTime.Items.Add(Properties.Resources.Hour);
                cBUpdateTime.Items.Add(Properties.Resources.Day);
                cBUpdateTime.SelectedIndex = index;
                cBUpdateTime.EndUpdate();
            }
            else if (cBUpdateTime.Items[0].ToString() == Properties.Resources.Hour)
            {
                int index = currentIndex;
                cBUpdateTime.BeginUpdate();
                cBUpdateTime.Items.Clear();
                cBUpdateTime.Items.Add(Properties.Resources.Hours);
                cBUpdateTime.Items.Add(Properties.Resources.Days);
                cBUpdateTime.SelectedIndex = index;
                cBUpdateTime.EndUpdate();
            }
        }

        private void cBUpdateTime_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = cBUpdateTime.SelectedIndex;
            if (index == 0)
                Config.CheckWhen = (int)nUDUpdateTime.Value;
            else if (index == 1)
                Config.CheckWhen = (int)nUDUpdateTime.Value * 24;
        }

        private void lLBUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // Sorry other devs, gonna have to find your own server
            Uri url = new Uri("https://raw.githubusercontent.com/Ryochan7/DS4Windows/jay/DS4Windows/newest.txt");
            WebClient wct = new WebClient();
            wct.DownloadFileAsync(url, $"{API.AppDataPath}\\version.txt");
            wct.DownloadFileCompleted += (sender2, e2) => TaskRunner.Run(() => wct_DownloadFileCompleted(sender2, e2));
        }

        private void cBDisconnectBT_CheckedChanged(object sender, EventArgs e)
        {
            Config.DisconnectBTAtStop = cBDisconnectBT.Checked;
        }

        void wct_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            Config.LastChecked = DateTime.Now;
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            string version2 = fvi.FileVersion;
            string newversion2 = File.ReadAllText($"{API.AppDataPath}\\version.txt").Trim();
            if (!string.IsNullOrWhiteSpace(newversion2) && version2.Replace(',', '.').CompareTo(newversion2) != 0)
            {
                if ((DialogResult)this.Invoke(new Func<DialogResult>(() =>
                {
                    return MessageBox.Show(Properties.Resources.DownloadVersion.Replace("*number*", newversion2),
                    Properties.Resources.DS4Update, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                })) == DialogResult.Yes)
                {
                    if (!File.Exists(DS4_UPDATER_FN) || 
                        (FileVersionInfo.GetVersionInfo(DS4_UPDATER_FN).FileVersion.CompareTo(UPDATER_VERSION) != 0))
                    {
                        Uri url2 = new Uri($"https://github.com/Ryochan7/DS4Updater/releases/download/v{UPDATER_VERSION}/{updaterExe}");
                        WebClient wc2 = new WebClient();
                        if (API.AppDataPath == API.ExePath)
                            wc2.DownloadFile(url2, DS4_UPDATER_FN);
                        else
                        {
                            this.BeginInvoke((System.Action)(() => MessageBox.Show(Properties.Resources.PleaseDownloadUpdater)));
                            Process.Start($"https://github.com/Ryochan7/DS4Updater/releases/download/v{UPDATER_VERSION}/{updaterExe}");
                        }
                    }

                    Process p = new Process();
                    p.StartInfo.FileName = DS4_UPDATER_FN;
                    p.StartInfo.Arguments = "-autolaunch";
                    if (API.ExePathNeedsAdmin)
                        p.StartInfo.Verb = "runas";

                    try { p.Start(); Close(); }
                    catch { }
                }
                else
                    File.Delete(VERSION_FN);
            }
            else {
                File.Delete(VERSION_FN);
                this.BeginInvoke((System.Action)(() => MessageBox.Show(Properties.Resources.UpToDate, "DS4Windows Updater")));
            }
        }

        private void linkProfiles_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start($"{API.AppDataPath}\\Profiles");
        }

        private void cBoxNotifications_SelectedIndexChanged(object sender, EventArgs e)
        {
            Config.Notifications = cBoxNotifications.SelectedIndex;
        }

        private void lLSetup_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            DriverSetupPrep();

            tabSettings.Text = originalsettingstext;
            linkSetup.LinkColor = Color.Blue;
        }

        private void DriverSetupPrep()
        {
            if (btnStartStop.Text == Properties.Resources.StopText)
                BtnStartStop_Clicked(false);

            TaskRunner.Run(() =>
            {
                while (changingService)
                {
                    Thread.SpinWait(1000);
                }

                Process p = new Process();
                p.StartInfo.FileName = Assembly.GetExecutingAssembly().Location;
                p.StartInfo.Arguments = "-driverinstall";
                p.StartInfo.Verb = "runas";
                try { p.Start(); }
                catch { }
                //WelcomeDialog wd = new WelcomeDialog();
                //wd.ShowDialog();
            });
        }

        private void ScpForm_Closing(object sender, FormClosingEventArgs e)
        {
            if (optPop)
            {
                opt.Close();
                e.Cancel = true;
                return;
            }

            bool closeMini = cBCloseMini.Checked;
            bool userClosing = e.CloseReason == CloseReason.UserClosing;
            //in case user accidentally clicks on the close button whilst "Close Minimizes" checkbox is unchecked
            if (userClosing && !closeMini && !contextclose)
            {
                if (!Program.NoControllers())
                {
                    if (MessageBox.Show(Properties.Resources.CloseConfirm, Properties.Resources.Confirm,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                    {
                        e.Cancel = true;
                        return;
                    }
                    else
                    {
                        Util.UnregisterNotify(regHandle);
                    }
                }
                else
                {
                    Util.UnregisterNotify(regHandle);
                }
            }
            else if (userClosing && closeMini && !contextclose)
            {
                WindowState = FormWindowState.Minimized;
                e.Cancel = true;
                return;
            }

            if (systemShutdown)
            // Reset the variable because the user might cancel the 
            // shutdown.
            {
                systemShutdown = false;
                DS4LightBar.shuttingdown = true;
            }

            Global.ControllerRemoved -= ControllerRemovedChange;

            if (!string.IsNullOrEmpty(API.AppDataPath))
            {
                Config.Save();
                blankControllerTab();
            }

            TaskRunner.Run(() => Program.rootHub.Stop()).Wait();
            // Make sure to stop event generation routines. Should fix odd crashes on shutdown
            Application.Exit();
        }

        private void cBSwipeProfiles_CheckedChanged(object sender, EventArgs e)
        {
            bool swipe = false;
            Config.SwipeProfiles = swipe = cBSwipeProfiles.Checked;
            bool timerEnabled = hotkeysTimer.Enabled;
            if (swipe && !timerEnabled)
            {
                ChangeHotkeysStatus(true);
                //hotkeysTimer.Start();
            }
            else if (!swipe && timerEnabled)
            {
                ChangeHotkeysStatus(false);
                //hotkeysTimer.Stop();
            }
        }

        private void cBQuickCharge_CheckedChanged(object sender, EventArgs e)
        {
            Config.QuickCharge = cBQuickCharge.Checked;
        }

        private void lbLastMessage_MouseHover(object sender, EventArgs e)
        {
            toolTip1.Show(lbLastMessage.Text, lbLastMessage, -3, -3);
        }

        private void pnlButton_MouseLeave(object sender, EventArgs e)
        {
            toolTip1.Hide(lbLastMessage);
        }

        private void cBCloseMini_CheckedChanged(object sender, EventArgs e)
        {
            Config.CloseMinimizes = cBCloseMini.Checked;
        }

        private void Pads_MouseHover(object sender, EventArgs e)
        {
            Label lb = (Label)sender;
            int i = Convert.ToInt32(lb.Tag);
            DS4Device d = Program.RootHub(i).DS4Controller;
            if (d != null)
            {
                double latency = d.Latency;
                toolTip1.Hide(Pads[i]);
                toolTip1.Show(Properties.Resources.InputDelay.Replace("*number*", latency.ToString()), lb, lb.Size.Width, 0);
            }
        }

        private void Pads_MouseLeave(object sender, EventArgs e)
        {
            toolTip1.Hide((Label)sender);
        }

        Process bat;

        private IDeviceConfig curCustomLedCfg;
        private DS4LightBar curCustomLedLightBar;
        private void EditCustomLed(object sender, EventArgs e)
        {
            Button btn = (Button)sender;
            int devIndex = Convert.ToInt32(btn.Tag);
            curCustomLedCfg = API.Cfg(devIndex);
            curCustomLedLightBar = API.Bar(devIndex);
            bool customLedChecked = curCustomLedCfg.UseCustomColor;;
            useCustomColorToolStripMenuItem.Checked = customLedChecked;
            useProfileColorToolStripMenuItem.Checked = !customLedChecked;
            cMCustomLed.Show(btn, new Point(0, btn.Height));
        }

        private void useProfileColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            curCustomLedCfg.UseCustomColor = false;
            lights[curCustomLedCfg.DevIndex].BackColor = curCustomLedCfg.MainColor.ToColorA;
            Config.Save();
        }
    
        private void useCustomColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            advColorDialog.Color = curCustomLedCfg.CustomColor.ToColor;
            AdvancedColorDialog.ColorUpdateHandler tempDel =
                new AdvancedColorDialog.ColorUpdateHandler(advColor_CustomColorUpdate);

            advColorDialog.OnUpdateColor += tempDel;
            if (advColorDialog.ShowDialog() == DialogResult.OK)
            {
                lights[curCustomLedCfg.DevIndex].BackColor = new DS4Color(advColorDialog.Color).ToColorA;
                curCustomLedCfg.CustomColor = new DS4Color(advColorDialog.Color);
                curCustomLedCfg.UseCustomColor = true;
                Config.Save();
            }

            advColorDialog.OnUpdateColor -= tempDel;
            curCustomLedLightBar.forcedFlash = 0;
            curCustomLedLightBar.forcedLight = false;
        }

        private void advColor_CustomColorUpdate(Color color, EventArgs e)
        {
            if (curCustomLedCfg.DevIndex < 4)
            {
                DS4Color dcolor = new DS4Color { red = color.R, green = color.G, blue = color.B };
                curCustomLedLightBar.forcedColor = dcolor;
                curCustomLedLightBar.forcedFlash = 0;
                curCustomLedLightBar.forcedLight = true;
            }
        }

        private void cBUseWhiteIcon_CheckedChanged(object sender, EventArgs e)
        {
            Config.UseWhiteIcon = cBUseWhiteIcon.Checked;
            notifyIcon1.Icon = Config.UseWhiteIcon ? Properties.Resources.DS4W___White : Properties.Resources.DS4W;
        }

        private void advColorDialog_OnUpdateColor(object sender, EventArgs e)
        {
            if (sender is Color)
            {
                var color = (Color)sender;
                DS4Color dcolor = new DS4Color(color);
                Console.WriteLine(dcolor);
                curCustomLedLightBar.forcedColor = dcolor;
                curCustomLedLightBar.forcedFlash = 0;
                curCustomLedLightBar.forcedLight = true;
            }
        }

        private void lBProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = lBProfiles.SelectedIndex;
            if (index >= 0)
            {
                tsBNewProfle.Enabled = true;
                tsBEditProfile.Enabled = true;
                tsBDeleteProfile.Enabled = true;
                tSBDupProfile.Enabled = true;
                tSBImportProfile.Enabled = true;
                tSBExportProfile.Enabled = true;
            }
            else
            {
                tsBNewProfle.Enabled = true;
                tsBEditProfile.Enabled = false;
                tsBDeleteProfile.Enabled = false;
                tSBDupProfile.Enabled = false;
                tSBImportProfile.Enabled = true;
                tSBExportProfile.Enabled = false;
            }
        }

        private void runStartProgRadio_Click(object sender, EventArgs e)
        {
            appShortcutToStartup();
            changeStartupRoutine();
        }

        private void runStartTaskRadio_Click(object sender, EventArgs e)
        {
            appShortcutToStartup();
            changeStartupRoutine();
        }

        private void changeStartupRoutine()
        {
            if (runStartTaskRadio.Checked)
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    TaskService ts = new TaskService();
                    Task tasker = ts.FindTask("RunDS4Windows");
                    if (tasker != null)
                    {
                        ts.RootFolder.DeleteTask("RunDS4Windows");
                    }

                    TaskDefinition td = ts.NewTask();
                    td.Actions.Add(new ExecAction(@"%windir%\System32\cmd.exe",
                        "/c start \"RunDS4Windows\" \"" + Process.GetCurrentProcess().MainModule.FileName + "\" -m",
                        new FileInfo(Process.GetCurrentProcess().MainModule.FileName).DirectoryName));

                    td.Principal.RunLevel = TaskRunLevel.Highest;
                    ts.RootFolder.RegisterTaskDefinition("RunDS4Windows", td);
                }
            }
            else
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    TaskService ts = new TaskService();
                    Task tasker = ts.FindTask("RunDS4Windows");
                    if (tasker != null)
                    {
                        ts.RootFolder.DeleteTask("RunDS4Windows");
                    }
                }
            }
        }

        private void linkCB_CheckedChanged(object sender, EventArgs e)
        {
            CheckBox linkCb = (CheckBox)sender;
            int i = Convert.ToInt32(linkCb.Tag);
            var aux = API.Aux(i);
            var cfg = API.Cfg(i);
            bool check = linkCb.Checked;
            aux.LinkedProfileCheck = check;
            DS4Device device = Program.RootHub(i).DS4Controller;
            if (device?.IsSynced ?? false)
            {
                if (check)
                {
                    if (device.isValidSerial())
                    {
                        Config.SetLinkedProfile(device.MacAddress, cfg.ProfilePath);
                    }
                }
                else
                {
                    Config.RemoveLinkedProfile(device.MacAddress);
                    cfg.ProfilePath = cfg.OlderProfilePath;
                    int profileIndex = cbs[i].FindString(cfg.ProfilePath);
                    if (profileIndex >= 0)
                    {
                        cbs[i].SelectedIndex = profileIndex;
                    }
                }

                Config.SaveLinkedProfiles();
            }
        }

        private void exportLogTxtBtn_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog {
                AddExtension = true,
                DefaultExt = ".txt",
                Filter = "Text Documents (*.txt)|*.txt",
                Title = "Select Export File",
                InitialDirectory = API.AppDataPath
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string outputFile = dialog.FileName;
                List < string > outputLines = new List<string>();
                ListViewItem item = null;
                for (int i = 0, len = lvDebug.Items.Count; i < len; i++)
                {
                    item = lvDebug.Items[i];
                    outputLines.Add(item.SubItems[0].Text + ": " + item.SubItems[1].Text);
                }

                try
                {
                    StreamWriter stream = new StreamWriter(outputFile);
                    string line = string.Empty;
                    for (int i = 0, len = outputLines.Count; i < len; i++)
                    {
                        line = outputLines[i];
                        stream.WriteLine(line);
                    }
                    stream.Close();
                }
                catch { }
            }
        }

        private void languagePackComboBox1_SelectedValueChanged(object sender, EventArgs e)
        {
            string newValue = ((DS4Forms.LanguagePackComboBox)sender).SelectedValue.ToString();
            if (newValue != Config.UseLang)
            {
                Config.UseLang = newValue;
                Config.Save();
                MessageBox.Show(Properties.Resources.LanguagePackApplyRestartRequired, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OpenProgramFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process temp = new Process();
            temp.StartInfo.FileName = "explorer.exe";
            temp.StartInfo.Arguments = @"/select, " + Assembly.GetExecutingAssembly().Location;
            try { temp.Start(); }
            catch { }
        }

        private void DiscontoolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            int i = Convert.ToInt32(item.Tag);
            DS4Device d = Program.RootHub(i).DS4Controller;
            if (d != null)
            {
                if (d.ConnectionType == ConnectionType.BT && !d.IsCharging)
                {
                    d.DisconnectBT();
                }
                else if (d.ConnectionType == ConnectionType.SONYWA && !d.IsCharging)
                {
                    d.DisconnectDongle();
                }
            }
        }

        private void MintoTaskCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            Config.MinToTaskbar = mintoTaskCheckBox.Checked;
            Config.Save();
        }

        private void CBController_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = true;
        }

        private async void CkUdpServ_CheckedChanged(object sender, EventArgs e)
        {
            bool state = ckUdpServ.Checked;
            Config.UseUDPServer = state;
            if (!state)
            {
                Program.rootHub.ChangeMotionEventStatus(state);
                await TaskRunner.Delay(100).ContinueWith((t) =>
                {
                    Program.rootHub.ChangeUDPStatus(state);
                });
            }
            else
            {
                Program.rootHub.ChangeUDPStatus(state);
                await TaskRunner.Delay(100).ContinueWith((t) =>
                {
                    Program.rootHub.ChangeMotionEventStatus(state);
                });
            }

            nUDUdpPortNum.Enabled = state;
            tBUdpListenAddress.Enabled = state;
        }

        private void NUDUdpPortNum_Leave(object sender, EventArgs e)
        {
            int curValue = (int)nUDUdpPortNum.Value;
            if (curValue != Config.UDPServerPort)
            {
                Config.UDPServerPort = curValue;
                nUDUdpPortNum.Enabled = false;
                tBUdpListenAddress.Enabled = false;
                WaitUDPPortChange();
            }
        }

        private async void WaitUDPPortChange()
        {
            await TaskRunner.Delay(100);
            if (Config.UseUDPServer)
            {
                await TaskRunner.Run(() => Program.rootHub.UseUDPPort());
                nUDUdpPortNum.Enabled = true;
                tBUdpListenAddress.Enabled = true;
            }
        }

        private void cBFlashWhenLate_CheckedChanged(object sender, EventArgs e)
        {
            Config.FlashWhenLate = cBFlashWhenLate.Checked;
            nUDLatency.Enabled = cBFlashWhenLate.Checked;
            lbMsLatency.Enabled = cBFlashWhenLate.Checked;
        }

        private void nUDLatency_ValueChanged(object sender, EventArgs e)
        {
            Config.FlashWhenLateAt = (int)Math.Round(nUDLatency.Value);
        }
    }


    //
    // Class to store autoprofile path and title data. Path and Title are pre-stored as lowercase versions (case insensitive search) to speed up IsMatch method in autoprofile timer calls.
    // AutoProfile thread monitors active processes and windows. Autoprofile search rule can define just a process path or both path and window title search keywords. 
    // Keyword syntax: xxxx = exact matach, ^xxxx = match to beginning of path or title string. xxxx$ = match to end of string. *xxxx = contains in a string search
    //
    public class ProgramPathItem
    {
        public string path;
        public string title;
        private string path_lowercase;
        private string title_lowercase;

        public ProgramPathItem(string pathStr, string titleStr)
        {
            // Initialize autoprofile search keywords (xxx_tolower). To improve performance the search keyword is pre-calculated in xxx_tolower variables,
            // so autoprofile timer thread doesn't have to create substrings/replace/tolower string instances every second over and over again.
            if (!string.IsNullOrEmpty(pathStr))
            {
                path = pathStr;
                path_lowercase = path.ToLower().Replace('/', '\\');

                if (path.Length >= 2)
                {
                    if (path[0] == '^') path_lowercase = path_lowercase.Substring(1);
                    else if (path[path.Length - 1] == '$') path_lowercase = path_lowercase.Substring(0, path_lowercase.Length - 1);
                    else if (path[0] == '*') path_lowercase = path_lowercase.Substring(1);
                }
            }
            else path = path_lowercase = String.Empty;

            if (!string.IsNullOrEmpty(titleStr))
            {
                title = titleStr;
                title_lowercase = title.ToLower();

                if (title.Length >= 2)
                {
                    if (title[0] == '^') title_lowercase = title_lowercase.Substring(1);
                    else if (title[title.Length - 1] == '$') title_lowercase = title_lowercase.Substring(0, title_lowercase.Length - 1);
                    else if (title[0] == '*') title_lowercase = title_lowercase.Substring(1);
                }
            }
            else title = title_lowercase = String.Empty;
        }

        public bool IsMatch(string searchPath, string searchTitle)
        {
            bool bPathMatched = true;
            bool bTitleMwatched = true;

            if (!String.IsNullOrEmpty(path_lowercase))
            {
                bPathMatched = (path_lowercase == searchPath
                    || (path[0] == '^' && searchPath.StartsWith(path_lowercase))
                    || (path[path.Length - 1] == '$' && searchPath.EndsWith(path_lowercase))
                    || (path[0] == '*' && searchPath.Contains(path_lowercase))
                   );
            }

            if (bPathMatched && !String.IsNullOrEmpty(title_lowercase))
            {
                bTitleMwatched = (title_lowercase == searchTitle
                    || (title[0] == '^' && searchTitle.StartsWith(title_lowercase))
                    || (title[title.Length - 1] == '$' && searchTitle.EndsWith(title_lowercase))
                    || (title[0] == '*' && searchTitle.Contains(title_lowercase))
                   );
            }

            // If both path and title defined in autoprofile entry then do AND condition (ie. both path and title should match)
            return bPathMatched && bTitleMwatched;
        }
    }
}
