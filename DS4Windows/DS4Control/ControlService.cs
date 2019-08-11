﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Media;
using System.Threading.Tasks;
using static DS4Windows.Global;
using System.Threading;
using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace DS4Windows
{
    public class ControlService
    {
        public ViGEmClient vigemTestClient = null;
        public const int DS4_CONTROLLER_COUNT = 4;
        public DS4Device[] DS4Controllers = new DS4Device[DS4_CONTROLLER_COUNT];
        public Mouse[] touchPad = new Mouse[DS4_CONTROLLER_COUNT];
        private bool running = false;
        private DS4State[] MappedState = new DS4State[DS4_CONTROLLER_COUNT];
        private DS4State[] CurrentState = new DS4State[DS4_CONTROLLER_COUNT];
        private DS4State[] PreviousState = new DS4State[DS4_CONTROLLER_COUNT];
        private DS4State[] TempState = new DS4State[DS4_CONTROLLER_COUNT];
        public DS4StateExposed[] ExposedState = new DS4StateExposed[DS4_CONTROLLER_COUNT];
        public bool recordingMacro = false;
        public event EventHandler<DebugEventArgs> Debug = null;
        bool[] buttonsdown = new bool[4] { false, false, false, false };
        bool[] held = new bool[DS4_CONTROLLER_COUNT];
        int[] oldmouse = new int[DS4_CONTROLLER_COUNT] { -1, -1, -1, -1 };
        public OutputDevice[] outputDevices = new OutputDevice[4] { null, null, null, null };
        //public Xbox360Controller[] x360controls = new Xbox360Controller[4] { null, null, null, null };
        /*private Xbox360Report[] x360reports = new Xbox360Report[4] { new Xbox360Report(), new Xbox360Report(),
            new Xbox360Report(), new Xbox360Report()
        };
        */
        Thread tempThread;
        public List<string> affectedDevs = new List<string>()
        {
            @"HID\VID_054C&PID_05C4",
            @"HID\VID_054C&PID_09CC&MI_03",
            @"HID\VID_054C&PID_0BA0&MI_03",
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&05c4",
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc",
        };
        public bool suspending;
        //SoundPlayer sp = new SoundPlayer();
        private UdpServer _udpServer;

        private class X360Data
        {
            public byte[] Report = new byte[28];
            public byte[] Rumble = new byte[8];
        }

        private X360Data[] processingData = new X360Data[4];
        private byte[][] udpOutBuffers = new byte[4][]
        {
            new byte[100], new byte[100],
            new byte[100], new byte[100]
        };


        void GetPadDetailForIdx(int padIdx, ref DualShockPadMeta meta)
        {
            //meta = new DualShockPadMeta();
            meta.PadId = (byte) padIdx;
            meta.Model = DsModel.DS4;

            var d = DS4Controllers[padIdx];
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
            //if (d.isValidSerial())
            //{
                string stringMac = d.getMacAddress();
                if (!string.IsNullOrEmpty(stringMac))
                {
                    stringMac = string.Join("", stringMac.Split(':'));
                    //stringMac = stringMac.Replace(":", "").Trim();
                    meta.PadMacAddress = System.Net.NetworkInformation.PhysicalAddress.Parse(stringMac);
                    isValidSerial = d.isValidSerial();
                }
            //}

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

        private object busThrLck = new object();
        private bool busThrRunning = false;
        private Queue<Action> busEvtQueue = new Queue<Action>();
        private object busEvtQueueLock = new object();
        public ControlService()
        {
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
                { Process tempProc = Process.Start(startInfo); tempProc.Dispose(); }
                catch { }
            }

            for (int i = 0, arlength = DS4Controllers.Length; i < arlength; i++)
            {
                processingData[i] = new X360Data();
                MappedState[i] = new DS4State();
                CurrentState[i] = new DS4State();
                TempState[i] = new DS4State();
                PreviousState[i] = new DS4State();
                ExposedState[i] = new DS4StateExposed(CurrentState[i]);
            }
        }

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

        private void WarnExclusiveModeFailure(DS4Device device)
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

        private SynchronizationContext uiContext = null;
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
                    foreach (DS4Device device in devices) {
                        var cfg = API.Cfg(i);
                        var aux = API.Aux(i);
                        if (showlog)
                            LogDebug(Properties.Resources.FoundController + device.getMacAddress() + " (" + device.getConnectionType() + ")");

                        Task task = new Task(() => { Thread.Sleep(5); WarnExclusiveModeFailure(device); });
                        task.Start();

                        DS4Controllers[i] = device;
                        device.Removal += this.On_DS4Removal;
                        device.Removal += DS4Devices.On_Removal;
                        device.SyncChange += this.On_SyncChange;
                        device.SyncChange += DS4Devices.UpdateSerial;
                        device.SerialChange += this.On_SerialChange;

                        touchPad[i] = new Mouse(i, device);

                        if (!aux.UseTempProfile)
                        {
                            if (device.isValidSerial() && API.Config.ContainsLinkedProfile(device.getMacAddress()))
                            {
                                cfg.ProfilePath = API.Config.GetLinkedProfile(device.getMacAddress());
                            }
                            else {
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
                                LogDebug("Plugging in X360 Controller #" + (i + 1));
                                Xbox360OutDevice tempXbox = new Xbox360OutDevice(vigemTestClient);
                                outputDevices[i] = tempXbox;
                                int devIndex = i;
                                tempXbox.cont.FeedbackReceived += (sender, args) =>
                                {
                                    SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);
                                };

                                tempXbox.Connect();
                                LogDebug("X360 Controller #" + (i + 1) + " connected");
                            }
                            else if (contType == OutContType.DS4)
                            {
                                LogDebug("Plugging in DS4 Controller #" + (i + 1));
                                DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
                                outputDevices[i] = tempDS4;
                                int devIndex = i;
                                tempDS4.cont.FeedbackReceived += (sender, args) =>
                                {
                                    SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);
                                };

                                tempDS4.Connect();
                                LogDebug("DS4 Controller #" + (i + 1) + " connected");
                            }
                        }
                        else
                        {
                            aux.UseDInputOnly = true;
                        }

                        int tempIdx = i;
                        device.Report += (sender, e) =>
                        {
                            this.On_Report(sender, e, tempIdx);
                        };

                        DS4Device.ReportHandler<EventArgs> tempEvnt = (sender, args) =>
                        {
                            DualShockPadMeta padDetail = new DualShockPadMeta();
                            GetPadDetailForIdx(tempIdx, ref padDetail);
                            _udpServer.NewReportIncoming(ref padDetail, CurrentState[tempIdx], udpOutBuffers[tempIdx]);
                        };
                        device.MotionEvent = tempEvnt;

                        if (_udpServer != null)
                        {
                            device.Report += tempEvnt;
                        }

                        TouchPadOn(i, device);
                        CheckProfileOptions(i, device, true);
                        device.StartUpdate();
                        //string filename = ProfileExePath[ind];
                        //ind++;
                        if (showlog)
                        {
                            if (File.Exists($"{API.AppDataPath}\\Profiles\\{cfg.ProfilePath}.xml"))
                            {
                                string prolog = Properties.Resources.UsingProfile.Replace("*number*", (i + 1).ToString()).Replace("*Profile name*", cfg.ProfilePath);
                                LogDebug(prolog);
                                AppLogger.LogToTray(prolog);
                            }
                            else
                            {
                                string prolog = Properties.Resources.NotUsingProfile.Replace("*number*", (i + 1).ToString());
                                LogDebug(prolog);
                                AppLogger.LogToTray(prolog);
                            }
                        }

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

        public bool Stop(bool showlog = true)
        {
            if (running)
            {
                running = false;
                API.RunHotPlug = false;

                if (showlog)
                    LogDebug(Properties.Resources.StoppingX360);

                LogDebug("Closing connection to ViGEmBus");

                for (int i = 0, arlength = DS4Controllers.Length; i < arlength; i++) {
                    DS4LightBar lightBar = API.Bar(i);
                    DS4Device tempDevice = DS4Controllers[i];
                    if (tempDevice != null)
                    {
                        if ((API.Config.DisconnectBTAtStop && !tempDevice.isCharging()) || suspending)
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
                            lightBar.updateLightBar(DS4Controllers[i]);
                            tempDevice.IsRemoved = true;
                            tempDevice.StopUpdate();
                            DS4Devices.RemoveDevice(tempDevice);
                            Thread.Sleep(50);
                        }

                        CurrentState[i].Battery = PreviousState[i].Battery = 0; // Reset for the next connection's initial status change.
                        outputDevices[i]?.Disconnect();
                        outputDevices[i] = null;
                        API.Aux(i).UseDInputOnly = true;
                        DS4Controllers[i] = null;
                        touchPad[i] = null;
                        lag[i] = false;
                        inWarnMonitor[i] = false;
                    }
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

        public bool HotPlug()
        {
            if (running)
            {
                DS4Devices.findControllers();
                IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
                //foreach (DS4Device device in devices)
                //for (int i = 0, devlen = devices.Count(); i < devlen; i++)
                for (var devEnum = devices.GetEnumerator(); devEnum.MoveNext();)
                {
                    DS4Device device = devEnum.Current;
                    //DS4Device device = devices.ElementAt(i);

                    if (device.isDisconnectingStatus())
                        continue;

                    if (((Func<bool>)delegate
                    {
                        for (Int32 Index = 0, arlength = DS4Controllers.Length; Index < arlength; Index++)
                        {
                            if (DS4Controllers[Index] != null &&
                                DS4Controllers[Index].getMacAddress() == device.getMacAddress())
                                return true;
                        }

                        return false;
                    })())
                    {
                        continue;
                    }

                    for (Int32 index = 0, arlength = DS4Controllers.Length; index < arlength; index++)
                    {
                        var cfg = API.Cfg(index);
                        var aux = API.Aux(index);
                        if (DS4Controllers[index] == null)
                        {
                            LogDebug(Properties.Resources.FoundController + device.getMacAddress() + " (" + device.getConnectionType() + ")");
                            Task task = new Task(() => { Thread.Sleep(5); WarnExclusiveModeFailure(device); });
                            task.Start();
                            DS4Controllers[index] = device;
                            device.Removal += this.On_DS4Removal;
                            device.Removal += DS4Devices.On_Removal;
                            device.SyncChange += this.On_SyncChange;
                            device.SyncChange += DS4Devices.UpdateSerial;
                            device.SerialChange += this.On_SerialChange;

                            touchPad[index] = new Mouse(index, device);

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

                            int tempIdx = index;
                            device.Report += (sender, e) =>
                            {
                                this.On_Report(sender, e, tempIdx);
                            };

                            DS4Device.ReportHandler<EventArgs> tempEvnt = (sender, args) =>
                            {
                                DualShockPadMeta padDetail = new DualShockPadMeta();
                                GetPadDetailForIdx(tempIdx, ref padDetail);
                                _udpServer.NewReportIncoming(ref padDetail, CurrentState[tempIdx], udpOutBuffers[tempIdx]);
                            };
                            device.MotionEvent = tempEvnt;

                            if (_udpServer != null)
                            {
                                device.Report += tempEvnt;
                            }
                            
                            if (!cfg.DInputOnly && device.isSynced())
                            {
                                aux.UseDInputOnly = false;
                                OutContType contType = cfg.OutputDevType;
                                if (contType == OutContType.X360)
                                {
                                    LogDebug("Plugging in X360 Controller #" + (index + 1));
                                    Xbox360OutDevice tempXbox = new Xbox360OutDevice(vigemTestClient);
                                    outputDevices[index] = tempXbox;
                                    int devIndex = index;
                                    tempXbox.cont.FeedbackReceived += (sender, args) =>
                                    {
                                        SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);
                                    };

                                    tempXbox.Connect();
                                    LogDebug("X360 Controller #" + (index + 1) + " connected");
                                }
                                else if (contType == OutContType.DS4)
                                {
                                    LogDebug("Plugging in DS4 Controller #" + (index + 1));
                                    DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
                                    outputDevices[index] = tempDS4;
                                    int devIndex = index;
                                    tempDS4.cont.FeedbackReceived += (sender, args) =>
                                    {
                                        SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);
                                    };

                                    tempDS4.Connect();
                                    LogDebug("DS4 Controller #" + (index + 1) + " connected");
                                }
                                
                            }
                            else
                            {
                                aux.UseDInputOnly = true;
                            }

                            TouchPadOn(index, device);
                            CheckProfileOptions(index, device);
                            device.StartUpdate();

                            //string filename = Path.GetFileName(ProfileExePath[Index]);
                            if (File.Exists($"{API.AppDataPath}\\Profiles\\{cfg.ProfilePath}.xml"))
                            {
                                string prolog = Properties.Resources.UsingProfile.Replace("*number*", (index + 1).ToString()).Replace("*Profile name*", cfg.ProfilePath);
                                LogDebug(prolog);
                                AppLogger.LogToTray(prolog);
                            }
                            else
                            {
                                string prolog = Properties.Resources.NotUsingProfile.Replace("*number*", (index + 1).ToString());
                                LogDebug(prolog);
                                AppLogger.LogToTray(prolog);
                            }

                            break;
                        }
                    }
                }
            }

            return true;
        }

        /*private void testNewReport(ref Xbox360Report xboxreport, DS4State state,
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
        */

        /*private short AxisScale(Int32 Value, Boolean Flip)
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
        */

        private void CheckProfileOptions(int ind, DS4Device device, bool startUp=false)
        {
            var cfg = API.Cfg(ind);
            device.setIdleTimeout(cfg.IdleDisconnectTimeout);
            device.setBTPollRate(cfg.BTPollRate);
            touchPad[ind].ResetTrackAccel(cfg.TrackballFriction);

            if (!startUp)
            {
                CheckLauchProfileOption(ind, device);
            }
        }

        private void CheckLauchProfileOption(int ind, DS4Device device)
        {
            string programPath = API.Cfg(ind).LaunchProgram;
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
                    Task processTask = new Task(() =>
                    {
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

        public void TouchPadOn(int ind, DS4Device device)
        {
            Mouse tPad = touchPad[ind];
            //ITouchpadBehaviour tPad = touchPad[ind];
            device.Touchpad.TouchButtonDown += tPad.touchButtonDown;
            device.Touchpad.TouchButtonUp += tPad.touchButtonUp;
            device.Touchpad.TouchesBegan += tPad.touchesBegan;
            device.Touchpad.TouchesMoved += tPad.touchesMoved;
            device.Touchpad.TouchesEnded += tPad.touchesEnded;
            device.Touchpad.TouchUnchanged += tPad.touchUnchanged;
            //device.Touchpad.PreTouchProcess += delegate { touchPad[ind].populatePriorButtonStates(); };
            device.Touchpad.PreTouchProcess += (sender, args) => { touchPad[ind].populatePriorButtonStates(); };
            device.SixAxis.SixAccelMoved += tPad.sixaxisMoved;
            //LogDebug("Touchpad mode for " + device.MacAddress + " is now " + tmode.ToString());
            //Log.LogToTray("Touchpad mode for " + device.MacAddress + " is now " + tmode.ToString());
        }

        public string getDS4ControllerInfo(int index)
        {
            DS4Device d = DS4Controllers[index];
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

        public string getDS4MacAddress(int index)
        {
            DS4Device d = DS4Controllers[index];
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

        public string getShortDS4ControllerInfo(int index)
        {
            DS4Device d = DS4Controllers[index];
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

        public string getDS4Battery(int index)
        {
            DS4Device d = DS4Controllers[index];
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

        public string getDS4Status(int index)
        {
            DS4Device d = DS4Controllers[index];
            if (d != null)
            {
                return d.getConnectionType() + "";
            }
            else
                return Properties.Resources.NoneText;
        }

        protected void On_SerialChange(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0, arlength = DS4_CONTROLLER_COUNT; ind == -1 && i < arlength; i++)
            {
                DS4Device tempDev = DS4Controllers[i];
                if (tempDev != null && device == tempDev)
                    ind = i;
            }

            if (ind >= 0)
            {
                OnDeviceSerialChange(this, ind, device.getMacAddress());
            }
        }

        protected void On_SyncChange(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0, arlength = DS4_CONTROLLER_COUNT; ind == -1 && i < arlength; i++)
            {
                DS4Device tempDev = DS4Controllers[i];
                if (tempDev != null && device == tempDev)
                    ind = i;
            }

            if (ind >= 0) {
                var aux = API.Aux(ind);
                bool synced = device.isSynced();

                if (!synced)
                {
                    if (!aux.UseDInputOnly)
                    {
                        string tempType = outputDevices[ind].GetDeviceType();
                        outputDevices[ind].Disconnect();
                        outputDevices[ind] = null;
                        aux.UseDInputOnly = true;
                        LogDebug($"{tempType} Controller #{ind + 1} unplugged");
                    }
                }
                else
                {
                    if (!aux.UseDInputOnly)
                    {
                        OutContType conType = API.Cfg(ind).OutputDevType;
                        if (conType == OutContType.X360)
                        {
                            LogDebug("Plugging in X360 Controller #" + (ind + 1));
                            Xbox360OutDevice tempXbox = new Xbox360OutDevice(vigemTestClient);
                            outputDevices[ind] = tempXbox;
                            tempXbox.cont.FeedbackReceived += (eventsender, args) =>
                            {
                                SetDevRumble(device, args.LargeMotor, args.SmallMotor, ind);
                            };

                            tempXbox.Connect();
                            LogDebug("X360 Controller #" + (ind + 1) + " connected");
                        }
                        else if (conType == OutContType.DS4)
                        {
                            LogDebug("Plugging in DS4 Controller #" + (ind + 1));
                            DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
                            outputDevices[ind] = tempDS4;
                            int devIndex = ind;
                            tempDS4.cont.FeedbackReceived += (eventsender, args) =>
                            {
                                SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);
                            };

                            tempDS4.Connect();
                            LogDebug("DS4 Controller #" + (ind + 1) + " connected");
                        }

                        aux.UseDInputOnly = false;
                    }
                }
            }
        }

        //Called when DS4 is disconnected or timed out
        protected virtual void On_DS4Removal(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0, arlength = DS4Controllers.Length; ind == -1 && i < arlength; i++)
            {
                if (DS4Controllers[i] != null && device.getMacAddress() == DS4Controllers[i].getMacAddress())
                    ind = i;
            }

            if (ind != -1) {
                var aux = API.Aux(ind);
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
                    CurrentState[ind].Battery = PreviousState[ind].Battery = 0; // Reset for the next connection's initial status change.
                    if (!aux.UseDInputOnly)
                    {
                        string tempType = outputDevices[ind].GetDeviceType();
                        outputDevices[ind].Disconnect();
                        outputDevices[ind] = null;
                        //x360controls[ind].Disconnect();
                        //x360controls[ind] = null;
                        LogDebug(tempType + " Controller # " + (ind + 1) + " unplugged");
                    }

                    // Use Task to reset device synth state and commit it
                    Task.Run(() =>
                    {
                        API.Mapping(ind).Commit();
                    }).Wait();

                    string removed = Properties.Resources.ControllerWasRemoved.Replace("*Mac address*", (ind + 1).ToString());
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
                    DS4Controllers[ind] = null;
                    touchPad[ind] = null;
                    lag[ind] = false;
                    inWarnMonitor[ind] = false;
                    aux.UseDInputOnly = true;
                    uiContext?.Post(new SendOrPostCallback((state) =>
                    {
                        OnControllerRemoved(this, ind);
                    }), null);
                    //Thread.Sleep(XINPUT_UNPLUG_SETTLE_TIME);
                }
            }
        }

        public bool[] lag = new bool[4] { false, false, false, false };
        public bool[] inWarnMonitor = new bool[4] { false, false, false, false };
        private byte[] currentBattery = new byte[4] { 0, 0, 0, 0 };
        private bool[] charging = new bool[4] { false, false, false, false };
        private string[] tempStrings = new string[4] { string.Empty, string.Empty, string.Empty, string.Empty };

        // Called every time a new input report has arrived
        //protected virtual void On_Report(object sender, EventArgs e, int ind)
        protected virtual void On_Report(DS4Device device, EventArgs e, int ind)
        {
            //DS4Device device = (DS4Device)sender;
            if (ind == -1) return;
            var cfg = API.Cfg(ind);
            var aux = API.Aux(ind);
            var mapping = API.Mapping(ind);
            if (cfg.FlushHIDQueue)
                device.FlushHID();

            string devError = tempStrings[ind] = device.error;
            if (!string.IsNullOrEmpty(devError))
            {
                uiContext?.Post(new SendOrPostCallback(delegate (object state)
                {
                    LogDebug(devError);
                }), null);
            }

            if (inWarnMonitor[ind])
            {
                int flashWhenLateAt = API.Config.FlashWhenLateAt;
                if (!lag[ind] && device.Latency >= flashWhenLateAt)
                {
                    lag[ind] = true;
                    uiContext?.Post(new SendOrPostCallback(delegate (object state)
                    {
                        LagFlashWarning(ind, true);
                    }), null);
                }
                else if (lag[ind] && device.Latency < flashWhenLateAt)
                {
                    lag[ind] = false;
                    uiContext?.Post(new SendOrPostCallback(delegate (object state)
                    {
                        LagFlashWarning(ind, false);
                    }), null);
                }
            }
            else
            {
                if (DateTime.UtcNow - device.firstActive > TimeSpan.FromSeconds(5))
                {
                    inWarnMonitor[ind] = true;
                }
            }

            device.getCurrentState(CurrentState[ind]);
            DS4State cState = CurrentState[ind];
            DS4State pState = device.getPreviousStateRef();
            //device.getPreviousState(PreviousState[ind]);
            //DS4State pState = PreviousState[ind];

            if (device.firstReport && device.IsAlive())
            {
                device.firstReport = false;
                uiContext?.Post(new SendOrPostCallback(delegate (object state)
                {
                    OnDeviceStatusChanged(this, ind);
                }), null);
            }
            else if (pState.Battery != cState.Battery || device.oldCharging != device.isCharging())
            {
                byte tempBattery = currentBattery[ind] = cState.Battery;
                bool tempCharging = charging[ind] = device.isCharging();
                uiContext?.Post(new SendOrPostCallback(delegate (object state)
                {
                    OnBatteryStatusChange(this, ind, tempBattery, tempCharging);
                }), null);
            }

            if (cfg.EnableTouchToggle)
                CheckForTouchToggle(ind, cState, pState);

            cState = mapping.SetCurveAndDeadzone(cState, TempState[ind]);

            if (!recordingMacro && (aux.UseTempProfile ||
                cfg.HasCustomActions || cfg.HasCustomExtras ||
                cfg.ProfileActions.Count > 0 ||
                cfg.SASteeringWheelEmulationAxis >= SASteeringWheelEmulationAxisType.VJoy1X))
            {
                mapping.MapCustom(cState, MappedState[ind], ExposedState[ind], touchPad[ind], this);
                cState = MappedState[ind];
            }

            if (!aux.UseDInputOnly)
            {
                outputDevices[ind]?.ConvertandSendReport(cState, ind);
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
            API.Bar(ind).updateLightBar(device);
        }

        public void LagFlashWarning(int ind, bool on)
        {
            var lightBar = API.Bar(ind);
            if (on)
            {
                lag[ind] = true;
                LogDebug(Properties.Resources.LatencyOverTen.Replace("*number*", (ind + 1).ToString()), true);
                if (API.Config.FlashWhenLate)
                {
                    DS4Color color = new DS4Color { red = 50, green = 0, blue = 0 };
                    lightBar.forcedColor = color;
                    lightBar.forcedFlash = 2;
                    lightBar.forcedLight = true;
                }
            }
            else
            {
                lag[ind] = false;
                LogDebug(Properties.Resources.LatencyNotOverTen.Replace("*number*", (ind + 1).ToString()));
                lightBar.forcedLight = false;
                lightBar.forcedFlash = 0;
            }
        }

        public DS4Controls GetActiveInputControl(int ind)
        {
            DS4State cState = CurrentState[ind];
            DS4StateExposed eState = ExposedState[ind];
            Mouse tp = touchPad[ind];
            DS4Controls result = DS4Controls.None;

            if (DS4Controllers[ind] != null)
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

        public bool[] touchreleased = new bool[4] { true, true, true, true },
            touchslid = new bool[4] { false, false, false, false };

        protected virtual void CheckForTouchToggle(int deviceID, DS4State cState, DS4State pState)
        {
            var cfg = API.Cfg(deviceID);
            var aux = API.Aux(deviceID);
            if (!cfg.UseTPforControls && cState.Touch1 && pState.PS)
            {
                if (aux.TouchpadActive && touchreleased[deviceID])
                {
                    aux.TouchpadActive = false;
                    LogDebug(Properties.Resources.TouchpadMovementOff);
                    AppLogger.LogToTray(Properties.Resources.TouchpadMovementOff);
                    touchreleased[deviceID] = false;
                }
                else if (touchreleased[deviceID])
                {
                    aux.TouchpadActive = true;
                    LogDebug(Properties.Resources.TouchpadMovementOn);
                    AppLogger.LogToTray(Properties.Resources.TouchpadMovementOn);
                    touchreleased[deviceID] = false;
                }
            }
            else
                touchreleased[deviceID] = true;
        }

        public virtual void StartTPOff(int deviceID)
        {
            if (deviceID < 4)
            {
                API.Aux(deviceID).TouchpadActive = false;
            }
        }

        public virtual string TouchpadSlide(int ind)
        {
            DS4State cState = CurrentState[ind];
            string slidedir = "none";
            if (DS4Controllers[ind] != null && cState.Touch2 &&
               !(touchPad[ind].dragging || touchPad[ind].dragging2))
            {
                if (touchPad[ind].slideright && !touchslid[ind])
                {
                    slidedir = "right";
                    touchslid[ind] = true;
                }
                else if (touchPad[ind].slideleft && !touchslid[ind])
                {
                    slidedir = "left";
                    touchslid[ind] = true;
                }
                else if (!touchPad[ind].slideleft && !touchPad[ind].slideright)
                {
                    slidedir = "";
                    touchslid[ind] = false;
                }
            }

            return slidedir;
        }

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

        // sets the rumble adjusted with rumble boost. General use method
        public virtual void setRumble(byte heavyMotor, byte lightMotor, int deviceNum)
        {
            if (deviceNum < 4)
            {
                DS4Device device = DS4Controllers[deviceNum];
                if (device != null)
                    SetDevRumble(device, heavyMotor, lightMotor, deviceNum);
                    //device.setRumble((byte)lightBoosted, (byte)heavyBoosted);
            }
        }

        // sets the rumble adjusted with rumble boost. Method more used for
        // report handling. Avoid constant checking for a device.
        public void SetDevRumble(DS4Device device,
            byte heavyMotor, byte lightMotor, int deviceNum)
        {
            byte boost = API.Cfg(deviceNum).RumbleBoost;
            uint lightBoosted = ((uint)lightMotor * (uint)boost) / 100;
            if (lightBoosted > 255)
                lightBoosted = 255;
            uint heavyBoosted = ((uint)heavyMotor * (uint)boost) / 100;
            if (heavyBoosted > 255)
                heavyBoosted = 255;

            device.setRumble((byte)lightBoosted, (byte)heavyBoosted);
        }

        public DS4State getDS4State(int ind)
        {
            return CurrentState[ind];
        }

        public DS4State getDS4StateMapped(int ind)
        {
            return MappedState[ind];
        }

        public DS4State getDS4StateTemp(int ind)
        {
            return TempState[ind];
        }
    }
}
