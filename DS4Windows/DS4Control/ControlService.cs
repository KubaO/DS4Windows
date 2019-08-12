using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Media;
using System.Threading.Tasks;
using static DS4Windows.Global;
using System.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace DS4Windows
{
    public partial class DeviceControlService
    {
        // Per-device
        private readonly int devIndex;
        private readonly ControlService ctlSvc;
        internal readonly IDeviceConfig cfg;
        internal readonly IDeviceAuxiliaryConfig aux;
        private readonly DS4LightBar lightBar;
        private readonly Mapping mapping;

        public DS4Device DS4Controller;
        public Mouse touchPad;
        private DS4State MappedState;
        private DS4State CurrentState;
        private DS4State PreviousState;
        private DS4State TempState;
        public DS4StateExposed ExposedState;

        bool buttonsdown = false;
        bool held;

        int oldmouse = -1;
        public OutputDevice outputDevice = null;

#if false
        public Xbox360Controller x360control = null;
        private Xbox360Report x360report = new Xbox360Report();
#endif

        private class X360Data
        {
            public byte[] Report = new byte[28];
            public byte[] Rumble = new byte[8];
        }

        private X360Data processingData;
        private byte[] udpOutBuffer = new byte[100];
    }

    public partial class ControlService
    {
        // Global
        public ViGEmClient vigemTestClient = null;
        private bool running = false;
        public bool recordingMacro = false;
        public event EventHandler<DebugEventArgs> Debug = null;
        Thread tempThread;

        public static readonly List<string> affectedDevs = new List<string>() {
            @"HID\VID_054C&PID_05C4",
            @"HID\VID_054C&PID_09CC&MI_03",
            @"HID\VID_054C&PID_0BA0&MI_03",
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&05c4",
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc",
        };

        public bool suspending;

        //SoundPlayer sp = new SoundPlayer();
        internal UdpServer _udpServer;

        private DeviceControlService[] ctlSvcs;
        public DeviceControlService CtlSvc(int index) { return ctlSvcs[index]; }
        public IEnumerable<DeviceControlService> CtlServices { get => ctlSvcs; }

        static void GetPadDetailForIdx(int index, ref DualShockPadMeta meta) 
            => Program.RootHub(index).GetPadDetail(ref meta);
    }

    public partial class DeviceControlService
    {
        internal void GetPadDetail(ref DualShockPadMeta meta)
        {
            //meta = new DualShockPadMeta();
            meta.PadId = (byte) devIndex;
            meta.Model = DsModel.DS4;

            var d = DS4Controller;
            if (d == null)
            {
                meta.PadMacAddress = null;
                meta.PadState = DsState.Disconnected;
                meta.ConnectionType = DsConnection.None;
                meta.Model = DsModel.None;
                meta.BatteryStatus = 0;
                meta.IsActive = false;
                return;
                //return meta;
            }

            bool isValidSerial = false;
            if (true || d.isValidSerial())
            {
                string stringMac = d.getMacAddress();
                if (!string.IsNullOrEmpty(stringMac))
                {
                    stringMac = string.Join("", stringMac.Split(':'));
                    //stringMac = stringMac.Replace(":", "").Trim();
                    meta.PadMacAddress = System.Net.NetworkInformation.PhysicalAddress.Parse(stringMac);
                    isValidSerial = d.isValidSerial();
                }
            }

            if (!isValidSerial)
            {
                //meta.PadMacAddress = null;
                meta.PadState = DsState.Disconnected;
            }
            else
            {
                if (d.isSynced() || d.IsAlive())
                    meta.PadState = DsState.Connected;
                else
                    meta.PadState = DsState.Reserved;
            }

            meta.ConnectionType = (d.getConnectionType() == ConnectionType.USB) ? DsConnection.Usb : DsConnection.Bluetooth;
            meta.IsActive = !d.isDS4Idle();

            if (d.isCharging() && d.getBattery() >= 100)
                meta.BatteryStatus = DsBattery.Charged;
            else
            {
                if (d.getBattery() >= 95)
                    meta.BatteryStatus = DsBattery.Full;
                else if (d.getBattery() >= 70)
                    meta.BatteryStatus = DsBattery.High;
                else if (d.getBattery() >= 50)
                    meta.BatteryStatus = DsBattery.Medium;
                else if (d.getBattery() >= 20)
                    meta.BatteryStatus = DsBattery.Low;
                else if (d.getBattery() >= 5)
                    meta.BatteryStatus = DsBattery.Dying;
                else
                    meta.BatteryStatus = DsBattery.None;
            }

            //return meta;
        }
    }

    public partial class ControlService
    {
        private object busThrLck = new object();
        private bool busThrRunning = false;
        private Queue<Action> busEvtQueue = new Queue<Action>();
        private object busEvtQueueLock = new object();

        public ControlService()
        {
            var ctlSvc_ = this;
            ctlSvcs = API.makePerDevice(i => new DeviceControlService(ctlSvc_, i));

            //sp.Stream = Properties.Resources.EE;
            // Cause thread affinity to not be tied to main GUI thread
            tempThread = new Thread(() => {
                //_udpServer = new UdpServer(GetPadDetailForIdx);
                busThrRunning = true;

                while (busThrRunning)
                {
                    lock (busEvtQueueLock)
                    {
                        Action tempAct = null;
                        for (int actInd = 0, actLen = busEvtQueue.Count; actInd < actLen; actInd++)
                        {
                            tempAct = busEvtQueue.Dequeue();
                            tempAct.Invoke();
                        }
                    }

                    lock (busThrLck)
                        Monitor.Wait(busThrLck);
                }
            });
            tempThread.Priority = ThreadPriority.Normal;
            tempThread.IsBackground = true;
            tempThread.Start();
            //while (_udpServer == null)
            //{
            //    Thread.SpinWait(500);
            //}

            if (DeviceDetection.IsHidGuardianInstalled())
            {
                ProcessStartInfo startInfo =
                    new ProcessStartInfo($"{API.ExePath}\\HidGuardHelper.exe");
                startInfo.Verb = "runas";
                startInfo.Arguments = Process.GetCurrentProcess().Id.ToString();
                startInfo.WorkingDirectory = API.ExePath;
                try
                {
                    Process tempProc = Process.Start(startInfo);
                    tempProc.Dispose();
                }
                catch { }
            }
        }
    }

    public partial class DeviceControlService
    {
        public DeviceControlService(ControlService ctlSvc, int devIndex)
        {
            this.devIndex = devIndex;
            this.ctlSvc = ctlSvc;
            cfg = API.Cfg(devIndex);
            aux = API.Aux(devIndex);
            lightBar = API.Bar(devIndex);
            mapping = API.Mapping(devIndex);

            processingData = new X360Data();
            MappedState = new DS4State();
            CurrentState = new DS4State();
            TempState = new DS4State();
            PreviousState = new DS4State();
            ExposedState = new DS4StateExposed(CurrentState);
        }
    }

    public partial class ControlService
    {

        private void TestQueueBus(Action temp)
        {
            lock (busEvtQueueLock)
            {
                busEvtQueue.Enqueue(temp);
            }

            lock (busThrLck)
                Monitor.Pulse(busThrLck);
        }

        public void ChangeUDPStatus(bool state, bool openPort=true)
        {
            if (state && _udpServer == null)
            {
                udpChangeStatus = true;
                TestQueueBus(() =>
                {
                    _udpServer = new UdpServer(GetPadDetailForIdx);
                    if (openPort)
                    {
                        // Change thread affinity of object to have normal priority
                        Task.Run(() =>
                        {
                            var UDP_SERVER_PORT = API.Config.UDPServerPort;
                            var UDP_SERVER_LISTEN_ADDRESS = API.Config.UDPServerListenAddress;

                            try
                            {
                                _udpServer.Start(UDP_SERVER_PORT, UDP_SERVER_LISTEN_ADDRESS);
                                LogDebug($"UDP server listening on address {UDP_SERVER_LISTEN_ADDRESS} port {UDP_SERVER_PORT}");
                            }
                            catch (System.Net.Sockets.SocketException ex)
                            {
                                var errMsg = String.Format("Couldn't start UDP server on address {0}:{1}, outside applications won't be able to access pad data ({2})", UDP_SERVER_LISTEN_ADDRESS, UDP_SERVER_PORT, ex.SocketErrorCode);

                                LogDebug(errMsg, true);
                                AppLogger.LogToTray(errMsg, true, true);
                            }
                        }).Wait();
                    }

                    udpChangeStatus = false;
                });
            }
            else if (!state && _udpServer != null)
            {
                TestQueueBus(() =>
                {
                    udpChangeStatus = true;
                    _udpServer.Stop();
                    _udpServer = null;
                    AppLogger.LogToGui("Closed UDP server", false);
                    udpChangeStatus = false;
                });
            }
        }

        public void ChangeMotionEventStatus(bool state)
        {
            IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
            if (state)
            {
                foreach (DS4Device dev in devices)
                {
                    dev.queueEvent(() =>
                    {
                        dev.Report += dev.MotionEvent;
                    });
                }
            }
            else
            {
                foreach (DS4Device dev in devices)
                {
                    dev.queueEvent(() =>
                    {
                        dev.Report -= dev.MotionEvent;
                    });
                }
            }
        }

        private bool udpChangeStatus = false;
        public bool changingUDPPort = false;
        public async void UseUDPPort()
        {
            changingUDPPort = true;
            IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
            foreach (DS4Device dev in devices)
            {
                dev.queueEvent(() =>
                {
                    dev.Report -= dev.MotionEvent;
                });
            }

            await Task.Delay(100);

            var UDP_SERVER_PORT = API.Config.UDPServerPort;
            var UDP_SERVER_LISTEN_ADDRESS = API.Config.UDPServerListenAddress;

            try
            {
                _udpServer.Start(UDP_SERVER_PORT, UDP_SERVER_LISTEN_ADDRESS);
                foreach (DS4Device dev in devices)
                {
                    dev.queueEvent(() =>
                    {
                        dev.Report += dev.MotionEvent;
                    });
                }
                LogDebug($"UDP server listening on address {UDP_SERVER_LISTEN_ADDRESS} port {UDP_SERVER_PORT}");
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                var errMsg = String.Format("Couldn't start UDP server on address {0}:{1}, outside applications won't be able to access pad data ({2})", UDP_SERVER_LISTEN_ADDRESS, UDP_SERVER_PORT, ex.SocketErrorCode);

                LogDebug(errMsg, true);
                AppLogger.LogToTray(errMsg, true, true);
            }

            changingUDPPort = false;
        }

        internal void WarnExclusiveModeFailure(DS4Device device)
        {
            if (DS4Devices.isExclusiveMode && !device.isExclusive())
            {
                string message = Properties.Resources.CouldNotOpenDS4.Replace("*Mac address*", device.getMacAddress()) + " " +
                    Properties.Resources.QuitOtherPrograms;
                LogDebug(message, true);
                AppLogger.LogToTray(message, true);
            }
        }

        private void startViGEm()
        {
            tempThread = new Thread(() => { try { vigemTestClient = new ViGEmClient(); } catch { } });
            tempThread.Priority = ThreadPriority.AboveNormal;
            tempThread.IsBackground = true;
            tempThread.Start();
            while (tempThread.IsAlive)
            {
                Thread.SpinWait(500);
            }
        }

        private void stopViGEm()
        {
            if (tempThread != null)
            {
                tempThread.Interrupt();
                tempThread.Join();
                tempThread = null;
            }

            if (vigemTestClient != null)
            {
                vigemTestClient.Dispose();
                vigemTestClient = null;
            }
        }

        internal SynchronizationContext uiContext = null;
        public bool Start(object tempui, bool showlog = true)
        {
            startViGEm();
            if (vigemTestClient != null)
            //if (x360Bus.Open() && x360Bus.Start())
            {
                if (showlog)
                    LogDebug(Properties.Resources.Starting);

                LogDebug($"Connection to ViGEmBus {API.ViGEmBusVersion} established");

                DS4Devices.isExclusiveMode = API.Config.UseExclusiveMode;
                uiContext = tempui as SynchronizationContext;
                if (showlog)
                {
                    LogDebug(Properties.Resources.SearchingController);
                    LogDebug(DS4Devices.isExclusiveMode ? Properties.Resources.UsingExclusive : Properties.Resources.UsingShared);
                }

                if (API.Config.UseUDPServer && _udpServer == null)
                {
                    ChangeUDPStatus(true, false);
                    while (udpChangeStatus == true)
                    {
                        Thread.SpinWait(500);
                    }
                }

                try
                {
                    DS4Devices.findControllers();
                    IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
                    DS4LightBar.defaultLight = false;

                    int i = 0;
                    foreach (DS4Device device in devices)
                    {
                        ctlSvcs[i].Start(device, showlog);
                        if (i >= 4) // out of Xinput devices!
                            break;
                    }
                }
                catch (Exception e)
                {
                    LogDebug(e.Message);
                    AppLogger.LogToTray(e.Message);
                }

                running = true;

                if (_udpServer != null)
                {
                    //var UDP_SERVER_PORT = 26760;
                    var UDP_SERVER_PORT = API.Config.UDPServerPort;
                    var UDP_SERVER_LISTEN_ADDRESS = API.Config.UDPServerListenAddress;

                    try
                    {
                        _udpServer.Start(UDP_SERVER_PORT, UDP_SERVER_LISTEN_ADDRESS);
                        LogDebug($"UDP server listening on address {UDP_SERVER_LISTEN_ADDRESS} port {UDP_SERVER_PORT}");
                    }
                    catch (System.Net.Sockets.SocketException ex)
                    {
                        var errMsg = String.Format("Couldn't start UDP server on address {0}:{1}, outside applications won't be able to access pad data ({2})", UDP_SERVER_LISTEN_ADDRESS, UDP_SERVER_PORT, ex.SocketErrorCode);

                        LogDebug(errMsg, true);
                        AppLogger.LogToTray(errMsg, true, true);
                    }
                }
            }
            else
            {
                string logMessage = "Could not connect to ViGEmBus. Please check the status of the System device in Device Manager and if Visual C++ 2017 Redistributable is installed.";
                LogDebug(logMessage);
                AppLogger.LogToTray(logMessage);
            }

            API.RunHotPlug = true;
            return true;
        }
    }

    public partial class DeviceControlService
    {
        private void WarnExclusiveModeFailure(DS4Device device) => ctlSvc.WarnExclusiveModeFailure(device);
        public ViGEmClient vigemTestClient { get => ctlSvc.vigemTestClient;  }
        private SynchronizationContext uiContext { get => ctlSvc.uiContext;  }

        internal void Start(DS4Device device, bool showlog)
        {
            if (showlog)
                LogDebug(Properties.Resources.FoundController + device.getMacAddress() + " (" + device.getConnectionType() + ")");

            Task task = new Task(() => {
                Thread.Sleep(5);
                WarnExclusiveModeFailure(device);
            });
            task.Start();

            DS4Controller = device;
            // FIXME
            device.Removal += this.On_DS4Removal;
            device.Removal += DS4Devices.On_Removal;
            device.SyncChange += this.On_SyncChange;
            device.SyncChange += DS4Devices.UpdateSerial;
            device.SerialChange += this.On_SerialChange;

            touchPad = new Mouse(devIndex, device);

            if (!aux.UseTempProfile)
            {
                if (device.isValidSerial() && API.Config.ContainsLinkedProfile(device.getMacAddress()))
                {
                    cfg.ProfilePath = API.Config.GetLinkedProfile(device.getMacAddress());
                }
                else
                {
                    cfg.ProfilePath = cfg.OlderProfilePath;
                }

                cfg.LoadProfile(false, this, false, false);
            }

            device.LightBarColor = cfg.MainColor;

            if (!cfg.DInputOnly && device.isSynced())
            {
                aux.UseDInputOnly = false;

                OutContType contType = cfg.OutputDevType;
                if (contType == OutContType.X360)
                {
                    LogDebug("Plugging in X360 Controller #" + (devIndex + 1));
                    Xbox360OutDevice tempXbox = new Xbox360OutDevice(vigemTestClient);
                    outputDevice = tempXbox;

                    tempXbox.cont.FeedbackReceived += (sender, args) => {
                        SetDevRumble(device, args.LargeMotor, args.SmallMotor);
                    };

                    tempXbox.Connect();
                    LogDebug("X360 Controller #" + (devIndex + 1) + " connected");
                }
                else if (contType == OutContType.DS4)
                {
                    LogDebug("Plugging in DS4 Controller #" + (devIndex + 1));
                    DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
                    outputDevice = tempDS4;

                    tempDS4.cont.FeedbackReceived += (sender, args) => {
                        SetDevRumble(device, args.LargeMotor, args.SmallMotor);
                    };

                    tempDS4.Connect();
                    LogDebug("DS4 Controller #" + (devIndex + 1) + " connected");
                }
            }
            else
            {
                aux.UseDInputOnly = true;
            }

            device.Report += (sender, e) => {
                this.On_Report(sender, e);
            };

            var tempIdx = devIndex;
            DS4Device.ReportHandler<EventArgs> tempEvnt = (sender, args) => {
                DualShockPadMeta padDetail = new DualShockPadMeta();
                GetPadDetail(ref padDetail);
                ctlSvc._udpServer.NewReportIncoming(ref padDetail, this.CurrentState, this.udpOutBuffer);
            };
            device.MotionEvent = tempEvnt;

            if (ctlSvc._udpServer != null)
            {
                device.Report += tempEvnt;
            }

            TouchPadOn(device);
            CheckProfileOptions(device, true);
            device.StartUpdate();
            //string filename = ProfileExePath[ind];
            //ind++;
            if (showlog)
            {
                if (File.Exists($"{API.AppDataPath}\\Profiles\\{cfg.ProfilePath}.xml"))
                {
                    string prolog = Properties.Resources.UsingProfile.Replace("*number*", (devIndex + 1).ToString()).Replace("*Profile name*", cfg.ProfilePath);
                    LogDebug(prolog);
                    AppLogger.LogToTray(prolog);
                }
                else
                {
                    string prolog = Properties.Resources.NotUsingProfile.Replace("*number*", (devIndex + 1).ToString());
                    LogDebug(prolog);
                    AppLogger.LogToTray(prolog);
                }
            }
        }
    }

    public partial class ControlService
    {
        public bool Stop(bool showlog = true)
        {
            if (running)
            {
                running = false;
                API.RunHotPlug = false;

                if (showlog)
                    LogDebug(Properties.Resources.StoppingX360);

                LogDebug("Closing connection to ViGEmBus");

                foreach (var devCtlSvc in ctlSvcs)
                {
                    devCtlSvc.Stop();
                }

                if (showlog)
                    LogDebug(Properties.Resources.StoppingDS4);

                DS4Devices.stopControllers();

                if (_udpServer != null)
                    ChangeUDPStatus(false);
                //_udpServer.Stop();

                if (showlog)
                    LogDebug(Properties.Resources.StoppedDS4Windows);

                stopViGEm();
            }

            API.RunHotPlug = false;
            return true;
        }
    }

    public partial class DeviceControlService
    {
        internal void Stop()
        {
            DS4Device tempDevice = DS4Controller;
            if (tempDevice != null)
            {
                if ((API.Config.DisconnectBTAtStop && !tempDevice.isCharging()) || ctlSvc.suspending)
                {
                    if (tempDevice.getConnectionType() == ConnectionType.BT)
                    {
                        tempDevice.StopUpdate();
                        tempDevice.DisconnectBT(true);
                    }
                    else if (tempDevice.getConnectionType() == ConnectionType.SONYWA)
                    {
                        tempDevice.StopUpdate();
                        tempDevice.DisconnectDongle(true);
                    }
                    else
                    {
                        tempDevice.StopUpdate();
                    }
                }
                else
                {
                    lightBar.forcedLight = false;
                    lightBar.forcedFlash = 0;
                    DS4LightBar.defaultLight = true;
                    lightBar.updateLightBar(DS4Controller);
                    tempDevice.IsRemoved = true;
                    tempDevice.StopUpdate();
                    DS4Devices.RemoveDevice(tempDevice);
                    Thread.Sleep(50);
                }

                CurrentState.Battery = PreviousState.Battery = 0; // Reset for the next connection's initial status change.
                outputDevice?.Disconnect();
                outputDevice = null;
                aux.UseDInputOnly = true;
                DS4Controller = null;
                touchPad = null;
            }

            lag = false;
            inWarnMonitor = false;
        }
    }

    public partial class ControlService
    {
        public bool HotPlug()
        {
            if (running)
            {
                DS4Devices.findControllers();
                IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();

                foreach (DS4Device device in devices)
                {
                    if (device.isDisconnectingStatus())
                        continue;

                    if (((Func<bool>) delegate {
                        foreach (var dCS in ctlSvcs)
                        {
                            if (dCS.DS4Controller != null &&
                                dCS.DS4Controller.getMacAddress() == device.getMacAddress())
                                return true;
                        }

                        return false;
                    })())
                    {
                        continue;
                    }

                    foreach (DeviceControlService dCS in ctlSvcs)
                    {
                        if (dCS.HotPlug(device)) break;
                    }
                }
            }

            return true;
        }
    }

    public partial class DeviceControlService
    {
        internal bool HotPlug(DS4Device device)
        {
            if (DS4Controller == null)
            {
                LogDebug(Properties.Resources.FoundController + device.getMacAddress() + " (" + device.getConnectionType() + ")");
                Task task = new Task(() => {
                    Thread.Sleep(5);
                    WarnExclusiveModeFailure(device);
                });
                task.Start();
                DS4Controller = device;
                device.Removal += this.On_DS4Removal;
                device.Removal += DS4Devices.On_Removal;
                device.SyncChange += this.On_SyncChange;
                device.SyncChange += DS4Devices.UpdateSerial;
                device.SerialChange += this.On_SerialChange;

                touchPad = new Mouse(devIndex, device);

                if (!aux.UseTempProfile)
                {
                    if (device.isValidSerial() && API.Config.ContainsLinkedProfile(device.getMacAddress()))
                    {
                        cfg.ProfilePath = API.Config.GetLinkedProfile(device.getMacAddress());
                    }
                    else
                    {
                        cfg.ProfilePath = cfg.OlderProfilePath;
                    }

                    cfg.LoadProfile(false, this, false, false);
                }

                device.LightBarColor = cfg.MainColor;

                device.Report += (sender, e) => {
                    this.On_Report(sender, e);
                };

                DS4Device.ReportHandler<EventArgs> tempEvnt = (sender, args) => {
                    DualShockPadMeta padDetail = new DualShockPadMeta();
                    GetPadDetail(ref padDetail);
                    ctlSvc._udpServer.NewReportIncoming(ref padDetail, this.CurrentState, this.udpOutBuffer);
                };
                device.MotionEvent = tempEvnt;

                if (ctlSvc._udpServer != null)
                {
                    device.Report += tempEvnt;
                }

                if (!cfg.DInputOnly && device.isSynced())
                {
                    aux.UseDInputOnly = false;
                    OutContType contType = cfg.OutputDevType;
                    if (contType == OutContType.X360)
                    {
                        LogDebug("Plugging in X360 Controller #" + (devIndex + 1));
                        Xbox360OutDevice tempXbox = new Xbox360OutDevice(vigemTestClient);
                        outputDevice = tempXbox;
                        tempXbox.cont.FeedbackReceived += (sender, args) => {
                            SetDevRumble(device, args.LargeMotor, args.SmallMotor);
                        };

                        tempXbox.Connect();
                        LogDebug("X360 Controller #" + (devIndex + 1) + " connected");
                    }
                    else if (contType == OutContType.DS4)
                    {
                        LogDebug("Plugging in DS4 Controller #" + (devIndex + 1));
                        DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
                        outputDevice = tempDS4;
                        tempDS4.cont.FeedbackReceived += (sender, args) => {
                            SetDevRumble(device, args.LargeMotor, args.SmallMotor);
                        };

                        tempDS4.Connect();
                        LogDebug("DS4 Controller #" + (devIndex + 1) + " connected");
                    }

                }
                else
                {
                    aux.UseDInputOnly = true;
                }

                TouchPadOn(device);
                CheckProfileOptions(device);
                device.StartUpdate();

                //string filename = Path.GetFileName(ProfileExePath[Index]);
                if (File.Exists($"{API.AppDataPath}\\Profiles\\{cfg.ProfilePath}.xml"))
                {
                    string prolog = Properties.Resources.UsingProfile.Replace("*number*", (devIndex + 1).ToString()).Replace("*Profile name*", cfg.ProfilePath);
                    LogDebug(prolog);
                    AppLogger.LogToTray(prolog);
                }
                else
                {
                    string prolog = Properties.Resources.NotUsingProfile.Replace("*number*", (devIndex + 1).ToString());
                    LogDebug(prolog);
                    AppLogger.LogToTray(prolog);
                }

                return true;
            }

            return false;
        }

#if false
        private void testNewReport(ref Xbox360Report xboxreport, DS4State state,
            int device)
        {
            Xbox360Buttons tempButtons = 0;

            unchecked
            {
                if (state.Share) tempButtons |= Xbox360Buttons.Back;
                if (state.L3) tempButtons |= Xbox360Buttons.LeftThumb;
                if (state.R3) tempButtons |= Xbox360Buttons.RightThumb;
                if (state.Options) tempButtons |= Xbox360Buttons.Start;

                if (state.DpadUp) tempButtons |= Xbox360Buttons.Up;
                if (state.DpadRight) tempButtons |= Xbox360Buttons.Right;
                if (state.DpadDown) tempButtons |= Xbox360Buttons.Down;
                if (state.DpadLeft) tempButtons |= Xbox360Buttons.Left;

                if (state.L1) tempButtons |= Xbox360Buttons.LeftShoulder;
                if (state.R1) tempButtons |= Xbox360Buttons.RightShoulder;

                if (state.Triangle) tempButtons |= Xbox360Buttons.Y;
                if (state.Circle) tempButtons |= Xbox360Buttons.B;
                if (state.Cross) tempButtons |= Xbox360Buttons.A;
                if (state.Square) tempButtons |= Xbox360Buttons.X;
                if (state.PS) tempButtons |= Xbox360Buttons.Guide;
                xboxreport.SetButtonsFull(tempButtons);
            }

            xboxreport.LeftTrigger = state.L2;
            xboxreport.RightTrigger = state.R2;

            SASteeringWheelEmulationAxisType steeringWheelMappedAxis = Global.GetSASteeringWheelEmulationAxis(device);
            switch (steeringWheelMappedAxis)
            {
                case SASteeringWheelEmulationAxisType.None:
                    xboxreport.LeftThumbX = AxisScale(state.LX, false);
                    xboxreport.LeftThumbY = AxisScale(state.LY, true);
                    xboxreport.RightThumbX = AxisScale(state.RX, false);
                    xboxreport.RightThumbY = AxisScale(state.RY, true);
                    break;

                case SASteeringWheelEmulationAxisType.LX:
                    xboxreport.LeftThumbX = (short)state.SASteeringWheelEmulationUnit;
                    xboxreport.LeftThumbY = AxisScale(state.LY, true);
                    xboxreport.RightThumbX = AxisScale(state.RX, false);
                    xboxreport.RightThumbY = AxisScale(state.RY, true);
                    break;

                case SASteeringWheelEmulationAxisType.LY:
                    xboxreport.LeftThumbX = AxisScale(state.LX, false);
                    xboxreport.LeftThumbY = (short)state.SASteeringWheelEmulationUnit;
                    xboxreport.RightThumbX = AxisScale(state.RX, false);
                    xboxreport.RightThumbY = AxisScale(state.RY, true);
                    break;

                case SASteeringWheelEmulationAxisType.RX:
                    xboxreport.LeftThumbX = AxisScale(state.LX, false);
                    xboxreport.LeftThumbY = AxisScale(state.LY, true);
                    xboxreport.RightThumbX = (short)state.SASteeringWheelEmulationUnit;
                    xboxreport.RightThumbY = AxisScale(state.RY, true);
                    break;

                case SASteeringWheelEmulationAxisType.RY:
                    xboxreport.LeftThumbX = AxisScale(state.LX, false);
                    xboxreport.LeftThumbY = AxisScale(state.LY, true);
                    xboxreport.RightThumbX = AxisScale(state.RX, false);
                    xboxreport.RightThumbY = (short)state.SASteeringWheelEmulationUnit;
                    break;

                case SASteeringWheelEmulationAxisType.L2R2:
                    xboxreport.LeftTrigger = xboxreport.RightTrigger = 0;
                    if (state.SASteeringWheelEmulationUnit >= 0) xboxreport.LeftTrigger = (Byte)state.SASteeringWheelEmulationUnit;
                    else xboxreport.RightTrigger = (Byte)state.SASteeringWheelEmulationUnit;
                    goto case SASteeringWheelEmulationAxisType.None;

                case SASteeringWheelEmulationAxisType.VJoy1X:
                case SASteeringWheelEmulationAxisType.VJoy2X:
                    DS4Windows.VJoyFeeder.vJoyFeeder.FeedAxisValue(state.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, DS4Windows.VJoyFeeder.HID_USAGES.HID_USAGE_X);
                    goto case SASteeringWheelEmulationAxisType.None;

                case SASteeringWheelEmulationAxisType.VJoy1Y:
                case SASteeringWheelEmulationAxisType.VJoy2Y:
                    DS4Windows.VJoyFeeder.vJoyFeeder.FeedAxisValue(state.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, DS4Windows.VJoyFeeder.HID_USAGES.HID_USAGE_Y);
                    goto case SASteeringWheelEmulationAxisType.None;

                case SASteeringWheelEmulationAxisType.VJoy1Z:
                case SASteeringWheelEmulationAxisType.VJoy2Z:
                    DS4Windows.VJoyFeeder.vJoyFeeder.FeedAxisValue(state.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, DS4Windows.VJoyFeeder.HID_USAGES.HID_USAGE_Z);
                    goto case SASteeringWheelEmulationAxisType.None;

                default:
                    // Should never come here but just in case use the NONE case as default handler....
                    goto case SASteeringWheelEmulationAxisType.None;
            }
        }
#endif

#if false
        private short AxisScale(Int32 Value, Boolean Flip)
        {
            unchecked
            {
                Value -= 0x80;

                //float temp = (Value - (-128)) / (float)inputResolution;
                float temp = (Value - (-128)) * reciprocalInputResolution;
                if (Flip) temp = (temp - 0.5f) * -1.0f + 0.5f;

                return (short)(temp * outputResolution + (-32768));
            }
        }
#endif

        private void CheckProfileOptions(DS4Device device, bool startUp = false)
        {
            device.setIdleTimeout(cfg.IdleDisconnectTimeout);
            device.setBTPollRate(cfg.BTPollRate);
            touchPad.ResetTrackAccel(cfg.TrackballFriction);

            if (!startUp)
            {
                CheckLauchProfileOption(device);
            }
        }

        private void CheckLauchProfileOption(DS4Device device)
        {
            string programPath = cfg.LaunchProgram;
            if (programPath != string.Empty)
            {
                System.Diagnostics.Process[] localAll = System.Diagnostics.Process.GetProcesses();
                bool procFound = false;
                for (int procInd = 0, procsLen = localAll.Length; !procFound && procInd < procsLen; procInd++)
                {
                    try
                    {
                        string temp = localAll[procInd].MainModule.FileName;
                        if (temp == programPath)
                        {
                            procFound = true;
                        }
                    }
                    // Ignore any process for which this information
                    // is not exposed
                    catch { }
                }

                if (!procFound)
                {
                    Task processTask = new Task(() => {
                        Thread.Sleep(5000);
                        System.Diagnostics.Process tempProcess = new System.Diagnostics.Process();
                        tempProcess.StartInfo.FileName = programPath;
                        tempProcess.StartInfo.WorkingDirectory = new FileInfo(programPath).Directory.ToString();
                        //tempProcess.StartInfo.UseShellExecute = false;
                        try { tempProcess.Start(); }
                        catch { }
                    });

                    processTask.Start();
                }
            }
        }

        public void TouchPadOn(DS4Device device)
        {
            Mouse tPad = touchPad;
            //ITouchpadBehaviour tPad = touchPad[ind];
            device.Touchpad.TouchButtonDown += tPad.touchButtonDown;
            device.Touchpad.TouchButtonUp += tPad.touchButtonUp;
            device.Touchpad.TouchesBegan += tPad.touchesBegan;
            device.Touchpad.TouchesMoved += tPad.touchesMoved;
            device.Touchpad.TouchesEnded += tPad.touchesEnded;
            device.Touchpad.TouchUnchanged += tPad.touchUnchanged;
            //device.Touchpad.PreTouchProcess += delegate { touchPad[ind].populatePriorButtonStates(); };
            device.Touchpad.PreTouchProcess += (sender, args) => { touchPad.populatePriorButtonStates(); };
            device.SixAxis.SixAccelMoved += tPad.sixaxisMoved;
            //LogDebug("Touchpad mode for " + device.MacAddress + " is now " + tmode.ToString());
            //Log.LogToTray("Touchpad mode for " + device.MacAddress + " is now " + tmode.ToString());
        }

        public string getDS4ControllerInfo()
        {
            DS4Device d = DS4Controller;
            if (d != null)
            {
                if (!d.IsAlive())
                {
                    return Properties.Resources.Connecting;
                }

                string battery;
                if (d.isCharging())
                {
                    if (d.getBattery() >= 100)
                        battery = Properties.Resources.Charged;
                    else
                        battery = Properties.Resources.Charging.Replace("*number*", d.getBattery().ToString());
                }
                else
                {
                    battery = Properties.Resources.Battery.Replace("*number*", d.getBattery().ToString());
                }

                return d.getMacAddress() + " (" + d.getConnectionType() + "), " + battery;
                //return d.MacAddress + " (" + d.ConnectionType + "), Battery is " + battery + ", Touchpad in " + modeSwitcher[index].ToString();
            }
            else
                return string.Empty;
        }

        public string getDS4MacAddress()
        {
            DS4Device d = DS4Controller;
            if (d != null)
            {
                if (!d.IsAlive())
                {
                    return Properties.Resources.Connecting;
                }

                return d.getMacAddress();
            }
            else
                return string.Empty;
        }

        public string getShortDS4ControllerInfo()
        {
            DS4Device d = DS4Controller;
            if (d != null)
            {
                string battery;
                if (!d.IsAlive())
                    battery = "...";

                if (d.isCharging())
                {
                    if (d.getBattery() >= 100)
                        battery = Properties.Resources.Full;
                    else
                        battery = d.getBattery() + "%+";
                }
                else
                {
                    battery = d.getBattery() + "%";
                }

                return (d.getConnectionType() + " " + battery);
            }
            else
                return Properties.Resources.NoneText;
        }

        public string getDS4Battery()
        {
            DS4Device d = DS4Controller;
            if (d != null)
            {
                string battery;
                if (!d.IsAlive())
                    battery = "...";

                if (d.isCharging())
                {
                    if (d.getBattery() >= 100)
                        battery = Properties.Resources.Full;
                    else
                        battery = d.getBattery() + "%+";
                }
                else
                {
                    battery = d.getBattery() + "%";
                }

                return battery;
            }
            else
                return Properties.Resources.NA;
        }

        public string getDS4Status()
        {
            DS4Device d = DS4Controller;
            if (d != null)
            {
                return d.getConnectionType() + "";
            }
            else
                return Properties.Resources.NoneText;
        }

        protected void On_SerialChange(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device) sender;
            Debug.Assert(DS4Controller == null || device == DS4Controller);

            if (DS4Controller != null)
            {
                // FIXME: this check may be unnecessary
                OnDeviceSerialChange(this, devIndex, device.getMacAddress());
            }
        }

        protected void On_SyncChange(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device) sender;
            Debug.Assert(DS4Controller == null || device == DS4Controller);

            if (DS4Controller != null)
            {
                // FIXME: this check may be unnecessary
                bool synced = device.isSynced();

                if (!synced)
                {
                    if (!aux.UseDInputOnly)
                    {
                        string tempType = outputDevice.GetDeviceType();
                        outputDevice.Disconnect();
                        outputDevice = null;
                        aux.UseDInputOnly = true;
                        LogDebug($"{tempType} Controller #{devIndex + 1} unplugged");
                    }
                }
                else
                {
                    if (!aux.UseDInputOnly)
                    {
                        OutContType conType = cfg.OutputDevType;
                        if (conType == OutContType.X360)
                        {
                            LogDebug("Plugging in X360 Controller #" + (devIndex + 1));
                            Xbox360OutDevice tempXbox = new Xbox360OutDevice(vigemTestClient);
                            outputDevice = tempXbox;
                            tempXbox.cont.FeedbackReceived += (eventsender, args) => {
                                SetDevRumble(device, args.LargeMotor, args.SmallMotor);
                            };

                            tempXbox.Connect();
                            LogDebug("X360 Controller #" + (devIndex + 1) + " connected");
                        }
                        else if (conType == OutContType.DS4)
                        {
                            LogDebug("Plugging in DS4 Controller #" + (devIndex + 1));
                            DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
                            outputDevice = tempDS4;
                            tempDS4.cont.FeedbackReceived += (eventsender, args) => {
                                SetDevRumble(device, args.LargeMotor, args.SmallMotor);
                            };

                            tempDS4.Connect();
                            LogDebug("DS4 Controller #" + (devIndex + 1) + " connected");
                        }

                        aux.UseDInputOnly = false;
                    }
                }
            }
        }

        //Called when DS4 is disconnected or timed out
        protected virtual void On_DS4Removal(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device) sender;
            Debug.Assert(DS4Controller == null || device == DS4Controller);

            if (DS4Controller != null)
            {
                // FIXME: this check may be unnecessary
                bool removingStatus = false;
                lock (device.removeLocker)
                {
                    if (!device.IsRemoving)
                    {
                        removingStatus = true;
                        device.IsRemoving = true;
                    }
                }

                if (removingStatus)
                {
                    CurrentState.Battery = PreviousState.Battery = 0; // Reset for the next connection's initial status change.
                    if (!aux.UseDInputOnly)
                    {
                        string tempType = outputDevice.GetDeviceType();
                        outputDevice.Disconnect();
                        outputDevice = null;
                        //x360control.Disconnect();
                        //x360control = null;
                        LogDebug(tempType + " Controller # " + (devIndex + 1) + " unplugged");
                    }

                    // Use Task to reset device synth state and commit it
                    Task.Run(() => {
                        mapping.Commit();
                    }).Wait();

                    string removed = Properties.Resources.ControllerWasRemoved.Replace("*Mac address*", (devIndex + 1).ToString());
                    if (device.getBattery() <= 20 &&
                        device.getConnectionType() == ConnectionType.BT && !device.isCharging())
                    {
                        removed += ". " + Properties.Resources.ChargeController;
                    }

                    LogDebug(removed);
                    AppLogger.LogToTray(removed);
                    /*Stopwatch sw = new Stopwatch();
                    sw.Start();
                    while (sw.ElapsedMilliseconds < XINPUT_UNPLUG_SETTLE_TIME)
                    {
                        // Use SpinWait to keep control of current thread. Using Sleep could potentially
                        // cause other events to get run out of order
                        System.Threading.Thread.SpinWait(500);
                    }
                    sw.Stop();
                    */

                    device.IsRemoved = true;
                    device.Synced = false;
                    DS4Controller = null;
                    touchPad = null;
                    lag = false;
                    inWarnMonitor = false;
                    aux.UseDInputOnly = true;
                    uiContext?.Post(new SendOrPostCallback((state) => {
                        OnControllerRemoved(this, devIndex);
                    }), null);
                    //Thread.Sleep(XINPUT_UNPLUG_SETTLE_TIME);
                }
            }
        }

        public bool lag { get; private set; } = false;
        public bool inWarnMonitor;
        private byte currentBattery = 0;
        private bool charging = false;
        private string tempString = string.Empty;

        // Called every time a new input report has arrived
        protected virtual void On_Report(DS4Device device, EventArgs e)
        {
            if (cfg.FlushHIDQueue)
                device.FlushHID();

            string devError = tempString = device.error;
            if (!string.IsNullOrEmpty(devError))
            {
                uiContext?.Post(new SendOrPostCallback(delegate(object state) {
                    LogDebug(devError);
                }), null);
            }

            if (inWarnMonitor)
            {
                int flashWhenLateAt = API.Config.FlashWhenLateAt;
                if (!lag && device.Latency >= flashWhenLateAt)
                {
                    lag = true;
                    uiContext?.Post(new SendOrPostCallback(delegate(object state) {
                        LagFlashWarning(true);
                    }), null);
                }
                else if (lag && device.Latency < flashWhenLateAt)
                {
                    lag = false;
                    uiContext?.Post(new SendOrPostCallback(delegate(object state) {
                        LagFlashWarning(false);
                    }), null);
                }
            }
            else
            {
                if (DateTime.UtcNow - device.firstActive > TimeSpan.FromSeconds(5))
                {
                    inWarnMonitor = true;
                }
            }

            device.getCurrentState(CurrentState);
            DS4State cState = CurrentState;
            DS4State pState = device.getPreviousStateRef();
            //device.getPreviousState(PreviousState[ind]);
            //DS4State pState = PreviousState[ind];

            if (device.firstReport && device.IsAlive())
            {
                device.firstReport = false;
                uiContext?.Post(new SendOrPostCallback(delegate(object state) {
                    OnDeviceStatusChanged(this, devIndex);
                }), null);
            }
            else if (pState.Battery != cState.Battery || device.oldCharging != device.isCharging())
            {
                byte tempBattery = currentBattery = cState.Battery;
                bool tempCharging = charging = device.isCharging();
                uiContext?.Post(new SendOrPostCallback(delegate(object state) {
                    OnBatteryStatusChange(this, devIndex, tempBattery, tempCharging);
                }), null);
            }

            if (cfg.EnableTouchToggle)
                CheckForTouchToggle(cState, pState);

            cState = mapping.SetCurveAndDeadzone(cState, TempState);

            if (!ctlSvc.recordingMacro && (aux.UseTempProfile ||
                                           cfg.HasCustomActions || cfg.HasCustomExtras ||
                                           cfg.ProfileActions.Count > 0 ||
                                           cfg.SASteeringWheelEmulationAxis >= SASteeringWheelEmulationAxisType.VJoy1X))
            {
                mapping.MapCustom(cState, MappedState, ExposedState, touchPad);
                cState = MappedState;
            }

            if (!aux.UseDInputOnly)
            {
                outputDevice?.ConvertandSendReport(cState, devIndex);
                //testNewReport(ref x360reports[ind], cState, ind);
                //x360controls[ind]?.SendReport(x360reports[ind]);

                //x360Bus.Parse(cState, processingData[ind].Report, ind);
                // We push the translated Xinput state, and simultaneously we
                // pull back any possible rumble data coming from Xinput consumers.
                /*if (x360Bus.Report(processingData[ind].Report, processingData[ind].Rumble))
                {
                    byte Big = processingData[ind].Rumble[3];
                    byte Small = processingData[ind].Rumble[4];

                    if (processingData[ind].Rumble[1] == 0x08)
                    {
                        SetDevRumble(device, Big, Small, ind);
                    }
                }
                */
            }
            else
            {
                // UseDInputOnly profile may re-map sixaxis gyro sensor values as a VJoy joystick axis (steering wheel emulation mode using VJoy output device). Handle this option because VJoy output works even in USeDInputOnly mode.
                // If steering wheel emulation uses LS/RS/R2/L2 output axies then the profile should NOT use UseDInputOnly option at all because those require a virtual output device.
                SASteeringWheelEmulationAxisType steeringWheelMappedAxis = cfg.SASteeringWheelEmulationAxis;
                switch (steeringWheelMappedAxis)
                {
                    case SASteeringWheelEmulationAxisType.None: break;

                    case SASteeringWheelEmulationAxisType.VJoy1X:
                    case SASteeringWheelEmulationAxisType.VJoy2X:
                        DS4Windows.VJoyFeeder.vJoyFeeder.FeedAxisValue(cState.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, DS4Windows.VJoyFeeder.HID_USAGES.HID_USAGE_X);
                        break;

                    case SASteeringWheelEmulationAxisType.VJoy1Y:
                    case SASteeringWheelEmulationAxisType.VJoy2Y:
                        DS4Windows.VJoyFeeder.vJoyFeeder.FeedAxisValue(cState.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, DS4Windows.VJoyFeeder.HID_USAGES.HID_USAGE_Y);
                        break;

                    case SASteeringWheelEmulationAxisType.VJoy1Z:
                    case SASteeringWheelEmulationAxisType.VJoy2Z:
                        DS4Windows.VJoyFeeder.vJoyFeeder.FeedAxisValue(cState.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, DS4Windows.VJoyFeeder.HID_USAGES.HID_USAGE_Z);
                        break;
                }
            }

            // Output any synthetic events.
            mapping.Commit();

            // Update the GUI/whatever.
            lightBar.updateLightBar(device);
        }

        public void LagFlashWarning(bool on)
        {
            if (on)
            {
                lag = true;
                LogDebug(Properties.Resources.LatencyOverTen.Replace("*number*", (devIndex + 1).ToString()), true);
                if (API.Config.FlashWhenLate)
                {
                    DS4Color color = new DS4Color {red = 50, green = 0, blue = 0};
                    lightBar.forcedColor = color;
                    lightBar.forcedFlash = 2;
                    lightBar.forcedLight = true;
                }
            }
            else
            {
                lag = false;
                LogDebug(Properties.Resources.LatencyNotOverTen.Replace("*number*", (devIndex + 1).ToString()));
                lightBar.forcedLight = false;
                lightBar.forcedFlash = 0;
            }
        }

        public DS4Controls GetActiveInputControl()
        {
            DS4State cState = CurrentState;
            DS4StateExposed eState = ExposedState;
            Mouse tp = touchPad;
            DS4Controls result = DS4Controls.None;

            if (DS4Controller != null)
            {
                if (Mapping.getBoolButtonMapping(cState.Cross))
                    result = DS4Controls.Cross;
                else if (Mapping.getBoolButtonMapping(cState.Circle))
                    result = DS4Controls.Circle;
                else if (Mapping.getBoolButtonMapping(cState.Triangle))
                    result = DS4Controls.Triangle;
                else if (Mapping.getBoolButtonMapping(cState.Square))
                    result = DS4Controls.Square;
                else if (Mapping.getBoolButtonMapping(cState.L1))
                    result = DS4Controls.L1;
                else if (Mapping.getBoolTriggerMapping(cState.L2))
                    result = DS4Controls.L2;
                else if (Mapping.getBoolButtonMapping(cState.L3))
                    result = DS4Controls.L3;
                else if (Mapping.getBoolButtonMapping(cState.R1))
                    result = DS4Controls.R1;
                else if (Mapping.getBoolTriggerMapping(cState.R2))
                    result = DS4Controls.R2;
                else if (Mapping.getBoolButtonMapping(cState.R3))
                    result = DS4Controls.R3;
                else if (Mapping.getBoolButtonMapping(cState.DpadUp))
                    result = DS4Controls.DpadUp;
                else if (Mapping.getBoolButtonMapping(cState.DpadDown))
                    result = DS4Controls.DpadDown;
                else if (Mapping.getBoolButtonMapping(cState.DpadLeft))
                    result = DS4Controls.DpadLeft;
                else if (Mapping.getBoolButtonMapping(cState.DpadRight))
                    result = DS4Controls.DpadRight;
                else if (Mapping.getBoolButtonMapping(cState.Share))
                    result = DS4Controls.Share;
                else if (Mapping.getBoolButtonMapping(cState.Options))
                    result = DS4Controls.Options;
                else if (Mapping.getBoolButtonMapping(cState.PS))
                    result = DS4Controls.PS;
                else if (Mapping.getBoolAxisDirMapping(cState.LX, true))
                    result = DS4Controls.LXPos;
                else if (Mapping.getBoolAxisDirMapping(cState.LX, false))
                    result = DS4Controls.LXNeg;
                else if (Mapping.getBoolAxisDirMapping(cState.LY, true))
                    result = DS4Controls.LYPos;
                else if (Mapping.getBoolAxisDirMapping(cState.LY, false))
                    result = DS4Controls.LYNeg;
                else if (Mapping.getBoolAxisDirMapping(cState.RX, true))
                    result = DS4Controls.RXPos;
                else if (Mapping.getBoolAxisDirMapping(cState.RX, false))
                    result = DS4Controls.RXNeg;
                else if (Mapping.getBoolAxisDirMapping(cState.RY, true))
                    result = DS4Controls.RYPos;
                else if (Mapping.getBoolAxisDirMapping(cState.RY, false))
                    result = DS4Controls.RYNeg;
                else if (Mapping.getBoolTouchMapping(tp.leftDown))
                    result = DS4Controls.TouchLeft;
                else if (Mapping.getBoolTouchMapping(tp.rightDown))
                    result = DS4Controls.TouchRight;
                else if (Mapping.getBoolTouchMapping(tp.multiDown))
                    result = DS4Controls.TouchMulti;
                else if (Mapping.getBoolTouchMapping(tp.upperDown))
                    result = DS4Controls.TouchUpper;
            }

            return result;
        }

        public bool touchreleased = true;
        public bool touchslid = false;

        protected virtual void CheckForTouchToggle(DS4State cState, DS4State pState)
        {
            if (!cfg.UseTPforControls && cState.Touch1 && pState.PS)
            {
                if (aux.TouchpadActive && touchreleased)
                {
                    aux.TouchpadActive = false;
                    LogDebug(Properties.Resources.TouchpadMovementOff);
                    AppLogger.LogToTray(Properties.Resources.TouchpadMovementOff);
                    touchreleased = false;
                }
                else if (touchreleased)
                {
                    aux.TouchpadActive = true;
                    LogDebug(Properties.Resources.TouchpadMovementOn);
                    AppLogger.LogToTray(Properties.Resources.TouchpadMovementOn);
                    touchreleased = false;
                }
            }
            else
                touchreleased = true;
        }

        public virtual void StartTPOff()
        {
            aux.TouchpadActive = false;
        }

        public enum TouchpadSlideDir { none, right, left, neither }

        public static bool isSlideLeftRight(TouchpadSlideDir dir)
            => dir == TouchpadSlideDir.left || dir == TouchpadSlideDir.right;

        public virtual TouchpadSlideDir TouchpadSlide()
        {
            DS4State cState = CurrentState;
            var slidedir = TouchpadSlideDir.none;
            if (DS4Controller != null && cState.Touch2 &&
                !(touchPad.dragging || touchPad.dragging2))
            {
                if (touchPad.slideright && !touchslid)
                {
                    slidedir = TouchpadSlideDir.right;
                    touchslid = true;
                }
                else if (touchPad.slideleft && !touchslid)
                {
                    slidedir = TouchpadSlideDir.left;
                    touchslid = true;
                }
                else if (!touchPad.slideleft && !touchPad.slideright)
                {
                    slidedir = TouchpadSlideDir.neither;
                    touchslid = false;
                }
            }

            return slidedir;
        }
    }

    public partial class ControlService
    {
        public virtual void LogDebug(String Data, bool warning = false)
        {
            //Console.WriteLine(System.DateTime.Now.ToString("G") + "> " + Data);
            if (Debug != null)
            {
                DebugEventArgs args = new DebugEventArgs(Data, warning);
                OnDebug(this, args);
            }
        }

        public virtual void OnDebug(object sender, DebugEventArgs args)
        {
            if (Debug != null)
                Debug(this, args);
        }
    }

    public partial class DeviceControlService
    {
        public void LogDebug(string data, bool warning = false)
            => ctlSvc.LogDebug(data, warning);

        // sets the rumble adjusted with rumble boost. General use method
        public virtual void setRumble(byte heavyMotor, byte lightMotor)
        {
            DS4Device device = DS4Controller;
            if (device != null)
                SetDevRumble(device, heavyMotor, lightMotor);
        }

        // sets the rumble adjusted with rumble boost. Method more used for
        // report handling. Avoid constant checking for a device.
        public void SetDevRumble(DS4Device device,
            byte heavyMotor, byte lightMotor)
        {
            byte boost = cfg.RumbleBoost;
            uint lightBoosted = ((uint)lightMotor * (uint)boost) / 100;
            if (lightBoosted > 255)
                lightBoosted = 255;
            uint heavyBoosted = ((uint)heavyMotor * (uint)boost) / 100;
            if (heavyBoosted > 255)
                heavyBoosted = 255;

            device.setRumble((byte)lightBoosted, (byte)heavyBoosted);
        }

        public DS4State getDS4State()
        {
            return CurrentState;
        }

        public DS4State getDS4StateMapped()
        {
            return MappedState;
        }

        public DS4State getDS4StateTemp()
        {
            return TempState;
        }
    }
}
