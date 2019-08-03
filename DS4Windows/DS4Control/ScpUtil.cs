using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;

using System.IO;
using System.Reflection;
using System.Xml;
using System.Drawing;

using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows.Forms.VisualStyles;
using System.Xml.Serialization;
using System.Collections;

namespace DS4Windows
{
    [Flags]
    public enum DS4KeyType : byte { None = 0, ScanCode = 1, Toggle = 2, Unbound = 4, Macro = 8, HoldMacro = 16, RepeatMacro = 32 }; // Increment by exponents of 2*, starting at 2^0
    public enum Ds3PadId : byte { None = 0xFF, One = 0x00, Two = 0x01, Three = 0x02, Four = 0x03, All = 0x04 };
    public enum DS4Controls : byte { None, LXNeg, LXPos, LYNeg, LYPos, RXNeg, RXPos, RYNeg, RYPos, L1, L2, L3, R1, R2, R3, Square, Triangle, Circle, Cross, DpadUp, DpadRight, DpadDown, DpadLeft, PS, TouchLeft, TouchUpper, TouchMulti, TouchRight, Share, Options, GyroXPos, GyroXNeg, GyroZPos, GyroZNeg, SwipeLeft, SwipeRight, SwipeUp, SwipeDown };
    public enum X360Controls : byte { None, LXNeg, LXPos, LYNeg, LYPos, RXNeg, RXPos, RYNeg, RYPos, LB, LT, LS, RB, RT, RS, X, Y, B, A, DpadUp, DpadRight, DpadDown, DpadLeft, Guide, Back, Start, LeftMouse, RightMouse, MiddleMouse, FourthMouse, FifthMouse, WUP, WDOWN, MouseUp, MouseDown, MouseLeft, MouseRight, Unbound };

    public enum SASteeringWheelEmulationAxisType: byte { None = 0, LX, LY, RX, RY, L2R2, VJoy1X, VJoy1Y, VJoy1Z, VJoy2X, VJoy2Y, VJoy2Z };
    public enum OutContType : uint { None = 0, X360, DS4 }

    public class DS4ControlSettings
    {
        public DS4Controls control;
        public string extras = null;
        public DS4KeyType keyType = DS4KeyType.None;
        public enum ActionType : byte { Default, Key, Button, Macro };
        public ActionType actionType = ActionType.Default;
        public object action = null;
        public ActionType shiftActionType = ActionType.Default;
        public object shiftAction = null;
        public int shiftTrigger = 0;
        public string shiftExtras = null;
        public DS4KeyType shiftKeyType = DS4KeyType.None;

        public DS4ControlSettings(DS4Controls ctrl)
        {
            control = ctrl;
        }

        public void Reset()
        {
            extras = null;
            keyType = DS4KeyType.None;
            actionType = ActionType.Default;
            action = null;
            shiftActionType = ActionType.Default;
            shiftAction = null;
            shiftTrigger = 0;
            shiftExtras = null;
            shiftKeyType = DS4KeyType.None;
        }

        internal void UpdateSettings(bool shift, object act, string exts, DS4KeyType kt, int trigger = 0)
        {
            if (!shift)
            {
                if (act is int || act is ushort)
                    actionType = ActionType.Key;
                else if (act is string || act is X360Controls)
                    actionType = ActionType.Button;
                else if (act is int[])
                    actionType = ActionType.Macro;
                else
                    actionType = ActionType.Default;

                action = act;
                extras = exts;
                keyType = kt;
            }
            else
            {
                if (act is int || act is ushort)
                    shiftActionType = ActionType.Key;
                else if (act is string || act is X360Controls)
                    shiftActionType = ActionType.Button;
                else if (act is int[])
                    shiftActionType = ActionType.Macro;
                else
                    shiftActionType = ActionType.Default;

                shiftAction = act;
                shiftExtras = exts;
                shiftKeyType = kt;
                shiftTrigger = trigger;
            }
        }
    }

    public class DebugEventArgs : EventArgs
    {
        public DebugEventArgs(string data, bool warn)
        {
            Data = data;
            Warning = warn;
        }
        public DateTime Time { get; private set; } = DateTime.Now;
        public string Data { get; private set; } = string.Empty;
        public bool Warning { get; private set; } = false;
    }

    public class MappingDoneEventArgs : EventArgs
    {
        public MappingDoneEventArgs(int DeviceID) => this.DeviceID = DeviceID;
        public int DeviceID { get; private set; } = -1;
    }

    public class ReportEventArgs : EventArgs
    {
        public ReportEventArgs(Ds3PadId Pad) => this.Pad = Pad;
        public Ds3PadId Pad { get; private set; } = Ds3PadId.None;
        public Byte[] Report { get; private set; } = new byte[64];
    }

    public class BatteryReportArgs : EventArgs
    {
        public BatteryReportArgs(int index, int level, bool charging)
        {
            this.index = index;
            this.level = level;
            this.isCharging = charging;
        }
        public int index { get; private set; }
        public int level { get; private set; }
        public bool isCharging { get; private set; }
    }

    public class ControllerRemovedArgs : EventArgs
    {
        public ControllerRemovedArgs(int index) => this.index = index;
        public int index { get; private set; }
    }

    public class DeviceStatusChangeEventArgs : EventArgs
    {
        public DeviceStatusChangeEventArgs(int index) => this.index = index;
        public int index { get; private set; }
    }

    public class SerialChangeArgs : EventArgs
    {
        public SerialChangeArgs(int index, string serial)
        {
            this.index = index;
            this.serial = serial;
        }
        public int index { get; private set; }
        public string serial { get; private set; }
    }

    struct Lazy<T> where T:struct
    {
        private T? value;
        private Func<T> init;
        public static implicit operator T(Lazy<T> o) => o.get();
        public static implicit operator Lazy<T>(Func<T> init) => new Lazy<T>(init);
        T get() => value ?? (T)(value = init());
        public Lazy(Func<T> init)
        {
            value = null;
            this.init = init;
        }
    }

    struct LazyObj<T> where T : class
    {
        private T value;
        private Func<T> init;
        public static implicit operator T(LazyObj<T> o) => o.get();
        public static implicit operator LazyObj<T>(Func<T> init) => new LazyObj<T>(init);
        T get() => value ?? (value = init());
        public LazyObj(Func<T> init)
        {
            value = null;
            this.init = init;
        }
    }

    class DeviceAuxiliaryConfig : IDeviceAuxiliaryConfig
    {
        public string TempProfileName { get; set; } = string.Empty;
        public bool UseTempProfile { get; set; } = false;
        public bool TempProfileDistance { get; set; } = false;
        public bool UseDInputOnly { get; set; } = true;
        public bool LinkedProfileCheck { get; set; } = true; // applies between this and successor profile
        public bool TouchpadActive { get; set; } = true;
        public OutContType OutDevTypeTemp { get; set; } = DS4Windows.OutContType.X360;
    }

    public class Global : IGlobalConfig
    {
        private BackingStore m_Config = new BackingStore();
        //private static IGlobalConfig This { get => Config; }

        Int32 idleTimeout = 600000;

        public string ExePath { get; } = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
        private string appDataPath; 
        public string AppDataPath
        {
            get => appDataPath ?? (AppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Windows");
            set
            {
                appDataPath = value;
                ProfilePath = $"{appDataPath}\\Profiles.xml";
                ActionsPath = $"{appDataPath}\\Actions.xml";
                LinkedProfilesPath = $"{appDataPath}\\LinkedProfiles.xml";
                ControllerConfigsPath = $"{appDataPath}\\ControllerConfigs.xml";
            }
        }
        public string ProfilePath { get; set;  }
        public string ActionsPath { get; set; }
        public string LinkedProfilesPath { get; set; }
        public string ControllerConfigsPath { get; set; }

        public bool IsFirstRun { get; private set; } = false;
        public bool MultiSaveSpots { get; set; } = false;
        public bool RunHotPlug { get; set; } = false;

        DeviceAuxiliaryConfig[] aux = new DeviceAuxiliaryConfig[5];
        DeviceBackingStore[] cfg = new DeviceBackingStore[5];
        public IDeviceAuxiliaryConfig Aux(int index) => aux[index];
        public IDeviceConfig Cfg(int index) => cfg[index];

        public X360Controls[] DefaultButtonMapping { get; } = { X360Controls.None, X360Controls.LXNeg, X360Controls.LXPos,
            X360Controls.LYNeg, X360Controls.LYPos, X360Controls.RXNeg, X360Controls.RXPos, X360Controls.RYNeg, X360Controls.RYPos,
            X360Controls.LB, X360Controls.LT, X360Controls.LS, X360Controls.RB, X360Controls.RT, X360Controls.RS, X360Controls.X,
            X360Controls.Y, X360Controls.B, X360Controls.A, X360Controls.DpadUp, X360Controls.DpadRight, X360Controls.DpadDown,
            X360Controls.DpadLeft, X360Controls.Guide, X360Controls.None, X360Controls.None, X360Controls.None, X360Controls.None,
            X360Controls.Back, X360Controls.Start, X360Controls.None, X360Controls.None, X360Controls.None, X360Controls.None,
            X360Controls.None, X360Controls.None, X360Controls.None, X360Controls.None
        };

        private DS4Controls[] revButtonMapping;
        public DS4Controls[] RevButtonMapping {
            get {
                if (revButtonMapping is DS4Controls[] result) return result;
                revButtonMapping = new DS4Controls[DefaultButtonMapping.Length];
                int i = 0;
                foreach (var mapping in DefaultButtonMapping.Where(m => m != X360Controls.None))
                {
                    revButtonMapping[(int)mapping] = (DS4Controls)i;
                    i++;
                }
                return revButtonMapping;
            }
        }

        private bool? exePathNeedsAdmin;
        public bool ExePathNeedsAdmin {
            get {
                if (exePathNeedsAdmin is bool result) return result;
                try
                {
                    File.WriteAllText($"{ExePath}\\test.txt", "test");
                    File.Delete($"{ExePath}\\test.txt");
                    return (bool)(exePathNeedsAdmin = false);
                }
                catch (UnauthorizedAccessException)
                {
                    return (bool)(exePathNeedsAdmin = true);
                }
            }
        }

        private bool? isAdministrator;
        public bool IsAdministrator {
            get {
                if (isAdministrator is bool result) return result;
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return (bool) (isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator));
            }
        }

        public void FindConfigLocation()
        {
            if (File.Exists($"{ExePath}\\Auto Profiles.xml")
                && File.Exists($"{AppDataPath}\\Auto Profiles.xml"))
            {
                IsFirstRun = true;
                MultiSaveSpots = true;
            }
            else if (File.Exists($"{ExePath}\\Auto Profiles.xml"))
                AppDataPath = ExePath;
            else if (File.Exists($"{AppDataPath}\\Auto Profiles.xml")) { }
            else if (!File.Exists("${ExePath}\\Auto Profiles.xml")
                && !File.Exists("${AppDataPath}\\Auto Profiles.xml"))
            {
                IsFirstRun = true;
                MultiSaveSpots = false;
            }
        }

        public void SetCulture(string culture)
        {
            try
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(culture);
            }
            catch { /* Skip setting culture that we cannot set */ }
        }

        public class ProfileActions : IEnumerable<string>
        {
            public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)actions).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => actions.GetEnumerator();
            [XmlIgnore]
            private string[] actions = { "Disconnect Controller" };

            [XmlText]
            public string Actions {
                get => String.Join("/", actions);
                set => actions = value.Split('/').ToArray();
            }
        }

        private void CreateStdActions()
        {
            XmlDocument xDoc = new XmlDocument();
            try {
                string[] profiles = Directory.GetFiles($"{AppDataPath}\\Profiles\\");
                foreach (var s in profiles.Where(s => Path.GetExtension(s) == ".xml")) {
                    const string kProfileActions = "ProfileActions";
                    const string kDisconnectController = "Disconnect Controller";

                    xDoc.Load(s);
                    XmlNode root = xDoc.SelectSingleNode("DS4Windows");
                    XmlNode el = root.SelectSingleNode(kProfileActions) ?? xDoc.CreateElement(kProfileActions);
                    if (string.IsNullOrEmpty(el.InnerText))
                        el.InnerText = kDisconnectController;
                    else if (!el.InnerText.Contains("Disconnect Controller"))
                        el.InnerText += $"/{kDisconnectController}";
                    if (el.ParentNode == null) root.AppendChild(el);
                    xDoc.Save(s);
                    LoadActions();
                }
            }
            catch { }
        }

        public static event EventHandler<EventArgs> ControllerStatusChange; // called when a controller is added/removed/battery or touchpad mode changes/etc.
        public static void ControllerStatusChanged(object sender)
        {
            if (ControllerStatusChange != null)
                ControllerStatusChange(sender, EventArgs.Empty);
        }

        public static event EventHandler<BatteryReportArgs> BatteryStatusChange;
        public static void OnBatteryStatusChange(object sender, int index, int level, bool charging)
        {
            if (BatteryStatusChange != null)
            {
                BatteryReportArgs args = new BatteryReportArgs(index, level, charging);
                BatteryStatusChange(sender, args);
            }
        }

        public static event EventHandler<ControllerRemovedArgs> ControllerRemoved;
        public static void OnControllerRemoved(object sender, int index)
        {
            if (ControllerRemoved != null)
            {
                ControllerRemovedArgs args = new ControllerRemovedArgs(index);
                ControllerRemoved(sender, args);
            }
        }

        public event EventHandler<DeviceStatusChangeEventArgs> DeviceStatusChange;
        public void OnDeviceStatusChanged(object sender, int index)
        {
            if (DeviceStatusChange != null)
            {
                DeviceStatusChangeEventArgs args = new DeviceStatusChangeEventArgs(index);
                DeviceStatusChange(sender, args);
            }
        }

        public event EventHandler<SerialChangeArgs> DeviceSerialChange;
        public void OnDeviceSerialChange(object sender, int index, string serial)
        {
            if (DeviceSerialChange != null)
            {
                SerialChangeArgs args = new SerialChangeArgs(index, serial);
                DeviceSerialChange(sender, args);
            }
        }

        public static void SaveAction(string name, string controls, int mode,
            string details, bool edit, string extras = "")
        {
            m_Config.SaveAction(name, controls, mode, details, edit, extras);
            Mapping.actionDone.Add(new Mapping.ActionState());
        }

        public static void RemoveAction(string name)
        {
            m_Config.RemoveAction(name);
        }

        public static bool LoadActions() => config.LoadActions();

        public static List<SpecialAction> GetActions() => m_Config.actions;

        public static int GetActionIndexOf(string name)
        {
            for (int i = 0, actionCount = m_Config.actions.Count; i < actionCount; i++)
            {
                if (m_Config.actions[i].name == name)
                    return i;
            }

            return -1;
        }

        public static int GetProfileActionIndexOf(int device, string name)
        {
            int index = -1;
            cfg[device].profileActionIndexDict.TryGetValue(name, out index);
            return index;
        }

        public static SpecialAction GetAction(string name)
        {
            foreach (SpecialAction sA in m_Config.actions)
            {
                if (sA.name == name)
                    return sA;
            }

            return new SpecialAction("null", "null", "null", "null");
        }

        public static SpecialAction GetProfileAction(int device, string name)
        {
            SpecialAction sA = null;
            cfg[device].profileActionDict.TryGetValue(name, out sA);
            return sA;
        }

        public static void calculateProfileActionDicts(int device)
        {
            cfg[device].profileActionDict.Clear();
            cfg[device].profileActionIndexDict.Clear();

            foreach (string actionname in cfg[device].profileActions)
            {
                cfg[device].profileActionDict[actionname] = GetAction(actionname);
                cfg[device].profileActionIndexDict[actionname] = GetActionIndexOf(actionname);
            }
        }

#if true
        public static void cacheProfileCustomsFlags(int device)
        {
            cfg[device].containsCustomAction = m_Config.HasCustomActions(device);
            cfg[device].containsCustomExtras = m_Config.HasCustomExtras(device);
        }
#endif

        public static X360Controls getX360ControlsByName(string key)
        {
            return m_Config.getX360ControlsByName(key);
        }

        public static string getX360ControlString(X360Controls key)
        {
            return m_Config.getX360ControlString(key);
        }

        public static DS4Controls getDS4ControlsByName(string key)
        {
            return m_Config.getDS4ControlsByName(key);
        }

        public static X360Controls getDefaultX360ControlBinding(DS4Controls dc)
        {
            return defaultButtonMapping[(int)dc];
        }

        public static bool containsLinkedProfile(string serial)
        {
            string tempSerial = serial.Replace(":", string.Empty);
            return m_Config.linkedProfiles.ContainsKey(tempSerial);
        }

        public static string getLinkedProfile(string serial)
        {
            string temp = string.Empty;
            string tempSerial = serial.Replace(":", string.Empty);
            if (m_Config.linkedProfiles.ContainsKey(tempSerial))
            {
                temp = m_Config.linkedProfiles[tempSerial];
            }

            return temp;
        }

        public static void changeLinkedProfile(string serial, string profile)
        {
            string tempSerial = serial.Replace(":", string.Empty);
            m_Config.linkedProfiles[tempSerial] = profile;
        }

        public static void removeLinkedProfile(string serial)
        {
            string tempSerial = serial.Replace(":", string.Empty);
            if (m_Config.linkedProfiles.ContainsKey(tempSerial))
            {
                m_Config.linkedProfiles.Remove(tempSerial);
            }
        }

        public static bool Load() => m_Config.Load();
        
        public static void LoadProfile(int device, bool launchprogram, ControlService control,
            bool xinputChange = true, bool postLoad = true)
        {
            m_Config.LoadProfile(device, launchprogram, control, "", xinputChange, postLoad);
            aux[device].TempProfileName = string.Empty;
            aux[device].UseTempProfile = false;
            aux[device].TempProfileDistance = false;
        }

        public static void LoadTempProfile(int device, string name, bool launchprogram,
            ControlService control, bool xinputChange = true)
        {
            m_Config.LoadProfile(device, launchprogram, control, appdatapath + @"\Profiles\" + name + ".xml");
            aux[device].TempProfileName = name;
            aux[device].UseTempProfile = true;
            aux[device].TempProfileDistance = name.ToLower().Contains("distance");
        }

        public static bool Save()
        {
            return m_Config.Save();
        }

        public static void SaveProfile(int device, string propath)
        {
            m_Config.SaveProfile(device, propath);
        }

        public static bool SaveLinkedProfiles()
        {
            return m_Config.SaveLinkedProfiles();
        }

        public static bool LoadLinkedProfiles()
        {
            return m_Config.LoadLinkedProfiles();
        }

        public static bool SaveControllerConfigs(DS4Device device = null)
        {
            if (device != null)
                return m_Config.SaveControllerConfigsForDevice(device);

            for (int idx = 0; idx < ControlService.DS4_CONTROLLER_COUNT; idx++)
                if (Program.rootHub.DS4Controllers[idx] != null)
                    m_Config.SaveControllerConfigsForDevice(Program.rootHub.DS4Controllers[idx]);

            return true;
        }

        public static bool LoadControllerConfigs(DS4Device device = null)
        {
            if (device != null)
                return m_Config.LoadControllerConfigsForDevice(device);

            for (int idx = 0; idx < ControlService.DS4_CONTROLLER_COUNT; idx++)
                if (Program.rootHub.DS4Controllers[idx] != null)
                    m_Config.LoadControllerConfigsForDevice(Program.rootHub.DS4Controllers[idx]);

            return true;
        }

        private static byte applyRatio(byte b1, byte b2, double r)
        {
            if (r > 100.0)
                r = 100.0;
            else if (r < 0.0)
                r = 0.0;

            r *= 0.01;
            return (byte)Math.Round((b1 * (1 - r)) + b2 * r, 0);
        }

        public static DS4Color getTransitionedColor(ref DS4Color c1, ref DS4Color c2, double ratio)
        {
            //Color cs = Color.FromArgb(c1.red, c1.green, c1.blue);
            DS4Color cs = new DS4Color
            {
                red = applyRatio(c1.red, c2.red, ratio),
                green = applyRatio(c1.green, c2.green, ratio),
                blue = applyRatio(c1.blue, c2.blue, ratio)
            };
            return cs;
        }

        private static Color applyRatio(Color c1, Color c2, uint r)
        {
            float ratio = r / 100f;
            float hue1 = c1.GetHue();
            float hue2 = c2.GetHue();
            float bri1 = c1.GetBrightness();
            float bri2 = c2.GetBrightness();
            float sat1 = c1.GetSaturation();
            float sat2 = c2.GetSaturation();
            float hr = hue2 - hue1;
            float br = bri2 - bri1;
            float sr = sat2 - sat1;
            Color csR;
            if (bri1 == 0)
                csR = HuetoRGB(hue2,sat2,bri2 - br*ratio);
            else
                csR = HuetoRGB(hue2 - hr * ratio, sat2 - sr * ratio, bri2 - br * ratio);

            return csR;
        }

        public static Color HuetoRGB(float hue, float sat, float bri)
        {
            float C = (1-Math.Abs(2*bri)-1)* sat;
            float X = C * (1 - Math.Abs((hue / 60) % 2 - 1));
            float m = bri - C / 2;
            float R, G, B;
            if (0 <= hue && hue < 60)
            {
                R = C; G = X; B = 0;
            }
            else if (60 <= hue && hue < 120)
            {
                R = X; G = C; B = 0;
            }
            else if (120 <= hue && hue < 180)
            {
                R = 0; G = C; B = X;
            }
            else if (180 <= hue && hue < 240)
            {
                R = 0; G = X; B = C;
            }
            else if (240 <= hue && hue < 300)
            {
                R = X; G = 0; B = C;
            }
            else if (300 <= hue && hue < 360)
            {
                R = C; G = 0; B = X;
            }
            else
            {
                R = 255; G = 0; B = 0;
            }

            R += m; G += m; B += m;
            R *= 255.0f; G *= 255.0f; B *= 255.0f;
            return Color.FromArgb((int)R, (int)G, (int)B);
        }

        public static double Clamp(double min, double value, double max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        private static int ClampInt(int min, int value, int max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }
    }

    public class DeviceBackingStore : IDeviceConfig {
    
        public DeviceBackingStore(int device) {
            lsOutCurveMode = 0;
            rsOutCurveMode = 0;
            l2OutCurveMode = 0;
            r2OutCurveMode = 0;
            sxOutCurveMode = 0;
            szOutCurveMode = 0;

            Color tempColor = Color.Blue;
            switch (device)
            {
                case 0: tempColor = Color.Blue; break;
                case 1: tempColor = Color.Red; break;
                case 2: tempColor = Color.Green; break;
                case 3: tempColor = Color.Pink; break;
                case 4: tempColor = Color.White; break;
                default: tempColor = Color.Blue; break;
            }
            MainColor = new DS4Color(tempColor);
        }

        public int BtPollRate { get; set; } = 4;
        public bool FlushHIDQueue { get; set; } = false;
        public int IdleDisconnectTimeout { get; set; } = 0;
        public byte RumbleBoost { get; set; } 100;
        public bool LowerRCOn { get; set; } = false;
        public string profilePath { get; set; } = string.Empty;
        public string olderProfilePath { get; set; } = string.Empty;

        public byte FlashType { get; set; } = 0;
        public int FlashAt { get; set; } = 0;
        public double Rainbow { get; set; } = 0.0;
        public bool LedAsBatteryIndicator { get; set; } = false;

        public DS4Color MainColor { get; set; }
        public DS4Color LowColor { get; set; } = new DS4Color(Color.Black);
        public DS4Color ChargingColor { get; set; } = new DS4Color(Color.Black);
        public DS4Color FlashColor { get; set; } = new DS4Color(Color.Black);
        public bool UseCustomColor { get; set; } = false;
        public DS4Color CustomColor { get; set; } = new DS4Color(Color.Blue);

        public bool EnableTouchToggle { get; set; } = true; // FIXME: resetProfile had it set to false
        public byte TouchSensitivity { get; set; } = 100;
        public bool TouchpadJitterCompensation { get; set; } = true;
        public int TouchpadInvert { get; set; } = 0;
        public bool StartTouchpadOff { get; set; } = false;
        public bool UseTPforControls { get; set; } = false;
        public int[] TouchDisInvertTriggers { get; set; } = { -1 };
        public byte TapSensitivity { get; set; } = 0;
        public bool DoubleTap { get; set; } = false;
        public int ScrollSensitivity { get; set; } = 0;

        public int GyroSensitivity { get; set; } = 100;
        public int GyroSensVerticalScale { get; set; } = 100;
        public int GyroInvert { get; set; } = 0;
        public bool GyroTriggerTurns { get; set; } = true;
        public bool GyroSmoothing { get; set; } = false;
        public double GyroSmoothingWeight { get; set; } = 0.5;
        public int GyroMouseHorizontalAxis { get; set; } = 0;
        public int GyroMouseDeadZone { get; set; } = MouseCursor.GYRO_MOUSE_DEADZONE;
        public bool GyroMouseToggle { get; set; } = false;

        public int ButtonMouseSensitivity { get; set; } = 25;
        public bool MouseAccel { get; set; } = false;

        public bool TrackballMode = false;
        public double TrackballFriction = 10.0;

        public bool DistanceProfiles { get; set; } = false;
        public StickDeadZoneInfo lsModInfo { get; set; } = new StickDeadZoneInfo();
        public StickDeadZoneInfo rsModInfo { get; set; } = new StickDeadZoneInfo();
        public TriggerDeadZoneZInfo l2ModInfo { get; set; } = new TriggerDeadZoneZInfo();
        public TriggerDeadZoneZInfo r2ModInfo { get; set; } = new TriggerDeadZoneZInfo();

        // The private ones are unused/legacy/dead/whatever. They were commented out.
        private byte l2Deadzone = 0, r2Deadzone = 0;
        private int l2AntiDeadzone = 0, r2AntiDeadzone = 0;
        private int l2MaxZone = 100, r2MaxZone = 100;

        public double LSRotation = 0.0, RSRotation = 0.0;
        public double SXDeadzone = 0.25, SZDeadzone = 0.25;
        public double SXMaxzone = 1.0, SZMaxzone = 1.0;
        public double SXAntiDeadzone = 0.0, SZAntiDeadzone = 0.0;
        public double l2Sens = 1.0, r2Sens = 1.0;
        public double LSSens = 1.0, RSSens = 1.0;
        public double SXSens = 1.0, SZSens = 1.0;

        public SquareStickInfo squStickInfo = new SquareStickInfo();


        private void setOutBezierCurveObj(BezierCurve bezierCurve, int curveOptionValue, BezierCurve.AxisType axisType)
        {
            // Set bezier curve obj of axis. 0=Linear (no curve mapping), 1-5=Pre-defined curves, 6=User supplied custom curve string value of a profile (comma separated list of 4 decimal numbers)
            switch (curveOptionValue)
            {
                case 1:
                    bezierCurve.InitBezierCurve(99.0, 99.0, 0.00, 0.00, axisType);
                    break; // Enhanced Precision (hard-coded curve) (The same curve as bezier 0.70, 0.28, 1.00, 1.00)
                case 2:
                    bezierCurve.InitBezierCurve(0.55, 0.09, 0.68, 0.53, axisType);
                    break; // Quadric
                case 3:
                    bezierCurve.InitBezierCurve(0.74, 0.12, 0.64, 0.29, axisType);
                    break; // Cubic
                case 4:
                    bezierCurve.InitBezierCurve(0.00, 0.00, 0.41, 0.96, axisType);
                    break; // Easeout Quad
                case 5:
                    bezierCurve.InitBezierCurve(0.08, 0.22, 0.22, 0.91, axisType);
                    break; // Easeout Cubic
                case 6:
                    bezierCurve.InitBezierCurve(bezierCurve.CustomDefinition, axisType);
                    break; // Custom output curve
            }
        }

        public BezierCurve lsOutBezierCurveObj = new BezierCurve();
        public BezierCurve rsOutBezierCurveObj = new BezierCurve();
        public BezierCurve l2OutBezierCurveObj = new BezierCurve();
        public BezierCurve r2OutBezierCurveObj = new BezierCurve();
        public BezierCurve sxOutBezierCurveObj = new BezierCurve();
        public BezierCurve szOutBezierCurveObj = new BezierCurve();

        private int setNewOutCurveMode(BezierCurve outBezierCurveObj, int value, BezierCurve.AxisType axisType)
        {
            if (value >= 1) setOutBezierCurveObj(outBezierCurveObj, value, axisType);
            return value;
        }

        private int _lsOutCurveMode = 0;
        public int lsOutCurveMode
        {
            get => _lsOutCurveMode;
            set => _lsOutCurveMode = setNewOutCurveMode(lsOutBezierCurveObj, value, BezierCurve.AxisType.LSRS);
        }

        private int _rsOutCurveMode = 0;
        public int rsOutCurveMode
        {
            get => _rsOutCurveMode;
            set => _rsOutCurveMode = setNewOutCurveMode(lsOutBezierCurveObj, value, BezierCurve.AxisType.LSRS);
        }

        private int _l2OutCurveMode = 0;
        public int l2OutCurveMode
        {
            get => _l2OutCurveMode;
            set => _l2OutCurveMode = setNewOutCurveMode(lsOutBezierCurveObj, value, BezierCurve.AxisType.L2R2);
        }

        private int _r2OutCurveMode = 0;
        public int r2OutCurveMode
        {
            get => _r2OutCurveMode;
            set => _r2OutCurveMode = setNewOutCurveMode(lsOutBezierCurveObj, value, BezierCurve.AxisType.L2R2);
        }

        private int _sxOutCurveMode = 0;
        public int sxOutCurveMode
        {
            get => _sxOutCurveMode;
            set => _sxOutCurveMode = setNewOutCurveMode(lsOutBezierCurveObj, value, BezierCurve.AxisType.SA);
        }

        private int _szOutCurveMode = 0;
        public int szOutCurveMode
        {
            get => _szOutCurveMode;
            set => _szOutCurveMode = setNewOutCurveMode(lsOutBezierCurveObj, value, BezierCurve.AxisType.SA);
        }


        public int chargingType = 0;
        public string launchProgram = string.Empty;
        public bool dinputOnly = false;
        public bool useSAforMouse = false;
        public string sATriggers = string.Empty;
        public bool sATriggerCond = true;
        
        public SASteeringWheelEmulationAxisType sASteeringWheelEmulationAxis =
            SASteeringWheelEmulationAxisType.None;
        public int sASteeringWheelEmulationRange = 360;
            

        public int lsCurve = 0;
        public int rsCurve = 0;

        public List<DS4ControlSettings> DS4Settings { get; } = new List<DS4ControlSettings>();

        public List<string> profileActions = null;
        public Dictionary<string, SpecialAction> profileActionDict = new Dictionary<string, SpecialAction>();
        public Dictionary<string, int> profileActionIndexDict = new Dictionary<string, int>();

        // Cache whether profile has custom action
        public bool containsCustomAction = false;
        // Cache whether profile has custom extras
        public bool containsCustomExtras = false;

        public OutContType outputDevType = OutContType.X360;

        public void UpdateDS4CSetting(string buttonName, bool shift, object action, string exts, DS4KeyType kt, int trigger = 0)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = BackingStore.getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = DS4Settings[index];
                dcs.UpdateSettings(shift, action, exts, kt, trigger);
            }
            hasCustomActions = hasCustomExtras = null;
        }

        public void UpdateDS4CExtra(int deviceNum, string buttonName, bool shift, string exts)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = BackingStore.getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = DS4Settings[index];
                if (shift)
                    dcs.shiftExtras = exts;
                else
                    dcs.extras = exts;
            }
            hasCustomActions = hasCustomExtras = null;
        }

        private bool? hasCustomActions;
        private bool? hasCustomExtras;
        public bool HasCustomActions {
            get => checkCustomActionsExtras() && (bool)hasCustomActions;
        }
        public bool HasCustomExtras
        {
            get => checkCustomActionsExtras() && (bool)hasCustomExtras;
        }
 
        private bool checkCustomActionsExtras()
        {
            if (hasCustomActions != null && hasCustomExtras != null) return true;
            bool actions = false, extras = false;
            foreach (var dcs in DS4Settings) {
                actions = actions || dcs.action != null || dcs.shiftAction != null;
                extras = extras || dcs.extras != null || dcs.shiftExtras != null;
                if (actions && extras) break;
            }
            hasCustomActions = actions;
            hasCustomExtras = extras;
            return true;
        }


    }


    public class BackingStore
    {
        //public String m_Profile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Tool" + "\\Profiles.xml";
        public String m_Profile = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName + "\\Profiles.xml";
        public String m_Actions = Global.appdatapath + "\\Actions.xml";
        public string m_linkedProfiles = Global.appdatapath + "\\LinkedProfiles.xml";
        public string m_controllerConfigs = Global.appdatapath + "\\ControllerConfigs.xml";

        protected XmlDocument m_Xdoc = new XmlDocument();
        // fifth value used for options, not fifth controller
        public readonly DeviceBackingStore[] dev =
            Enumerable.Range(0, 4).Select(i => new DeviceBackingStore(i)).ToArray();

        public Dictionary<string, string> linkedProfiles = new Dictionary<string, string>();

        // general values
        public bool useExclusiveMode { get; set; } = false;
        public Int32 formWidth = 782;
        public Int32 formHeight = 550;
        public int formLocationX = 0;
        public int formLocationY = 0;
        public Boolean startMinimized = false;
        public Boolean minToTaskbar = false;
        public DateTime lastChecked { get; set; }
        public int CheckWhen { get; set; } = 1;
        public int Notifications { get; set; } = 2;
        public bool disconnectBTAtStop { get; set; } = false;
        public bool swipeProfiles = true;
        public bool ds4Mapping = false;
        public bool quickCharge = false;
        public bool closeMini = false;
        public List<SpecialAction> actions = new List<SpecialAction>();

        public string useLang = "";
        public bool downloadLang = true;
        public bool useWhiteIcon;
        public bool flashWhenLate = true;
        public int flashWhenLateAt = 20;
        public bool useUDPServ = false;
        public int udpServPort = 26760;
        public string udpServListenAddress = "127.0.0.1"; // 127.0.0.1=IPAddress.Loopback (default), 0.0.0.0=IPAddress.Any as all interfaces, x.x.x.x = Specific ipv4 interface address or hostname
        public bool useCustomSteamFolder;
        public string customSteamFolder;

        bool tempBool = false;

        public BackingStore()
        {
            foreach (var dev in this.dev)
            {
                foreach (DS4Controls dc in Enum.GetValues(typeof(DS4Controls)))
                {
                    if (dc != DS4Controls.None)
                        dev.DS4Settings.Add(new DS4ControlSettings(dc));
                }

                dev.profileActions = new List<string>();
                dev.profileActions.Add("Disconnect Controller");
            }
        }

        private string stickOutputCurveString(int id)
        {
            string result = "linear";
            switch (id)
            {
                case 0: break;
                case 1: result = "enhanced-precision"; break;
                case 2: result = "quadratic"; break;
                case 3: result = "cubic"; break;
                case 4: result = "easeout-quad"; break;
                case 5: result = "easeout-cubic"; break;
                case 6: result = "custom"; break;
                default: break;
            }

            return result;
        }

        private int stickOutputCurveId(string name)
        {
            int id = 0;
            switch (name)
            {
                case "linear": id = 0; break;
                case "enhanced-precision": id = 1; break;
                case "quadratic": id = 2; break;
                case "cubic": id = 3; break;
                case "easeout-quad": id = 4; break;
                case "easeout-cubic": id = 5; break;
                case "custom": id = 6; break;
                default: break;
            }

            return id;
        }

        private string axisOutputCurveString(int id)
        {
            return stickOutputCurveString(id);
        }

        private int axisOutputCurveId(string name)
        {
            return stickOutputCurveId(name);
        }

        private static bool SaTriggerCondValue(string text)
        {
            bool result = true;
            switch (text)
            {
                case "and": result = true; break;
                case "or": result = false; break;
                default: result = true; break;
            }

            return result;
        }

        private static string SaTriggerCondString(bool value)
        {
            string result = value ? "and" : "or";
            return result;
        }
        
        public void SetSaTriggerCond(int index, string text)
        {
            dev[index].sATriggerCond = SaTriggerCondValue(text);
        }

        public void SetGyroMouseDZ(int index, int value, ControlService control)
        {
            dev[index].gyroMouseDeadZone = value;
            if (index < 4 && control.touchPad[index] != null)
                control.touchPad[index].CursorGyroDead = value;
        }

        public void SetGyroMouseToggle(int index, bool value, ControlService control)
        {
            dev[index].gyroMouseToggle = value;
            if (index < 4 && control.touchPad[index] != null)
                control.touchPad[index].ToggleGyroMouse = value;
        }

        private string OutContDeviceString(OutContType id)
        {
            string result = "X360";
            switch (id)
            {
                case OutContType.None:
                case OutContType.X360: result = "X360"; break;
                case OutContType.DS4: result = "DS4"; break;
                default: break;
            }

            return result;
        }

        public static OutContType OutContDeviceId(string name)
        {
            OutContType id = OutContType.X360;
            switch (name)
            {
                case "None":
                case "X360": id = OutContType.X360; break;
                case "DS4": id = OutContType.DS4; break;
                default: break;
            }

            return id;
        }

        public bool SaveProfile(int device, string propath)
        {
            Global.DeviceAuxiliaryConfig aux = Global.aux[device];
            DeviceBackingStore dev = this.dev[device];
            bool Saved = true;
            string path = Global.appdatapath + @"\Profiles\" + Path.GetFileNameWithoutExtension(propath) + ".xml";
            try
            {
                XmlNode Node;
                XmlNode xmlControls = m_Xdoc.SelectSingleNode("/DS4Windows/Control");
                XmlNode xmlShiftControls = m_Xdoc.SelectSingleNode("/DS4Windows/ShiftControl");
                m_Xdoc.RemoveAll();

                Node = m_Xdoc.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
                m_Xdoc.AppendChild(Node);

                Node = m_Xdoc.CreateComment(string.Format(" DS4Windows Configuration Data. {0} ", DateTime.Now));
                m_Xdoc.AppendChild(Node);

                Node = m_Xdoc.CreateWhitespace("\r\n");
                m_Xdoc.AppendChild(Node);

                Node = m_Xdoc.CreateNode(XmlNodeType.Element, "DS4Windows", null);

                XmlNode xmlFlushHIDQueue = m_Xdoc.CreateNode(XmlNodeType.Element, "flushHIDQueue", null); xmlFlushHIDQueue.InnerText = dev.flushHIDQueue.ToString(); Node.AppendChild(xmlFlushHIDQueue);
                XmlNode xmlTouchToggle = m_Xdoc.CreateNode(XmlNodeType.Element, "touchToggle", null); xmlTouchToggle.InnerText = dev.enableTouchToggle.ToString(); Node.AppendChild(xmlTouchToggle);
                XmlNode xmlIdleDisconnectTimeout = m_Xdoc.CreateNode(XmlNodeType.Element, "idleDisconnectTimeout", null); xmlIdleDisconnectTimeout.InnerText = dev.idleDisconnectTimeout.ToString(); Node.AppendChild(xmlIdleDisconnectTimeout);
                XmlNode xmlColor = m_Xdoc.CreateNode(XmlNodeType.Element, "Color", null);
                xmlColor.InnerText = dev.mainColor.toXMLText();
                Node.AppendChild(xmlColor);
                XmlNode xmlRumbleBoost = m_Xdoc.CreateNode(XmlNodeType.Element, "RumbleBoost", null); xmlRumbleBoost.InnerText = dev.rumbleBoost.ToString(); Node.AppendChild(xmlRumbleBoost);
                XmlNode xmlLedAsBatteryIndicator = m_Xdoc.CreateNode(XmlNodeType.Element, "ledAsBatteryIndicator", null); xmlLedAsBatteryIndicator.InnerText = dev.ledAsBatteryIndicator.ToString(); Node.AppendChild(xmlLedAsBatteryIndicator);
                XmlNode xmlLowBatteryFlash = m_Xdoc.CreateNode(XmlNodeType.Element, "FlashType", null); xmlLowBatteryFlash.InnerText = dev.flashType.ToString(); Node.AppendChild(xmlLowBatteryFlash);
                XmlNode xmlFlashBatterAt = m_Xdoc.CreateNode(XmlNodeType.Element, "flashBatteryAt", null); xmlFlashBatterAt.InnerText = dev.flashAt.ToString(); Node.AppendChild(xmlFlashBatterAt);
                XmlNode xmlTouchSensitivity = m_Xdoc.CreateNode(XmlNodeType.Element, "touchSensitivity", null); xmlTouchSensitivity.InnerText = dev.touchSensitivity.ToString(); Node.AppendChild(xmlTouchSensitivity);
                XmlNode xmlLowColor = m_Xdoc.CreateNode(XmlNodeType.Element, "LowColor", null);
                xmlLowColor.InnerText = dev.lowColor.toXMLText();
                Node.AppendChild(xmlLowColor);
                XmlNode xmlChargingColor = m_Xdoc.CreateNode(XmlNodeType.Element, "ChargingColor", null);
                xmlChargingColor.InnerText = dev.chargingColor.toXMLText();
                Node.AppendChild(xmlChargingColor);
                XmlNode xmlFlashColor = m_Xdoc.CreateNode(XmlNodeType.Element, "FlashColor", null);
                xmlFlashColor.InnerText = dev.flashColor.toXMLText();
                Node.AppendChild(xmlFlashColor);
                XmlNode xmlTouchpadJitterCompensation = m_Xdoc.CreateNode(XmlNodeType.Element, "touchpadJitterCompensation", null); xmlTouchpadJitterCompensation.InnerText = dev.touchpadJitterCompensation.ToString(); Node.AppendChild(xmlTouchpadJitterCompensation);
                XmlNode xmlLowerRCOn = m_Xdoc.CreateNode(XmlNodeType.Element, "lowerRCOn", null); xmlLowerRCOn.InnerText = dev.lowerRCOn.ToString(); Node.AppendChild(xmlLowerRCOn);
                XmlNode xmlTapSensitivity = m_Xdoc.CreateNode(XmlNodeType.Element, "tapSensitivity", null); xmlTapSensitivity.InnerText = dev.tapSensitivity.ToString(); Node.AppendChild(xmlTapSensitivity);
                XmlNode xmlDouble = m_Xdoc.CreateNode(XmlNodeType.Element, "doubleTap", null); xmlDouble.InnerText = dev.doubleTap.ToString(); Node.AppendChild(xmlDouble);
                XmlNode xmlScrollSensitivity = m_Xdoc.CreateNode(XmlNodeType.Element, "scrollSensitivity", null); xmlScrollSensitivity.InnerText = dev.scrollSensitivity.ToString(); Node.AppendChild(xmlScrollSensitivity);
                XmlNode xmlLeftTriggerMiddle = m_Xdoc.CreateNode(XmlNodeType.Element, "LeftTriggerMiddle", null); xmlLeftTriggerMiddle.InnerText = dev.l2ModInfo.deadZone.ToString(); Node.AppendChild(xmlLeftTriggerMiddle);
                XmlNode xmlRightTriggerMiddle = m_Xdoc.CreateNode(XmlNodeType.Element, "RightTriggerMiddle", null); xmlRightTriggerMiddle.InnerText = dev.r2ModInfo.deadZone.ToString(); Node.AppendChild(xmlRightTriggerMiddle);
                XmlNode xmlTouchpadInvert = m_Xdoc.CreateNode(XmlNodeType.Element, "TouchpadInvert", null); xmlTouchpadInvert.InnerText = dev.touchpadInvert.ToString(); Node.AppendChild(xmlTouchpadInvert);
                XmlNode xmlL2AD = m_Xdoc.CreateNode(XmlNodeType.Element, "L2AntiDeadZone", null); xmlL2AD.InnerText = dev.l2ModInfo.antiDeadZone.ToString(); Node.AppendChild(xmlL2AD);
                XmlNode xmlR2AD = m_Xdoc.CreateNode(XmlNodeType.Element, "R2AntiDeadZone", null); xmlR2AD.InnerText = dev.r2ModInfo.antiDeadZone.ToString(); Node.AppendChild(xmlR2AD);
                XmlNode xmlL2Maxzone = m_Xdoc.CreateNode(XmlNodeType.Element, "L2MaxZone", null); xmlL2Maxzone.InnerText = dev.l2ModInfo.maxZone.ToString(); Node.AppendChild(xmlL2Maxzone);
                XmlNode xmlR2Maxzone = m_Xdoc.CreateNode(XmlNodeType.Element, "R2MaxZone", null); xmlR2Maxzone.InnerText = dev.r2ModInfo.maxZone.ToString(); Node.AppendChild(xmlR2Maxzone);
                XmlNode xmlButtonMouseSensitivity = m_Xdoc.CreateNode(XmlNodeType.Element, "buttonMouseSensitivity", null); xmlButtonMouseSensitivity.InnerText = dev.buttonMouseSensitivity.ToString(); Node.AppendChild(xmlButtonMouseSensitivity);
                XmlNode xmlRainbow = m_Xdoc.CreateNode(XmlNodeType.Element, "Rainbow", null); xmlRainbow.InnerText = dev.rainbow.ToString(); Node.AppendChild(xmlRainbow);
                XmlNode xmlLSD = m_Xdoc.CreateNode(XmlNodeType.Element, "LSDeadZone", null); xmlLSD.InnerText = dev.lsModInfo.deadZone.ToString(); Node.AppendChild(xmlLSD);
                XmlNode xmlRSD = m_Xdoc.CreateNode(XmlNodeType.Element, "RSDeadZone", null); xmlRSD.InnerText = dev.rsModInfo.deadZone.ToString(); Node.AppendChild(xmlRSD);
                XmlNode xmlLSAD = m_Xdoc.CreateNode(XmlNodeType.Element, "LSAntiDeadZone", null); xmlLSAD.InnerText = dev.lsModInfo.antiDeadZone.ToString(); Node.AppendChild(xmlLSAD);
                XmlNode xmlRSAD = m_Xdoc.CreateNode(XmlNodeType.Element, "RSAntiDeadZone", null); xmlRSAD.InnerText = dev.rsModInfo.antiDeadZone.ToString(); Node.AppendChild(xmlRSAD);
                XmlNode xmlLSMaxZone = m_Xdoc.CreateNode(XmlNodeType.Element, "LSMaxZone", null); xmlLSMaxZone.InnerText = dev.lsModInfo.maxZone.ToString(); Node.AppendChild(xmlLSMaxZone);
                XmlNode xmlRSMaxZone = m_Xdoc.CreateNode(XmlNodeType.Element, "RSMaxZone", null); xmlRSMaxZone.InnerText = dev.rsModInfo.maxZone.ToString(); Node.AppendChild(xmlRSMaxZone);
                XmlNode xmlLSRotation = m_Xdoc.CreateNode(XmlNodeType.Element, "LSRotation", null); xmlLSRotation.InnerText = Convert.ToInt32(dev.LSRotation * 180.0 / Math.PI).ToString(); Node.AppendChild(xmlLSRotation);
                XmlNode xmlRSRotation = m_Xdoc.CreateNode(XmlNodeType.Element, "RSRotation", null); xmlRSRotation.InnerText = Convert.ToInt32(dev.RSRotation * 180.0 / Math.PI).ToString(); Node.AppendChild(xmlRSRotation);

                XmlNode xmlSXD = m_Xdoc.CreateNode(XmlNodeType.Element, "SXDeadZone", null); xmlSXD.InnerText = dev.SXDeadzone.ToString(); Node.AppendChild(xmlSXD);
                XmlNode xmlSZD = m_Xdoc.CreateNode(XmlNodeType.Element, "SZDeadZone", null); xmlSZD.InnerText = dev.SZDeadzone.ToString(); Node.AppendChild(xmlSZD);

                XmlNode xmlSXMaxzone = m_Xdoc.CreateNode(XmlNodeType.Element, "SXMaxZone", null); xmlSXMaxzone.InnerText = Convert.ToInt32(dev.SXMaxzone * 100.0).ToString(); Node.AppendChild(xmlSXMaxzone);
                XmlNode xmlSZMaxzone = m_Xdoc.CreateNode(XmlNodeType.Element, "SZMaxZone", null); xmlSZMaxzone.InnerText = Convert.ToInt32(dev.SZMaxzone * 100.0).ToString(); Node.AppendChild(xmlSZMaxzone);

                XmlNode xmlSXAntiDeadzone = m_Xdoc.CreateNode(XmlNodeType.Element, "SXAntiDeadZone", null); xmlSXAntiDeadzone.InnerText = Convert.ToInt32(dev.SXAntiDeadzone * 100.0).ToString(); Node.AppendChild(xmlSXAntiDeadzone);
                XmlNode xmlSZAntiDeadzone = m_Xdoc.CreateNode(XmlNodeType.Element, "SZAntiDeadZone", null); xmlSZAntiDeadzone.InnerText = Convert.ToInt32(dev.SZAntiDeadzone * 100.0).ToString(); Node.AppendChild(xmlSZAntiDeadzone);

                XmlNode xmlSens = m_Xdoc.CreateNode(XmlNodeType.Element, "Sensitivity", null);
                xmlSens.InnerText = $"{dev.LSSens}|{dev.RSSens}|{dev.l2Sens}|{dev.r2Sens}|{dev.SXSens}|{dev.SZSens}";
                Node.AppendChild(xmlSens);

                XmlNode xmlChargingType = m_Xdoc.CreateNode(XmlNodeType.Element, "ChargingType", null); xmlChargingType.InnerText = dev.chargingType.ToString(); Node.AppendChild(xmlChargingType);
                XmlNode xmlMouseAccel = m_Xdoc.CreateNode(XmlNodeType.Element, "MouseAcceleration", null); xmlMouseAccel.InnerText = dev.mouseAccel.ToString(); Node.AppendChild(xmlMouseAccel);
                //XmlNode xmlShiftMod = m_Xdoc.CreateNode(XmlNodeType.Element, "ShiftModifier", null); xmlShiftMod.InnerText = shiftModifier[device].ToString(); Node.AppendChild(xmlShiftMod);
                XmlNode xmlLaunchProgram = m_Xdoc.CreateNode(XmlNodeType.Element, "LaunchProgram", null); xmlLaunchProgram.InnerText = dev.launchProgram.ToString(); Node.AppendChild(xmlLaunchProgram);
                XmlNode xmlDinput = m_Xdoc.CreateNode(XmlNodeType.Element, "DinputOnly", null); xmlDinput.InnerText = dev.dinputOnly.ToString(); Node.AppendChild(xmlDinput);
                XmlNode xmlStartTouchpadOff = m_Xdoc.CreateNode(XmlNodeType.Element, "StartTouchpadOff", null); xmlStartTouchpadOff.InnerText = dev.startTouchpadOff.ToString(); Node.AppendChild(xmlStartTouchpadOff);
                XmlNode xmlUseTPforControls = m_Xdoc.CreateNode(XmlNodeType.Element, "UseTPforControls", null); xmlUseTPforControls.InnerText = dev.useTPforControls.ToString(); Node.AppendChild(xmlUseTPforControls);
                XmlNode xmlUseSAforMouse = m_Xdoc.CreateNode(XmlNodeType.Element, "UseSAforMouse", null); xmlUseSAforMouse.InnerText = dev.useSAforMouse.ToString(); Node.AppendChild(xmlUseSAforMouse);
                XmlNode xmlSATriggers = m_Xdoc.CreateNode(XmlNodeType.Element, "SATriggers", null); xmlSATriggers.InnerText = dev.sATriggers.ToString(); Node.AppendChild(xmlSATriggers);
                XmlNode xmlSATriggerCond = m_Xdoc.CreateNode(XmlNodeType.Element, "SATriggerCond", null); xmlSATriggerCond.InnerText = SaTriggerCondString(dev.sATriggerCond); Node.AppendChild(xmlSATriggerCond);
                XmlNode xmlSASteeringWheelEmulationAxis = m_Xdoc.CreateNode(XmlNodeType.Element, "SASteeringWheelEmulationAxis", null); xmlSASteeringWheelEmulationAxis.InnerText = dev.sASteeringWheelEmulationAxis.ToString("G"); Node.AppendChild(xmlSASteeringWheelEmulationAxis);
                XmlNode xmlSASteeringWheelEmulationRange = m_Xdoc.CreateNode(XmlNodeType.Element, "SASteeringWheelEmulationRange", null); xmlSASteeringWheelEmulationRange.InnerText = dev.sASteeringWheelEmulationRange.ToString(); Node.AppendChild(xmlSASteeringWheelEmulationRange);

                XmlNode xmlTouchDisInvTriggers = m_Xdoc.CreateNode(XmlNodeType.Element, "TouchDisInvTriggers", null);
                string tempTouchDisInv = string.Join(",", dev.touchDisInvertTriggers);
                xmlTouchDisInvTriggers.InnerText = tempTouchDisInv;
                Node.AppendChild(xmlTouchDisInvTriggers);

                XmlNode xmlGyroSensitivity = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroSensitivity", null); xmlGyroSensitivity.InnerText = dev.gyroSensitivity.ToString(); Node.AppendChild(xmlGyroSensitivity);
                XmlNode xmlGyroSensVerticalScale = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroSensVerticalScale", null); xmlGyroSensVerticalScale.InnerText = dev.gyroSensVerticalScale.ToString(); Node.AppendChild(xmlGyroSensVerticalScale);
                XmlNode xmlGyroInvert = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroInvert", null); xmlGyroInvert.InnerText = dev.gyroInvert.ToString(); Node.AppendChild(xmlGyroInvert);
                XmlNode xmlGyroTriggerTurns = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroTriggerTurns", null); xmlGyroTriggerTurns.InnerText = dev.gyroTriggerTurns.ToString(); Node.AppendChild(xmlGyroTriggerTurns);
                XmlNode xmlGyroSmoothWeight = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroSmoothingWeight", null); xmlGyroSmoothWeight.InnerText = Convert.ToInt32(dev.gyroSmoothingWeight* 100).ToString(); Node.AppendChild(xmlGyroSmoothWeight);
                XmlNode xmlGyroSmoothing = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroSmoothing", null); xmlGyroSmoothing.InnerText = dev.gyroSmoothing.ToString(); Node.AppendChild(xmlGyroSmoothing);
                XmlNode xmlGyroMouseHAxis = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroMouseHAxis", null); xmlGyroMouseHAxis.InnerText = dev.gyroMouseHorizontalAxis.ToString(); Node.AppendChild(xmlGyroMouseHAxis);
                XmlNode xmlGyroMouseDZ = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroMouseDeadZone", null); xmlGyroMouseDZ.InnerText = dev.gyroMouseDeadZone.ToString(); Node.AppendChild(xmlGyroMouseDZ);
                XmlNode xmlGyroMouseToggle = m_Xdoc.CreateNode(XmlNodeType.Element, "GyroMouseToggle", null); xmlGyroMouseToggle.InnerText = dev.gyroMouseToggle.ToString(); Node.AppendChild(xmlGyroMouseToggle);
                XmlNode xmlLSC = m_Xdoc.CreateNode(XmlNodeType.Element, "LSCurve", null); xmlLSC.InnerText = dev.lsCurve.ToString(); Node.AppendChild(xmlLSC);
                XmlNode xmlRSC = m_Xdoc.CreateNode(XmlNodeType.Element, "RSCurve", null); xmlRSC.InnerText = dev.rsCurve.ToString(); Node.AppendChild(xmlRSC);
                XmlNode xmlProfileActions = m_Xdoc.CreateNode(XmlNodeType.Element, "ProfileActions", null); xmlProfileActions.InnerText = string.Join("/", dev.profileActions); Node.AppendChild(xmlProfileActions);
                XmlNode xmlBTPollRate = m_Xdoc.CreateNode(XmlNodeType.Element, "BTPollRate", null); xmlBTPollRate.InnerText = dev.btPollRate.ToString(); Node.AppendChild(xmlBTPollRate);

                XmlNode xmlLsOutputCurveMode = m_Xdoc.CreateNode(XmlNodeType.Element, "LSOutputCurveMode", null); xmlLsOutputCurveMode.InnerText = stickOutputCurveString(dev.lsOutCurveMode); Node.AppendChild(xmlLsOutputCurveMode);
                XmlNode xmlLsOutputCurveCustom  = m_Xdoc.CreateNode(XmlNodeType.Element, "LSOutputCurveCustom", null); xmlLsOutputCurveCustom.InnerText = dev.lsOutBezierCurveObj.ToString(); Node.AppendChild(xmlLsOutputCurveCustom);

                XmlNode xmlRsOutputCurveMode = m_Xdoc.CreateNode(XmlNodeType.Element, "RSOutputCurveMode", null); xmlRsOutputCurveMode.InnerText = stickOutputCurveString(dev.rsOutCurveMode); Node.AppendChild(xmlRsOutputCurveMode);
                XmlNode xmlRsOutputCurveCustom = m_Xdoc.CreateNode(XmlNodeType.Element, "RSOutputCurveCustom", null); xmlRsOutputCurveCustom.InnerText = dev.rsOutBezierCurveObj.ToString(); Node.AppendChild(xmlRsOutputCurveCustom);

                XmlNode xmlLsSquareStickMode = m_Xdoc.CreateNode(XmlNodeType.Element, "LSSquareStick", null); xmlLsSquareStickMode.InnerText = dev.squStickInfo.lsMode.ToString(); Node.AppendChild(xmlLsSquareStickMode);
                XmlNode xmlRsSquareStickMode = m_Xdoc.CreateNode(XmlNodeType.Element, "RSSquareStick", null); xmlRsSquareStickMode.InnerText = dev.squStickInfo.rsMode.ToString(); Node.AppendChild(xmlRsSquareStickMode);

                XmlNode xmlSquareStickRoundness = m_Xdoc.CreateNode(XmlNodeType.Element, "SquareStickRoundness", null); xmlSquareStickRoundness.InnerText = dev.squStickInfo.roundness.ToString(); Node.AppendChild(xmlSquareStickRoundness);

                XmlNode xmlL2OutputCurveMode = m_Xdoc.CreateNode(XmlNodeType.Element, "L2OutputCurveMode", null); xmlL2OutputCurveMode.InnerText = axisOutputCurveString(dev.l2OutCurveMode); Node.AppendChild(xmlL2OutputCurveMode);
                XmlNode xmlL2OutputCurveCustom = m_Xdoc.CreateNode(XmlNodeType.Element, "L2OutputCurveCustom", null); xmlL2OutputCurveCustom.InnerText = dev.l2OutBezierCurveObj.ToString(); Node.AppendChild(xmlL2OutputCurveCustom);

                XmlNode xmlR2OutputCurveMode = m_Xdoc.CreateNode(XmlNodeType.Element, "R2OutputCurveMode", null); xmlR2OutputCurveMode.InnerText = axisOutputCurveString(dev.r2OutCurveMode); Node.AppendChild(xmlR2OutputCurveMode);
                XmlNode xmlR2OutputCurveCustom = m_Xdoc.CreateNode(XmlNodeType.Element, "R2OutputCurveCustom", null); xmlR2OutputCurveCustom.InnerText = dev.r2OutBezierCurveObj.ToString(); Node.AppendChild(xmlR2OutputCurveCustom);

                XmlNode xmlSXOutputCurveMode = m_Xdoc.CreateNode(XmlNodeType.Element, "SXOutputCurveMode", null); xmlSXOutputCurveMode.InnerText = axisOutputCurveString(dev.sxOutCurveMode); Node.AppendChild(xmlSXOutputCurveMode);
                XmlNode xmlSXOutputCurveCustom = m_Xdoc.CreateNode(XmlNodeType.Element, "SXOutputCurveCustom", null); xmlSXOutputCurveCustom.InnerText = dev.sxOutBezierCurveObj.ToString(); Node.AppendChild(xmlSXOutputCurveCustom);

                XmlNode xmlSZOutputCurveMode = m_Xdoc.CreateNode(XmlNodeType.Element, "SZOutputCurveMode", null); xmlSZOutputCurveMode.InnerText = axisOutputCurveString(dev.szOutCurveMode); Node.AppendChild(xmlSZOutputCurveMode);
                XmlNode xmlSZOutputCurveCustom = m_Xdoc.CreateNode(XmlNodeType.Element, "SZOutputCurveCustom", null); xmlSZOutputCurveCustom.InnerText = dev.szOutBezierCurveObj.ToString(); Node.AppendChild(xmlSZOutputCurveCustom);

                XmlNode xmlTrackBallMode = m_Xdoc.CreateNode(XmlNodeType.Element, "TrackballMode", null); xmlTrackBallMode.InnerText = dev.trackballMode.ToString(); Node.AppendChild(xmlTrackBallMode);
                XmlNode xmlTrackBallFriction = m_Xdoc.CreateNode(XmlNodeType.Element, "TrackballFriction", null); xmlTrackBallFriction.InnerText = dev.trackballFriction.ToString(); Node.AppendChild(xmlTrackBallFriction);

                XmlNode xmlOutContDevice = m_Xdoc.CreateNode(XmlNodeType.Element, "OutputContDevice", null); xmlOutContDevice.InnerText = OutContDeviceString(dev.outputDevType); Node.AppendChild(xmlOutContDevice);

                XmlNode NodeControl = m_Xdoc.CreateNode(XmlNodeType.Element, "Control", null);
                XmlNode Key = m_Xdoc.CreateNode(XmlNodeType.Element, "Key", null);
                XmlNode Macro = m_Xdoc.CreateNode(XmlNodeType.Element, "Macro", null);
                XmlNode KeyType = m_Xdoc.CreateNode(XmlNodeType.Element, "KeyType", null);
                XmlNode Button = m_Xdoc.CreateNode(XmlNodeType.Element, "Button", null);
                XmlNode Extras = m_Xdoc.CreateNode(XmlNodeType.Element, "Extras", null);

                XmlNode NodeShiftControl = m_Xdoc.CreateNode(XmlNodeType.Element, "ShiftControl", null);

                XmlNode ShiftKey = m_Xdoc.CreateNode(XmlNodeType.Element, "Key", null);
                XmlNode ShiftMacro = m_Xdoc.CreateNode(XmlNodeType.Element, "Macro", null);
                XmlNode ShiftKeyType = m_Xdoc.CreateNode(XmlNodeType.Element, "KeyType", null);
                XmlNode ShiftButton = m_Xdoc.CreateNode(XmlNodeType.Element, "Button", null);
                XmlNode ShiftExtras = m_Xdoc.CreateNode(XmlNodeType.Element, "Extras", null);

                foreach (DS4ControlSettings dcs in dev.DS4Settings)
                {
                    if (dcs.action != null)
                    {
                        XmlNode buttonNode;
                        string keyType = string.Empty;

                        if (dcs.action is string)
                        {
                            if (dcs.action.ToString() == "Unbound")
                                keyType += DS4KeyType.Unbound;
                        }

                        if (dcs.keyType.HasFlag(DS4KeyType.HoldMacro))
                            keyType += DS4KeyType.HoldMacro;
                        else if (dcs.keyType.HasFlag(DS4KeyType.Macro))
                            keyType += DS4KeyType.Macro;

                        if (dcs.keyType.HasFlag(DS4KeyType.Toggle))
                            keyType += DS4KeyType.Toggle;
                        if (dcs.keyType.HasFlag(DS4KeyType.ScanCode))
                            keyType += DS4KeyType.ScanCode;

                        if (keyType != string.Empty)
                        {
                            buttonNode = m_Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
                            buttonNode.InnerText = keyType;
                            KeyType.AppendChild(buttonNode);
                        }

                        buttonNode = m_Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
                        if (dcs.action is IEnumerable<int> || dcs.action is int[] || dcs.action is ushort[])
                        {
                            int[] ii = (int[])dcs.action;
                            buttonNode.InnerText = string.Join("/", ii);
                            Macro.AppendChild(buttonNode);
                        }
                        else if (dcs.action is int || dcs.action is ushort || dcs.action is byte)
                        {
                            buttonNode.InnerText = dcs.action.ToString();
                            Key.AppendChild(buttonNode);
                        }
                        else if (dcs.action is string)
                        {
                            buttonNode.InnerText = dcs.action.ToString();
                            Button.AppendChild(buttonNode);
                        }
                        else if (dcs.action is X360Controls)
                        {
                            buttonNode.InnerText = getX360ControlString((X360Controls)dcs.action);
                            Button.AppendChild(buttonNode);
                        }
                    }

                    bool hasvalue = false;
                    if (!string.IsNullOrEmpty(dcs.extras))
                    {
                        foreach (string s in dcs.extras.Split(','))
                        {
                            if (s != "0")
                            {
                                hasvalue = true;
                                break;
                            }
                        }
                    }

                    if (hasvalue)
                    {
                        XmlNode extraNode = m_Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
                        extraNode.InnerText = dcs.extras;
                        Extras.AppendChild(extraNode);
                    }

                    if (dcs.shiftAction != null && dcs.shiftTrigger > 0)
                    {
                        XmlElement buttonNode;
                        string keyType = string.Empty;

                        if (dcs.shiftAction is string)
                        {
                            if (dcs.shiftAction.ToString() == "Unbound")
                                keyType += DS4KeyType.Unbound;
                        }

                        if (dcs.shiftKeyType.HasFlag(DS4KeyType.HoldMacro))
                            keyType += DS4KeyType.HoldMacro;
                        if (dcs.shiftKeyType.HasFlag(DS4KeyType.Macro))
                            keyType += DS4KeyType.Macro;
                        if (dcs.shiftKeyType.HasFlag(DS4KeyType.Toggle))
                            keyType += DS4KeyType.Toggle;
                        if (dcs.shiftKeyType.HasFlag(DS4KeyType.ScanCode))
                            keyType += DS4KeyType.ScanCode;

                        if (keyType != string.Empty)
                        {
                            buttonNode = m_Xdoc.CreateElement(dcs.control.ToString());
                            buttonNode.InnerText = keyType;
                            ShiftKeyType.AppendChild(buttonNode);
                        }

                        buttonNode = m_Xdoc.CreateElement(dcs.control.ToString());
                        buttonNode.SetAttribute("Trigger", dcs.shiftTrigger.ToString());
                        if (dcs.shiftAction is IEnumerable<int> || dcs.shiftAction is int[] || dcs.shiftAction is ushort[])
                        {
                            int[] ii = (int[])dcs.shiftAction;
                            buttonNode.InnerText = string.Join("/", ii);
                            ShiftMacro.AppendChild(buttonNode);
                        }
                        else if (dcs.shiftAction is int || dcs.shiftAction is ushort || dcs.shiftAction is byte)
                        {
                            buttonNode.InnerText = dcs.shiftAction.ToString();
                            ShiftKey.AppendChild(buttonNode);
                        }
                        else if (dcs.shiftAction is string || dcs.shiftAction is X360Controls)
                        {
                            buttonNode.InnerText = dcs.shiftAction.ToString();
                            ShiftButton.AppendChild(buttonNode);
                        }
                    }

                    hasvalue = false;
                    if (!string.IsNullOrEmpty(dcs.shiftExtras))
                    {
                        foreach (string s in dcs.shiftExtras.Split(','))
                        {
                            if (s != "0")
                            {
                                hasvalue = true;
                                break;
                            }
                        }
                    }

                    if (hasvalue)
                    {
                        XmlNode extraNode = m_Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
                        extraNode.InnerText = dcs.shiftExtras;
                        ShiftExtras.AppendChild(extraNode);
                    }
                }

                Node.AppendChild(NodeControl);
                if (Button.HasChildNodes)
                    NodeControl.AppendChild(Button);
                if (Macro.HasChildNodes)
                    NodeControl.AppendChild(Macro);
                if (Key.HasChildNodes)
                    NodeControl.AppendChild(Key);
                if (Extras.HasChildNodes)
                    NodeControl.AppendChild(Extras);
                if (KeyType.HasChildNodes)
                    NodeControl.AppendChild(KeyType);
                if (NodeControl.HasChildNodes)
                    Node.AppendChild(NodeControl);

                Node.AppendChild(NodeShiftControl);
                if (ShiftButton.HasChildNodes)
                    NodeShiftControl.AppendChild(ShiftButton);
                if (ShiftMacro.HasChildNodes)
                    NodeShiftControl.AppendChild(ShiftMacro);
                if (ShiftKey.HasChildNodes)
                    NodeShiftControl.AppendChild(ShiftKey);
                if (ShiftKeyType.HasChildNodes)
                    NodeShiftControl.AppendChild(ShiftKeyType);
                if (ShiftExtras.HasChildNodes)
                    NodeShiftControl.AppendChild(ShiftExtras);
                
                m_Xdoc.AppendChild(Node);
                m_Xdoc.Save(path);
            }
            catch { Saved = false; }
            return Saved;
        }

        public static DS4Controls getDS4ControlsByName(string key)
        {
            if (!key.StartsWith("bn"))
                return (DS4Controls)Enum.Parse(typeof(DS4Controls), key, true);

            switch (key)
            {
                case "bnShare": return DS4Controls.Share;
                case "bnL3": return DS4Controls.L3;
                case "bnR3": return DS4Controls.R3;
                case "bnOptions": return DS4Controls.Options;
                case "bnUp": return DS4Controls.DpadUp;
                case "bnRight": return DS4Controls.DpadRight;
                case "bnDown": return DS4Controls.DpadDown;
                case "bnLeft": return DS4Controls.DpadLeft;

                case "bnL1": return DS4Controls.L1;
                case "bnR1": return DS4Controls.R1;
                case "bnTriangle": return DS4Controls.Triangle;
                case "bnCircle": return DS4Controls.Circle;
                case "bnCross": return DS4Controls.Cross;
                case "bnSquare": return DS4Controls.Square;

                case "bnPS": return DS4Controls.PS;
                case "bnLSLeft": return DS4Controls.LXNeg;
                case "bnLSUp": return DS4Controls.LYNeg;
                case "bnRSLeft": return DS4Controls.RXNeg;
                case "bnRSUp": return DS4Controls.RYNeg;

                case "bnLSRight": return DS4Controls.LXPos;
                case "bnLSDown": return DS4Controls.LYPos;
                case "bnRSRight": return DS4Controls.RXPos;
                case "bnRSDown": return DS4Controls.RYPos;
                case "bnL2": return DS4Controls.L2;
                case "bnR2": return DS4Controls.R2;

                case "bnTouchLeft": return DS4Controls.TouchLeft;
                case "bnTouchMulti": return DS4Controls.TouchMulti;
                case "bnTouchUpper": return DS4Controls.TouchUpper;
                case "bnTouchRight": return DS4Controls.TouchRight;
                case "bnGyroXP": return DS4Controls.GyroXPos;
                case "bnGyroXN": return DS4Controls.GyroXNeg;
                case "bnGyroZP": return DS4Controls.GyroZPos;
                case "bnGyroZN": return DS4Controls.GyroZNeg;

                case "bnSwipeUp": return DS4Controls.SwipeUp;
                case "bnSwipeDown": return DS4Controls.SwipeDown;
                case "bnSwipeLeft": return DS4Controls.SwipeLeft;
                case "bnSwipeRight": return DS4Controls.SwipeRight;

#region OldShiftname
                case "sbnShare": return DS4Controls.Share;
                case "sbnL3": return DS4Controls.L3;
                case "sbnR3": return DS4Controls.R3;
                case "sbnOptions": return DS4Controls.Options;
                case "sbnUp": return DS4Controls.DpadUp;
                case "sbnRight": return DS4Controls.DpadRight;
                case "sbnDown": return DS4Controls.DpadDown;
                case "sbnLeft": return DS4Controls.DpadLeft;

                case "sbnL1": return DS4Controls.L1;
                case "sbnR1": return DS4Controls.R1;
                case "sbnTriangle": return DS4Controls.Triangle;
                case "sbnCircle": return DS4Controls.Circle;
                case "sbnCross": return DS4Controls.Cross;
                case "sbnSquare": return DS4Controls.Square;

                case "sbnPS": return DS4Controls.PS;
                case "sbnLSLeft": return DS4Controls.LXNeg;
                case "sbnLSUp": return DS4Controls.LYNeg;
                case "sbnRSLeft": return DS4Controls.RXNeg;
                case "sbnRSUp": return DS4Controls.RYNeg;

                case "sbnLSRight": return DS4Controls.LXPos;
                case "sbnLSDown": return DS4Controls.LYPos;
                case "sbnRSRight": return DS4Controls.RXPos;
                case "sbnRSDown": return DS4Controls.RYPos;
                case "sbnL2": return DS4Controls.L2;
                case "sbnR2": return DS4Controls.R2;

                case "sbnTouchLeft": return DS4Controls.TouchLeft;
                case "sbnTouchMulti": return DS4Controls.TouchMulti;
                case "sbnTouchUpper": return DS4Controls.TouchUpper;
                case "sbnTouchRight": return DS4Controls.TouchRight;
                case "sbnGsyroXP": return DS4Controls.GyroXPos;
                case "sbnGyroXN": return DS4Controls.GyroXNeg;
                case "sbnGyroZP": return DS4Controls.GyroZPos;
                case "sbnGyroZN": return DS4Controls.GyroZNeg;
#endregion

                case "bnShiftShare": return DS4Controls.Share;
                case "bnShiftL3": return DS4Controls.L3;
                case "bnShiftR3": return DS4Controls.R3;
                case "bnShiftOptions": return DS4Controls.Options;
                case "bnShiftUp": return DS4Controls.DpadUp;
                case "bnShiftRight": return DS4Controls.DpadRight;
                case "bnShiftDown": return DS4Controls.DpadDown;
                case "bnShiftLeft": return DS4Controls.DpadLeft;

                case "bnShiftL1": return DS4Controls.L1;
                case "bnShiftR1": return DS4Controls.R1;
                case "bnShiftTriangle": return DS4Controls.Triangle;
                case "bnShiftCircle": return DS4Controls.Circle;
                case "bnShiftCross": return DS4Controls.Cross;
                case "bnShiftSquare": return DS4Controls.Square;

                case "bnShiftPS": return DS4Controls.PS;
                case "bnShiftLSLeft": return DS4Controls.LXNeg;
                case "bnShiftLSUp": return DS4Controls.LYNeg;
                case "bnShiftRSLeft": return DS4Controls.RXNeg;
                case "bnShiftRSUp": return DS4Controls.RYNeg;

                case "bnShiftLSRight": return DS4Controls.LXPos;
                case "bnShiftLSDown": return DS4Controls.LYPos;
                case "bnShiftRSRight": return DS4Controls.RXPos;
                case "bnShiftRSDown": return DS4Controls.RYPos;
                case "bnShiftL2": return DS4Controls.L2;
                case "bnShiftR2": return DS4Controls.R2;

                case "bnShiftTouchLeft": return DS4Controls.TouchLeft;
                case "bnShiftTouchMulti": return DS4Controls.TouchMulti;
                case "bnShiftTouchUpper": return DS4Controls.TouchUpper;
                case "bnShiftTouchRight": return DS4Controls.TouchRight;
                case "bnShiftGyroXP": return DS4Controls.GyroXPos;
                case "bnShiftGyroXN": return DS4Controls.GyroXNeg;
                case "bnShiftGyroZP": return DS4Controls.GyroZPos;
                case "bnShiftGyroZN": return DS4Controls.GyroZNeg;

                case "bnShiftSwipeUp": return DS4Controls.SwipeUp;
                case "bnShiftSwipeDown": return DS4Controls.SwipeDown;
                case "bnShiftSwipeLeft": return DS4Controls.SwipeLeft;
                case "bnShiftSwipeRight": return DS4Controls.SwipeRight;
            }

            return 0;
        }

        public X360Controls getX360ControlsByName(string key)
        {
            X360Controls x3c;
            if (Enum.TryParse(key, true, out x3c))
                return x3c;

            switch (key)
            {
                case "Back": return X360Controls.Back;
                case "Left Stick": return X360Controls.LS;
                case "Right Stick": return X360Controls.RS;
                case "Start": return X360Controls.Start;
                case "Up Button": return X360Controls.DpadUp;
                case "Right Button": return X360Controls.DpadRight;
                case "Down Button": return X360Controls.DpadDown;
                case "Left Button": return X360Controls.DpadLeft;

                case "Left Bumper": return X360Controls.LB;
                case "Right Bumper": return X360Controls.RB;
                case "Y Button": return X360Controls.Y;
                case "B Button": return X360Controls.B;
                case "A Button": return X360Controls.A;
                case "X Button": return X360Controls.X;

                case "Guide": return X360Controls.Guide;
                case "Left X-Axis-": return X360Controls.LXNeg;
                case "Left Y-Axis-": return X360Controls.LYNeg;
                case "Right X-Axis-": return X360Controls.RXNeg;
                case "Right Y-Axis-": return X360Controls.RYNeg;

                case "Left X-Axis+": return X360Controls.LXPos;
                case "Left Y-Axis+": return X360Controls.LYPos;
                case "Right X-Axis+": return X360Controls.RXPos;
                case "Right Y-Axis+": return X360Controls.RYPos;
                case "Left Trigger": return X360Controls.LT;
                case "Right Trigger": return X360Controls.RT;

                case "Left Mouse Button": return X360Controls.LeftMouse;
                case "Right Mouse Button": return X360Controls.RightMouse;
                case "Middle Mouse Button": return X360Controls.MiddleMouse;
                case "4th Mouse Button": return X360Controls.FourthMouse;
                case "5th Mouse Button": return X360Controls.FifthMouse;
                case "Mouse Wheel Up": return X360Controls.WUP;
                case "Mouse Wheel Down": return X360Controls.WDOWN;
                case "Mouse Up": return X360Controls.MouseUp;
                case "Mouse Down": return X360Controls.MouseDown;
                case "Mouse Left": return X360Controls.MouseLeft;
                case "Mouse Right": return X360Controls.MouseRight;
                case "Unbound": return X360Controls.Unbound;
            }

            return X360Controls.Unbound;
        }

        public string getX360ControlString(X360Controls key)
        {
            switch (key)
            {
                case X360Controls.Back: return "Back";
                case X360Controls.LS: return "Left Stick";
                case X360Controls.RS: return "Right Stick";
                case X360Controls.Start: return "Start";
                case X360Controls.DpadUp: return "Up Button";
                case X360Controls.DpadRight: return "Right Button";
                case X360Controls.DpadDown: return "Down Button";
                case X360Controls.DpadLeft: return "Left Button";

                case X360Controls.LB: return "Left Bumper";
                case X360Controls.RB: return "Right Bumper";
                case X360Controls.Y: return "Y Button";
                case X360Controls.B: return "B Button";
                case X360Controls.A: return "A Button";
                case X360Controls.X: return "X Button";

                case X360Controls.Guide: return "Guide";
                case X360Controls.LXNeg: return "Left X-Axis-";
                case X360Controls.LYNeg: return "Left Y-Axis-";
                case X360Controls.RXNeg: return "Right X-Axis-";
                case X360Controls.RYNeg: return "Right Y-Axis-";

                case X360Controls.LXPos: return "Left X-Axis+";
                case X360Controls.LYPos: return "Left Y-Axis+";
                case X360Controls.RXPos: return "Right X-Axis+";
                case X360Controls.RYPos: return "Right Y-Axis+";
                case X360Controls.LT: return "Left Trigger";
                case X360Controls.RT: return "Right Trigger";

                case X360Controls.LeftMouse: return "Left Mouse Button";
                case X360Controls.RightMouse: return "Right Mouse Button";
                case X360Controls.MiddleMouse: return "Middle Mouse Button";
                case X360Controls.FourthMouse: return "4th Mouse Button";
                case X360Controls.FifthMouse: return "5th Mouse Button";
                case X360Controls.WUP: return "Mouse Wheel Up";
                case X360Controls.WDOWN: return "Mouse Wheel Down";
                case X360Controls.MouseUp: return "Mouse Up";
                case X360Controls.MouseDown: return "Mouse Down";
                case X360Controls.MouseLeft: return "Mouse Left";
                case X360Controls.MouseRight: return "Mouse Right";
                case X360Controls.Unbound: return "Unbound";
            }

            return "Unbound";
        }

        public bool LoadProfile(int device, bool launchprogram, ControlService control,
            string propath = "", bool xinputChange = true, bool postLoad = true)
        {
            var aux = Global.aux[device];
            var dev = this.dev[device];
            bool Loaded = true;
            Dictionary<DS4Controls, DS4KeyType> customMapKeyTypes = new Dictionary<DS4Controls, DS4KeyType>();
            Dictionary<DS4Controls, UInt16> customMapKeys = new Dictionary<DS4Controls, UInt16>();
            Dictionary<DS4Controls, X360Controls> customMapButtons = new Dictionary<DS4Controls, X360Controls>();
            Dictionary<DS4Controls, String> customMapMacros = new Dictionary<DS4Controls, String>();
            Dictionary<DS4Controls, String> customMapExtras = new Dictionary<DS4Controls, String>();
            Dictionary<DS4Controls, DS4KeyType> shiftCustomMapKeyTypes = new Dictionary<DS4Controls, DS4KeyType>();
            Dictionary<DS4Controls, UInt16> shiftCustomMapKeys = new Dictionary<DS4Controls, UInt16>();
            Dictionary<DS4Controls, X360Controls> shiftCustomMapButtons = new Dictionary<DS4Controls, X360Controls>();
            Dictionary<DS4Controls, String> shiftCustomMapMacros = new Dictionary<DS4Controls, String>();
            Dictionary<DS4Controls, String> shiftCustomMapExtras = new Dictionary<DS4Controls, String>();
            string rootname = "DS4Windows";
            bool missingSetting = false;
            string profilepath;
            if (propath == "")
                profilepath = Global.appdatapath + @"\Profiles\" + dev.profilePath + ".xml";
            else
                profilepath = propath;

            bool xinputPlug = false;
            bool xinputStatus = false;

            if (File.Exists(profilepath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(DS4ProfileXML));
                DS4Profile profile;
                using (FileStream fs = new FileStream(profilepath, FileMode.Open))
                    profile = (DS4Profile)serializer.Deserialize(fs);
                Console.WriteLine("START");
                serializer.Serialize(Console.Out, profile);
                Console.WriteLine("END");
            }

            if (File.Exists(profilepath))
            {
                XmlNode Item;

                m_Xdoc.Load(profilepath);
                if (m_Xdoc.SelectSingleNode(rootname) == null)
                {
                    rootname = "ScpControl";
                    missingSetting = true;
                }

                if (device < 4)
                {
                    DS4LightBar.forcelight[device] = false;
                    DS4LightBar.forcedFlash[device] = 0;
                }

                OutContType oldContType = dev.outputDevType;

                // Make sure to reset currently set profile values before parsing
                ResetProfile(device);

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/flushHIDQueue"); Boolean.TryParse(Item.InnerText, out dev.flushHIDQueue); }
                catch { missingSetting = true; }//rootname = }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/touchToggle"); Boolean.TryParse(Item.InnerText, out dev.enableTouchToggle); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/idleDisconnectTimeout"); Int32.TryParse(Item.InnerText, out dev.idleDisconnectTimeout); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/Color"); DS4Color.TryParse(Item.InnerText, ref dev.mainColor); }
                catch { missingSetting = true; }

                if (m_Xdoc.SelectSingleNode("/" + rootname + "/Color") == null)
                {
                    //Old method of color saving
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/Red"); Byte.TryParse(Item.InnerText, out dev.mainColor.red); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/Green"); Byte.TryParse(Item.InnerText, out dev.mainColor.green); }
                    catch { missingSetting = true; }

                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/Blue"); Byte.TryParse(Item.InnerText, out dev.mainColor.blue); }
                    catch { missingSetting = true; }
                }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RumbleBoost"); Byte.TryParse(Item.InnerText, out dev.rumbleBoost); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/ledAsBatteryIndicator"); Boolean.TryParse(Item.InnerText, out dev.ledAsBatteryIndicator); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/FlashType"); Byte.TryParse(Item.InnerText, out dev.flashType); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/flashBatteryAt"); Int32.TryParse(Item.InnerText, out dev.flashAt); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/touchSensitivity"); Byte.TryParse(Item.InnerText, out dev.touchSensitivity); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LowColor"); DS4Color.TryParse(Item.InnerText, ref dev.lowColor); }
                catch { missingSetting = true; }

                if (m_Xdoc.SelectSingleNode("/" + rootname + "/LowColor") == null)
                {
                    //Old method of color saving
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LowRed"); byte.TryParse(Item.InnerText, out dev.lowColor.red); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LowGreen"); byte.TryParse(Item.InnerText, out dev.lowColor.green); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LowBlue"); byte.TryParse(Item.InnerText, out dev.lowColor.blue); }
                    catch { missingSetting = true; }
                }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/ChargingColor"); DS4Color.TryParse(Item.InnerText, ref dev.chargingColor); }
                catch { missingSetting = true; }

                if (m_Xdoc.SelectSingleNode("/" + rootname + "/ChargingColor") == null)
                {
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/ChargingRed"); Byte.TryParse(Item.InnerText, out dev.chargingColor.red); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/ChargingGreen"); Byte.TryParse(Item.InnerText, out dev.chargingColor.green); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/ChargingBlue"); Byte.TryParse(Item.InnerText, out dev.chargingColor.blue); }
                    catch { missingSetting = true; }
                }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/FlashColor"); DS4Color.TryParse(Item.InnerText, ref dev.flashColor); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/touchpadJitterCompensation"); bool.TryParse(Item.InnerText, out dev.touchpadJitterCompensation); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/lowerRCOn"); bool.TryParse(Item.InnerText, out dev.lowerRCOn); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/tapSensitivity"); byte.TryParse(Item.InnerText, out dev.tapSensitivity); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/doubleTap"); bool.TryParse(Item.InnerText, out dev.doubleTap); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/scrollSensitivity"); int.TryParse(Item.InnerText, out dev.scrollSensitivity); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/TouchpadInvert"); int temp = 0; int.TryParse(Item.InnerText, out temp); dev.touchpadInvert = Math.Min(Math.Max(temp, 0), 3); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LeftTriggerMiddle"); byte.TryParse(Item.InnerText, out dev.l2ModInfo.deadZone); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RightTriggerMiddle"); byte.TryParse(Item.InnerText, out dev.r2ModInfo.deadZone); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/L2AntiDeadZone"); int.TryParse(Item.InnerText, out dev.l2ModInfo.antiDeadZone); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/R2AntiDeadZone"); int.TryParse(Item.InnerText, out dev.r2ModInfo.antiDeadZone); }
                catch { missingSetting = true; }

                try {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/L2MaxZone"); int temp = 100;
                    int.TryParse(Item.InnerText, out temp);
                    dev.l2ModInfo.maxZone = Util.Clamp(temp, 0, 100);
                }
                catch { missingSetting = true; }

                try {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/R2MaxZone"); int temp = 100;
                    int.TryParse(Item.InnerText, out temp);
                    dev.r2ModInfo.maxZone = Util.Clamp(temp, 0, 100);
                }
                catch { missingSetting = true; }

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSRotation"); int temp = 0;
                    int.TryParse(Item.InnerText, out temp);
                    temp = Math.Min(Math.Max(temp, -180), 180);
                    dev.LSRotation = temp * Math.PI / 180.0;
                }
                catch { missingSetting = true; }

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSRotation"); int temp = 0;
                    int.TryParse(Item.InnerText, out temp);
                    temp = Math.Min(Math.Max(temp, -180), 180);
                    dev.RSRotation = temp * Math.PI / 180.0;
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/buttonMouseSensitivity"); int.TryParse(Item.InnerText, out dev.buttonMouseSensitivity); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/Rainbow"); double.TryParse(Item.InnerText, out dev.rainbow); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSDeadZone"); int.TryParse(Item.InnerText, out dev.lsModInfo.deadZone); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSDeadZone"); int.TryParse(Item.InnerText, out dev.rsModInfo.deadZone); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSAntiDeadZone"); int.TryParse(Item.InnerText, out dev.lsModInfo.antiDeadZone); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSAntiDeadZone"); int.TryParse(Item.InnerText, out dev.rsModInfo.antiDeadZone); }
                catch { missingSetting = true; }

                try {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSMaxZone"); int temp = 100;
                    int.TryParse(Item.InnerText, out temp);
                    dev.lsModInfo.maxZone = Math.Min(Math.Max(temp, 0), 100);
                }
                catch { missingSetting = true; }

                try {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSMaxZone"); int temp = 100;
                    int.TryParse(Item.InnerText, out temp);
                    dev.rsModInfo.maxZone = Math.Min(Math.Max(temp, 0), 100);
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SXDeadZone"); double.TryParse(Item.InnerText, out dev.SXDeadzone); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SZDeadZone"); double.TryParse(Item.InnerText, out dev.SZDeadzone); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SXMaxZone");
                    int temp = 0;
                    int.TryParse(Item.InnerText, out temp);
                    dev.SXMaxzone = Math.Min(Math.Max(temp * 0.01, 0.0), 1.0);
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SZMaxZone");
                    int temp = 0;
                    int.TryParse(Item.InnerText, out temp);
                    dev.SZMaxzone = Math.Min(Math.Max(temp * 0.01, 0.0), 1.0);
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SXAntiDeadZone");
                    int temp = 0;
                    int.TryParse(Item.InnerText, out temp);
                    dev.SXAntiDeadzone = Math.Min(Math.Max(temp * 0.01, 0.0), 1.0);
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SZAntiDeadZone");
                    int temp = 0;
                    int.TryParse(Item.InnerText, out temp);
                    dev.SZAntiDeadzone = Math.Min(Math.Max(temp * 0.01, 0.0), 1.0);
                }
                catch { missingSetting = true; }

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/Sensitivity");
                    string[] s = Item.InnerText.Split('|');
                    if (s.Length == 1)
                        s = Item.InnerText.Split(',');
                    if (!double.TryParse(s[0], out dev.LSSens) || dev.LSSens < .5f)
                        dev.LSSens = 1;
                    if (!double.TryParse(s[1], out dev.RSSens) || dev.RSSens < .5f)
                        dev.RSSens = 1;
                    if (!double.TryParse(s[2], out dev.l2Sens) || dev.l2Sens < .1f)
                        dev.l2Sens = 1;
                    if (!double.TryParse(s[3], out dev.r2Sens) || dev.r2Sens < .1f)
                        dev.r2Sens = 1;
                    if (!double.TryParse(s[4], out dev.SXSens) || dev.SXSens < .5f)
                        dev.SXSens = 1;
                    if (!double.TryParse(s[5], out dev.SZSens) || dev.SZSens < .5f)
                        dev.SZSens = 1;
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/ChargingType"); int.TryParse(Item.InnerText, out dev.chargingType); }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/MouseAcceleration"); bool.TryParse(Item.InnerText, out dev.mouseAccel); }
                catch { missingSetting = true; }

                int shiftM = 0;
                if (m_Xdoc.SelectSingleNode("/" + rootname + "/ShiftModifier") != null)
                    int.TryParse(m_Xdoc.SelectSingleNode("/" + rootname + "/ShiftModifier").InnerText, out shiftM);

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LaunchProgram");
                    dev.launchProgram = Item.InnerText;
                }
                catch { missingSetting = true; }

                if (launchprogram == true && dev.launchProgram != string.Empty)
                {
                    string programPath = dev.launchProgram;
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

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/DinputOnly");
                    bool.TryParse(Item.InnerText, out dev.dinputOnly);
                }
                catch { missingSetting = true; }

                bool oldUseDInputOnly = aux.UseDInputOnly;

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/StartTouchpadOff");
                    bool.TryParse(Item.InnerText, out dev.startTouchpadOff);
                    if (dev.startTouchpadOff) control.StartTPOff(device);
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/UseTPforControls"); bool.TryParse(Item.InnerText, out dev.useTPforControls); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/UseSAforMouse"); bool.TryParse(Item.InnerText, out dev.useSAforMouse); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SATriggers"); dev.sATriggers = Item.InnerText; }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SATriggerCond"); dev.sATriggerCond= SaTriggerCondValue(Item.InnerText); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SASteeringWheelEmulationAxis"); SASteeringWheelEmulationAxisType.TryParse(Item.InnerText, out dev.sASteeringWheelEmulationAxis); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SASteeringWheelEmulationRange"); int.TryParse(Item.InnerText, out dev.sASteeringWheelEmulationRange); }
                catch { missingSetting = true; }

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/TouchDisInvTriggers");
                    string[] triggers = Item.InnerText.Split(',');
                    int temp = -1;
                    List<int> tempIntList = new List<int>();
                    for (int i = 0, arlen = triggers.Length; i < arlen; i++)
                    {
                        if (int.TryParse(triggers[i], out temp))
                            tempIntList.Add(temp);
                    }

                    dev.touchDisInvertTriggers = tempIntList.ToArray();
                }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroSensitivity"); int.TryParse(Item.InnerText, out dev.gyroSensitivity); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroSensVerticalScale"); int.TryParse(Item.InnerText, out dev.gyroSensVerticalScale); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroInvert"); int.TryParse(Item.InnerText, out dev.gyroInvert); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroTriggerTurns"); bool.TryParse(Item.InnerText, out dev.gyroTriggerTurns); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroSmoothing"); bool.TryParse(Item.InnerText, out dev.gyroSmoothing); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroSmoothingWeight"); int temp = 0; int.TryParse(Item.InnerText, out temp); dev.gyroSmoothingWeight = Math.Min(Math.Max(0.0, Convert.ToDouble(temp * 0.01)), 1.0); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroMouseHAxis"); int temp = 0; int.TryParse(Item.InnerText, out temp); dev.gyroMouseHorizontalAxis = Math.Min(Math.Max(0, temp), 1); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroMouseDeadZone"); int.TryParse(Item.InnerText, out int temp);
                    SetGyroMouseDZ(device, temp, control); }
                catch { SetGyroMouseDZ(device, MouseCursor.GYRO_MOUSE_DEADZONE, control);  missingSetting = true; }

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/GyroMouseToggle"); bool.TryParse(Item.InnerText, out bool temp);
                    SetGyroMouseToggle(device, temp, control);
                }
                catch { SetGyroMouseToggle(device, false, control); missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSCurve"); int.TryParse(Item.InnerText, out dev.lsCurve); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSCurve"); int.TryParse(Item.InnerText, out dev.rsCurve); }
                catch { missingSetting = true; }

                try {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/BTPollRate");
                    int temp = 0;
                    int.TryParse(Item.InnerText, out temp);
                    dev.btPollRate = (temp >= 0 && temp <= 16) ? temp : 0;
                }
                catch { missingSetting = true; }

                // Note! xxOutputCurveCustom property needs to be read before xxOutputCurveMode property in case the curveMode is value 6
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSOutputCurveCustom"); dev.lsOutBezierCurveObj.CustomDefinition = Item.InnerText; }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSOutputCurveMode"); dev.lsOutCurveMode = stickOutputCurveId(Item.InnerText); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSOutputCurveCustom"); dev.rsOutBezierCurveObj.CustomDefinition = Item.InnerText; }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSOutputCurveMode"); dev.rsOutCurveMode = stickOutputCurveId(Item.InnerText); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/LSSquareStick"); bool.TryParse(Item.InnerText, out dev.squStickInfo.lsMode); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SquareStickRoundness"); double.TryParse(Item.InnerText, out dev.squStickInfo.roundness); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/RSSquareStick"); bool.TryParse(Item.InnerText, out dev.squStickInfo.rsMode); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/L2OutputCurveCustom"); dev.l2OutBezierCurveObj.CustomDefinition = Item.InnerText; }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/L2OutputCurveMode"); dev.l2OutCurveMode = axisOutputCurveId(Item.InnerText); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/R2OutputCurveCustom"); dev.r2OutBezierCurveObj.CustomDefinition = Item.InnerText; }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/R2OutputCurveMode"); dev.r2OutCurveMode = axisOutputCurveId(Item.InnerText); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SXOutputCurveCustom"); dev.sxOutBezierCurveObj.CustomDefinition = Item.InnerText; }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SXOutputCurveMode"); dev.sxOutCurveMode = axisOutputCurveId(Item.InnerText); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SZOutputCurveCustom"); dev.szOutBezierCurveObj.CustomDefinition = Item.InnerText; }
                catch { missingSetting = true; }
                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/SZOutputCurveMode"); dev.szOutCurveMode = axisOutputCurveId(Item.InnerText); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/TrackballMode"); bool.TryParse(Item.InnerText, out dev.trackballMode); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/TrackballFriction"); double.TryParse(Item.InnerText, out dev.trackballFriction); }
                catch { missingSetting = true; }

                try { Item = m_Xdoc.SelectSingleNode("/" + rootname + "/OutputContDevice"); dev.outputDevType= OutContDeviceId(Item.InnerText); }
                catch { missingSetting = true; }

                // Only change xinput devices under certain conditions. Avoid
                // performing this upon program startup before loading devices.
                if (xinputChange)
                {
                    if (device < 4)
                    {
                        DS4Device tempDevice = control.DS4Controllers[device];
                        bool exists = tempBool = (tempDevice != null);
                        bool synced = tempBool = exists ? tempDevice.isSynced() : false;
                        bool isAlive = tempBool = exists ? tempDevice.IsAlive() : false;
                        if (dev.dinputOnly != oldUseDInputOnly)
                        {
                            if (dev.dinputOnly)
                            {
                                xinputPlug = false;
                                xinputStatus = true;
                            }
                            else if (synced && isAlive)
                            {
                                xinputPlug = true;
                                xinputStatus = true;
                            }
                        }
                        else if (oldContType != dev.outputDevType)
                        {
                            xinputPlug = true;
                            xinputStatus = true;
                        }
                    }
                }

                try
                {
                    Item = m_Xdoc.SelectSingleNode("/" + rootname + "/ProfileActions");
                    dev.profileActions.Clear();
                    if (!string.IsNullOrEmpty(Item.InnerText))
                    {
                        string[] actionNames = Item.InnerText.Split('/');
                        for (int actIndex = 0, actLen = actionNames.Length; actIndex < actLen; actIndex++)
                        {
                            string tempActionName = actionNames[actIndex];
                            if (!dev.profileActions.Contains(tempActionName))
                            {
                                dev.profileActions.Add(tempActionName);
                            }
                        }
                    }
                }
                catch { missingSetting = true; }

                foreach (DS4ControlSettings dcs in dev.DS4Settings)
                    dcs.Reset();

                dev.containsCustomAction = false;
                dev.containsCustomExtras = false;
                dev.profileActionDict.Clear();
                dev.profileActionIndexDict.Clear(); // TODO CHECK profileactions
                foreach (string actionname in dev.profileActions)
                {
                    dev.profileActionDict[actionname] = Global.GetAction(actionname);
                    dev.profileActionIndexDict[actionname] = Global.GetActionIndexOf(actionname);
                }

                DS4KeyType keyType;
                ushort wvk;

                {
                    XmlNode ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/Control/Button");
                    if (ParentItem != null)
                    {
                        foreach (XmlNode item in ParentItem.ChildNodes)
                        {
                            UpdateDS4CSetting(device, item.Name, false, getX360ControlsByName(item.InnerText), "", DS4KeyType.None, 0);
                            customMapButtons.Add(getDS4ControlsByName(item.Name), getX360ControlsByName(item.InnerText));
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/Control/Macro");
                    if (ParentItem != null)
                    {
                        foreach (XmlNode item in ParentItem.ChildNodes)
                        {
                            customMapMacros.Add(getDS4ControlsByName(item.Name), item.InnerText);
                            string[] skeys;
                            int[] keys;
                            if (!string.IsNullOrEmpty(item.InnerText))
                            {
                                skeys = item.InnerText.Split('/');
                                keys = new int[skeys.Length];
                            }
                            else
                            {
                                skeys = new string[0];
                                keys = new int[0];
                            }

                            for (int i = 0, keylen = keys.Length; i < keylen; i++)
                                keys[i] = int.Parse(skeys[i]);

                            UpdateDS4CSetting(device, item.Name, false, keys, "", DS4KeyType.None, 0);
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/Control/Key");
                    if (ParentItem != null)
                    {
                        foreach (XmlNode item in ParentItem.ChildNodes)
                        {
                            if (ushort.TryParse(item.InnerText, out wvk))
                            {
                                UpdateDS4CSetting(device, item.Name, false, wvk, "", DS4KeyType.None, 0);
                                customMapKeys.Add(getDS4ControlsByName(item.Name), wvk);
                            }
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/Control/Extras");
                    if (ParentItem != null)
                    {
                        foreach (XmlNode item in ParentItem.ChildNodes)
                        {
                            if (item.InnerText != string.Empty)
                            {
                                UpdateDS4CExtra(device, item.Name, false, item.InnerText);
                                customMapExtras.Add(getDS4ControlsByName(item.Name), item.InnerText);
                            }
                            else
                                ParentItem.RemoveChild(item);
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/Control/KeyType");
                    if (ParentItem != null)
                    {
                        foreach (XmlNode item in ParentItem.ChildNodes)
                        {
                            if (item != null)
                            {
                                keyType = DS4KeyType.None;
                                if (item.InnerText.Contains(DS4KeyType.ScanCode.ToString()))
                                    keyType |= DS4KeyType.ScanCode;
                                if (item.InnerText.Contains(DS4KeyType.Toggle.ToString()))
                                    keyType |= DS4KeyType.Toggle;
                                if (item.InnerText.Contains(DS4KeyType.Macro.ToString()))
                                    keyType |= DS4KeyType.Macro;
                                if (item.InnerText.Contains(DS4KeyType.HoldMacro.ToString()))
                                    keyType |= DS4KeyType.HoldMacro;
                                if (item.InnerText.Contains(DS4KeyType.Unbound.ToString()))
                                    keyType |= DS4KeyType.Unbound;
                                if (keyType != DS4KeyType.None)
                                {
                                    UpdateDS4CKeyType(device, item.Name, false, keyType);
                                    customMapKeyTypes.Add(getDS4ControlsByName(item.Name), keyType);
                                }
                            }
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/ShiftControl/Button");
                    if (ParentItem != null)
                    {
                        foreach (XmlElement item in ParentItem.ChildNodes)
                        {
                            int shiftT = shiftM;
                            if (item.HasAttribute("Trigger"))
                                int.TryParse(item.Attributes["Trigger"].Value, out shiftT);
                            UpdateDS4CSetting(device, item.Name, true, getX360ControlsByName(item.InnerText), "", DS4KeyType.None, shiftT);
                            shiftCustomMapButtons.Add(getDS4ControlsByName(item.Name), getX360ControlsByName(item.InnerText));
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/ShiftControl/Macro");
                    if (ParentItem != null)
                    {
                        foreach (XmlElement item in ParentItem.ChildNodes)
                        {
                            shiftCustomMapMacros.Add(getDS4ControlsByName(item.Name), item.InnerText);
                            string[] skeys;
                            int[] keys;
                            if (!string.IsNullOrEmpty(item.InnerText))
                            {
                                skeys = item.InnerText.Split('/');
                                keys = new int[skeys.Length];
                            }
                            else
                            {
                                skeys = new string[0];
                                keys = new int[0];
                            }

                            for (int i = 0, keylen = keys.Length; i < keylen; i++)
                                keys[i] = int.Parse(skeys[i]);

                            int shiftT = shiftM;
                            if (item.HasAttribute("Trigger"))
                                int.TryParse(item.Attributes["Trigger"].Value, out shiftT);
                            UpdateDS4CSetting(device, item.Name, true, keys, "", DS4KeyType.None, shiftT);
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/ShiftControl/Key");
                    if (ParentItem != null)
                    {
                        foreach (XmlElement item in ParentItem.ChildNodes)
                        {
                            if (ushort.TryParse(item.InnerText, out wvk))
                            {
                                int shiftT = shiftM;
                                if (item.HasAttribute("Trigger"))
                                    int.TryParse(item.Attributes["Trigger"].Value, out shiftT);
                                UpdateDS4CSetting(device, item.Name, true, wvk, "", DS4KeyType.None, shiftT);
                                shiftCustomMapKeys.Add(getDS4ControlsByName(item.Name), wvk);
                            }
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/ShiftControl/Extras");
                    if (ParentItem != null)
                    {
                        foreach (XmlElement item in ParentItem.ChildNodes)
                        {
                            if (item.InnerText != string.Empty)
                            {
                                UpdateDS4CExtra(device, item.Name, true, item.InnerText);
                                shiftCustomMapExtras.Add(getDS4ControlsByName(item.Name), item.InnerText);
                            }
                            else
                                ParentItem.RemoveChild(item);
                        }
                    }

                    ParentItem = m_Xdoc.SelectSingleNode("/" + rootname + "/ShiftControl/KeyType");
                    if (ParentItem != null)
                    {
                        foreach (XmlElement item in ParentItem.ChildNodes)
                        {
                            if (item != null)
                            {
                                keyType = DS4KeyType.None;
                                if (item.InnerText.Contains(DS4KeyType.ScanCode.ToString()))
                                    keyType |= DS4KeyType.ScanCode;
                                if (item.InnerText.Contains(DS4KeyType.Toggle.ToString()))
                                    keyType |= DS4KeyType.Toggle;
                                if (item.InnerText.Contains(DS4KeyType.Macro.ToString()))
                                    keyType |= DS4KeyType.Macro;
                                if (item.InnerText.Contains(DS4KeyType.HoldMacro.ToString()))
                                    keyType |= DS4KeyType.HoldMacro;
                                if (item.InnerText.Contains(DS4KeyType.Unbound.ToString()))
                                    keyType |= DS4KeyType.Unbound;
                                if (keyType != DS4KeyType.None)
                                {
                                    UpdateDS4CKeyType(device, item.Name, true, keyType);
                                    shiftCustomMapKeyTypes.Add(getDS4ControlsByName(item.Name), keyType);
                                }
                            }
                        }
                    }
                }
            }

            // Only add missing settings if the actual load was graceful
            if (missingSetting && Loaded)// && buttons != null)
                SaveProfile(device, profilepath);

            dev.containsCustomAction = HasCustomActions(device);
            dev.containsCustomExtras = HasCustomExtras(device);

            // If a device exists, make sure to transfer relevant profile device
            // options to device instance
            if (postLoad && device < 4)
            {
                DS4Device tempDev = control.DS4Controllers[device];
                if (tempDev != null && tempDev.isSynced())
                {
                    tempDev.queueEvent(() =>
                    {
                        tempDev.setIdleTimeout(dev.idleDisconnectTimeout);
                        tempDev.setBTPollRate(dev.btPollRate);
                        if (xinputStatus && xinputPlug)
                        {
                            OutputDevice tempOutDev = control.outputDevices[device];
                            if (tempOutDev != null)
                            {
                                string tempType = tempOutDev.GetDeviceType();
                                AppLogger.LogToGui("Unplug " + tempType + " Controller #" + (device + 1), false);
                                tempOutDev.Disconnect();
                                tempOutDev = null;
                                control.outputDevices[device] = null;
                            }

                            OutContType tempContType = dev.outputDevType;
                            if (tempContType == OutContType.X360)
                            {
                                Xbox360OutDevice tempXbox = new Xbox360OutDevice(control.vigemTestClient);
                                control.outputDevices[device] = tempXbox;
                                tempXbox.cont.FeedbackReceived += (eventsender, args) =>
                                {
                                    control.SetDevRumble(tempDev, args.LargeMotor, args.SmallMotor, device);
                                };

                                tempXbox.Connect();
                                AppLogger.LogToGui("X360 Controller #" + (device + 1) + " connected", false);
                            }
                            else if (tempContType == OutContType.DS4)
                            {
                                DS4OutDevice tempDS4 = new DS4OutDevice(control.vigemTestClient);
                                control.outputDevices[device] = tempDS4;
                                tempDS4.cont.FeedbackReceived += (eventsender, args) =>
                                {
                                    control.SetDevRumble(tempDev, args.LargeMotor, args.SmallMotor, device);
                                };

                                tempDS4.Connect();
                                AppLogger.LogToGui("DS4 Controller #" + (device + 1) + " connected", false);
                            }

                            aux.UseDInputOnly = false;
                            
                        }
                        else if (xinputStatus && !xinputPlug)
                        {
                            string tempType = control.outputDevices[device].GetDeviceType();
                            control.outputDevices[device].Disconnect();
                            control.outputDevices[device] = null;
                            aux.UseDInputOnly = true;
                            AppLogger.LogToGui(tempType + " Controller #" + (device + 1) + " unplugged", false);
                        }

                        tempDev.setRumble(0, 0);
                    });

                    Program.rootHub.touchPad[device]?.ResetTrackAccel(dev.trackballFriction);
                }
            }

            return Loaded;
        }

        public bool Load()
        {
            bool Loaded = true;
            bool missingSetting = false;

            try
            {
                if (File.Exists(m_Profile))
                {
                    XmlNode Item;

                    m_Xdoc.Load(m_Profile);

                    try { Item = m_Xdoc.SelectSingleNode("/Profile/useExclusiveMode"); Boolean.TryParse(Item.InnerText, out useExclusiveMode); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/startMinimized"); Boolean.TryParse(Item.InnerText, out startMinimized); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/minimizeToTaskbar"); Boolean.TryParse(Item.InnerText, out minToTaskbar); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/formWidth"); Int32.TryParse(Item.InnerText, out formWidth); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/formHeight"); Int32.TryParse(Item.InnerText, out formHeight); }
                    catch { missingSetting = true; }
                    try {
                        int temp = 0;
                        Item = m_Xdoc.SelectSingleNode("/Profile/formLocationX"); Int32.TryParse(Item.InnerText, out temp);
                        formLocationX = Math.Max(temp, 0);
                    }
                    catch { missingSetting = true; }

                    try {
                        int temp = 0;
                        Item = m_Xdoc.SelectSingleNode("/Profile/formLocationY"); Int32.TryParse(Item.InnerText, out temp);
                        formLocationY = Math.Max(temp, 0);
                    }
                    catch { missingSetting = true; }

                    for (int i = 0; i < 4; i++) {
                        try {
                            Item = m_Xdoc.SelectSingleNode($"/Profile/Controller{i + 1}");
                            dev[i].profilePath = Item.InnerText;
                            if (dev[i].profilePath.ToLower().Contains("distance")) {
                                dev[i].distanceProfiles = true;
                            }

                            dev[i].olderProfilePath = dev[i].profilePath;
                        }
                        catch {
                            dev[i].profilePath = dev[i].olderProfilePath = string.Empty; dev[i].distanceProfiles = false; missingSetting = true;
                        }
                    }
               
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/LastChecked"); DateTime.TryParse(Item.InnerText, out lastChecked); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/CheckWhen"); Int32.TryParse(Item.InnerText, out CheckWhen); }
                    catch { missingSetting = true; }

                    try
                    {
                        Item = m_Xdoc.SelectSingleNode("/Profile/Notifications");
                        if (!int.TryParse(Item.InnerText, out Notifications))
                            Notifications = (Boolean.Parse(Item.InnerText) ? 2 : 0);
                    }
                    catch { missingSetting = true; }

                    try { Item = m_Xdoc.SelectSingleNode("/Profile/DisconnectBTAtStop"); Boolean.TryParse(Item.InnerText, out disconnectBTAtStop); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/SwipeProfiles"); Boolean.TryParse(Item.InnerText, out swipeProfiles); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/UseDS4ForMapping"); Boolean.TryParse(Item.InnerText, out ds4Mapping); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/QuickCharge"); Boolean.TryParse(Item.InnerText, out quickCharge); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/CloseMinimizes"); Boolean.TryParse(Item.InnerText, out closeMini); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/UseLang"); useLang = Item.InnerText; }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/DownloadLang"); Boolean.TryParse(Item.InnerText, out downloadLang); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/FlashWhenLate"); Boolean.TryParse(Item.InnerText, out flashWhenLate); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/FlashWhenLateAt"); int.TryParse(Item.InnerText, out flashWhenLateAt); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/WhiteIcon"); Boolean.TryParse(Item.InnerText, out useWhiteIcon); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/UseUDPServer"); Boolean.TryParse(Item.InnerText, out useUDPServ); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/UDPServerPort"); int temp; int.TryParse(Item.InnerText, out temp); udpServPort = Math.Min(Math.Max(temp, 1024), 65535); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/UDPServerListenAddress"); udpServListenAddress = Item.InnerText; }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/UseCustomSteamFolder"); Boolean.TryParse(Item.InnerText, out useCustomSteamFolder); }
                    catch { missingSetting = true; }
                    try { Item = m_Xdoc.SelectSingleNode("/Profile/CustomSteamFolder"); customSteamFolder = Item.InnerText; }
                    catch { missingSetting = true; }

                    for (int i = 0; i < 4; i++)
                    {
                        var dev = this.dev[i];
                        try
                        {
                            Item = m_Xdoc.SelectSingleNode("/Profile/CustomLed" + (i + 1));
                            string[] ss = Item.InnerText.Split(':');
                            bool.TryParse(ss[0], out dev.useCustomColor);
                            DS4Color.TryParse(ss[1], ref dev.customColor);
                        }
                        catch { dev.useCustomColor = false; dev.customColor = new DS4Color(Color.Blue); missingSetting = true; }
                    }
                }
            }
            catch { }

            if (missingSetting)
                Save();

            return Loaded;
        }

        public bool Save()
        {
            bool Saved = true;

            XmlNode Node;

            m_Xdoc.RemoveAll();

            Node = m_Xdoc.CreateXmlDeclaration("1.0", "utf-8", String.Empty);
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateComment(String.Format(" Profile Configuration Data. {0} ", DateTime.Now));
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateWhitespace("\r\n");
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateNode(XmlNodeType.Element, "Profile", null);

            XmlNode xmlUseExclNode = m_Xdoc.CreateNode(XmlNodeType.Element, "useExclusiveMode", null); xmlUseExclNode.InnerText = useExclusiveMode.ToString(); Node.AppendChild(xmlUseExclNode);
            XmlNode xmlStartMinimized = m_Xdoc.CreateNode(XmlNodeType.Element, "startMinimized", null); xmlStartMinimized.InnerText = startMinimized.ToString(); Node.AppendChild(xmlStartMinimized);
            XmlNode xmlminToTaskbar = m_Xdoc.CreateNode(XmlNodeType.Element, "minimizeToTaskbar", null); xmlminToTaskbar.InnerText = minToTaskbar.ToString(); Node.AppendChild(xmlminToTaskbar);
            XmlNode xmlFormWidth = m_Xdoc.CreateNode(XmlNodeType.Element, "formWidth", null); xmlFormWidth.InnerText = formWidth.ToString(); Node.AppendChild(xmlFormWidth);
            XmlNode xmlFormHeight = m_Xdoc.CreateNode(XmlNodeType.Element, "formHeight", null); xmlFormHeight.InnerText = formHeight.ToString(); Node.AppendChild(xmlFormHeight);
            XmlNode xmlFormLocationX = m_Xdoc.CreateNode(XmlNodeType.Element, "formLocationX", null); xmlFormLocationX.InnerText = formLocationX.ToString(); Node.AppendChild(xmlFormLocationX);
            XmlNode xmlFormLocationY = m_Xdoc.CreateNode(XmlNodeType.Element, "formLocationY", null); xmlFormLocationY.InnerText = formLocationY.ToString(); Node.AppendChild(xmlFormLocationY);

            XmlNode xmlController1 = m_Xdoc.CreateNode(XmlNodeType.Element, "Controller1", null); xmlController1.InnerText = !Global.aux[0].LinkedProfileCheck ? dev[0].profilePath : dev[0].olderProfilePath; Node.AppendChild(xmlController1);
            XmlNode xmlController2 = m_Xdoc.CreateNode(XmlNodeType.Element, "Controller2", null); xmlController2.InnerText = !Global.aux[1].LinkedProfileCheck ? dev[1].profilePath : dev[1].olderProfilePath; Node.AppendChild(xmlController2);
            XmlNode xmlController3 = m_Xdoc.CreateNode(XmlNodeType.Element, "Controller3", null); xmlController3.InnerText = !Global.aux[2].LinkedProfileCheck ? dev[2].profilePath : dev[2].olderProfilePath; Node.AppendChild(xmlController3);
            XmlNode xmlController4 = m_Xdoc.CreateNode(XmlNodeType.Element, "Controller4", null); xmlController4.InnerText = !Global.aux[3].LinkedProfileCheck ? dev[3].profilePath : dev[3].olderProfilePath; Node.AppendChild(xmlController4);

            XmlNode xmlLastChecked = m_Xdoc.CreateNode(XmlNodeType.Element, "LastChecked", null); xmlLastChecked.InnerText = lastChecked.ToString(); Node.AppendChild(xmlLastChecked);
            XmlNode xmlCheckWhen = m_Xdoc.CreateNode(XmlNodeType.Element, "CheckWhen", null); xmlCheckWhen.InnerText = CheckWhen.ToString(); Node.AppendChild(xmlCheckWhen);
            XmlNode xmlNotifications = m_Xdoc.CreateNode(XmlNodeType.Element, "Notifications", null); xmlNotifications.InnerText = Notifications.ToString(); Node.AppendChild(xmlNotifications);
            XmlNode xmlDisconnectBT = m_Xdoc.CreateNode(XmlNodeType.Element, "DisconnectBTAtStop", null); xmlDisconnectBT.InnerText = disconnectBTAtStop.ToString(); Node.AppendChild(xmlDisconnectBT);
            XmlNode xmlSwipeProfiles = m_Xdoc.CreateNode(XmlNodeType.Element, "SwipeProfiles", null); xmlSwipeProfiles.InnerText = swipeProfiles.ToString(); Node.AppendChild(xmlSwipeProfiles);
            XmlNode xmlDS4Mapping = m_Xdoc.CreateNode(XmlNodeType.Element, "UseDS4ForMapping", null); xmlDS4Mapping.InnerText = ds4Mapping.ToString(); Node.AppendChild(xmlDS4Mapping);
            XmlNode xmlQuickCharge = m_Xdoc.CreateNode(XmlNodeType.Element, "QuickCharge", null); xmlQuickCharge.InnerText = quickCharge.ToString(); Node.AppendChild(xmlQuickCharge);
            XmlNode xmlCloseMini = m_Xdoc.CreateNode(XmlNodeType.Element, "CloseMinimizes", null); xmlCloseMini.InnerText = closeMini.ToString(); Node.AppendChild(xmlCloseMini);
            XmlNode xmlUseLang = m_Xdoc.CreateNode(XmlNodeType.Element, "UseLang", null); xmlUseLang.InnerText = useLang.ToString(); Node.AppendChild(xmlUseLang);
            XmlNode xmlDownloadLang = m_Xdoc.CreateNode(XmlNodeType.Element, "DownloadLang", null); xmlDownloadLang.InnerText = downloadLang.ToString(); Node.AppendChild(xmlDownloadLang);
            XmlNode xmlFlashWhenLate = m_Xdoc.CreateNode(XmlNodeType.Element, "FlashWhenLate", null); xmlFlashWhenLate.InnerText = flashWhenLate.ToString(); Node.AppendChild(xmlFlashWhenLate);
            XmlNode xmlFlashWhenLateAt = m_Xdoc.CreateNode(XmlNodeType.Element, "FlashWhenLateAt", null); xmlFlashWhenLateAt.InnerText = flashWhenLateAt.ToString(); Node.AppendChild(xmlFlashWhenLateAt);
            XmlNode xmlWhiteIcon = m_Xdoc.CreateNode(XmlNodeType.Element, "WhiteIcon", null); xmlWhiteIcon.InnerText = useWhiteIcon.ToString(); Node.AppendChild(xmlWhiteIcon);
            XmlNode xmlUseUDPServ = m_Xdoc.CreateNode(XmlNodeType.Element, "UseUDPServer", null); xmlUseUDPServ.InnerText = useUDPServ.ToString(); Node.AppendChild(xmlUseUDPServ);
            XmlNode xmlUDPServPort = m_Xdoc.CreateNode(XmlNodeType.Element, "UDPServerPort", null); xmlUDPServPort.InnerText = udpServPort.ToString(); Node.AppendChild(xmlUDPServPort);
            XmlNode xmlUDPServListenAddress = m_Xdoc.CreateNode(XmlNodeType.Element, "UDPServerListenAddress", null); xmlUDPServListenAddress.InnerText = udpServListenAddress; Node.AppendChild(xmlUDPServListenAddress);
            XmlNode xmlUseCustomSteamFolder = m_Xdoc.CreateNode(XmlNodeType.Element, "UseCustomSteamFolder", null); xmlUseCustomSteamFolder.InnerText = useCustomSteamFolder.ToString(); Node.AppendChild(xmlUseCustomSteamFolder);
            XmlNode xmlCustomSteamFolder = m_Xdoc.CreateNode(XmlNodeType.Element, "CustomSteamFolder", null); xmlCustomSteamFolder.InnerText = customSteamFolder; Node.AppendChild(xmlCustomSteamFolder);

            for (int i = 0; i < 4; i++)
            {
                var dev = this.dev[i];
                XmlNode xmlCustomLed = m_Xdoc.CreateNode(XmlNodeType.Element, "CustomLed" + (1 + i), null);
                xmlCustomLed.InnerText = dev.useCustomColor.ToString() + ":" + dev.customColor.toXMLText();
                Node.AppendChild(xmlCustomLed);
            }

            m_Xdoc.AppendChild(Node);

            try { m_Xdoc.Save(m_Profile); }
            catch (UnauthorizedAccessException) { Saved = false; }
            return Saved;
        }

        private void CreateAction()
        {
            XmlDocument m_Xdoc = new XmlDocument();
            XmlNode Node;

            Node = m_Xdoc.CreateXmlDeclaration("1.0", "utf-8", String.Empty);
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateComment(String.Format(" Special Actions Configuration Data. {0} ", DateTime.Now));
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateWhitespace("\r\n");
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateNode(XmlNodeType.Element, "Actions", "");
            m_Xdoc.AppendChild(Node);
            m_Xdoc.Save(m_Actions);
        }

        public bool SaveAction(string name, string controls, int mode, string details, bool edit, string extras = "")
        {
            bool saved = true;
            if (!File.Exists(m_Actions))
                CreateAction();
            m_Xdoc.Load(m_Actions);
            XmlNode Node;

            Node = m_Xdoc.CreateComment(String.Format(" Special Actions Configuration Data. {0} ", DateTime.Now));
            foreach (XmlNode node in m_Xdoc.SelectNodes("//comment()"))
                node.ParentNode.ReplaceChild(Node, node);

            Node = m_Xdoc.SelectSingleNode("Actions");
            XmlElement el = m_Xdoc.CreateElement("Action");
            el.SetAttribute("Name", name);
            el.AppendChild(m_Xdoc.CreateElement("Trigger")).InnerText = controls;
            switch (mode)
            {
                case 1:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "Macro";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details;
                    if (extras != string.Empty)
                        el.AppendChild(m_Xdoc.CreateElement("Extras")).InnerText = extras;
                    break;
                case 2:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "Program";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details.Split('?')[0];
                    el.AppendChild(m_Xdoc.CreateElement("Arguements")).InnerText = extras;
                    el.AppendChild(m_Xdoc.CreateElement("Delay")).InnerText = details.Split('?')[1];
                    break;
                case 3:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "Profile";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details;
                    el.AppendChild(m_Xdoc.CreateElement("UnloadTrigger")).InnerText = extras;
                    break;
                case 4:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "Key";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details;
                    if (!string.IsNullOrEmpty(extras))
                    {
                        string[] exts = extras.Split('\n');
                        el.AppendChild(m_Xdoc.CreateElement("UnloadTrigger")).InnerText = exts[1];
                        el.AppendChild(m_Xdoc.CreateElement("UnloadStyle")).InnerText = exts[0];
                    }
                    break;
                case 5:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "DisconnectBT";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details;
                    break;
                case 6:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "BatteryCheck";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details;
                    break;
                case 7:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "MultiAction";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details;
                    break;
                case 8:
                    el.AppendChild(m_Xdoc.CreateElement("Type")).InnerText = "SASteeringWheelEmulationCalibrate";
                    el.AppendChild(m_Xdoc.CreateElement("Details")).InnerText = details;
                    break;
            }

            if (edit)
            {
                XmlNode oldxmlprocess = m_Xdoc.SelectSingleNode("/Actions/Action[@Name=\"" + name + "\"]");
                Node.ReplaceChild(el, oldxmlprocess);
            }
            else { Node.AppendChild(el); }

            m_Xdoc.AppendChild(Node);
            try { m_Xdoc.Save(m_Actions); }
            catch { saved = false; }
            LoadActions();
            return saved;
        }

        public void RemoveAction(string name)
        {
            m_Xdoc.Load(m_Actions);
            XmlNode Node = m_Xdoc.SelectSingleNode("Actions");
            XmlNode Item = m_Xdoc.SelectSingleNode("/Actions/Action[@Name=\"" + name + "\"]");
            if (Item != null)
                Node.RemoveChild(Item);

            m_Xdoc.AppendChild(Node);
            m_Xdoc.Save(m_Actions);
            LoadActions();
        }

        public bool LoadActions()
        {
            bool saved = true;
            if (!File.Exists(Global.appdatapath + "\\Actions.xml"))
            {
                SaveAction("Disconnect Controller", "PS/Options", 5, "0", false);
                saved = false;
            }

            try
            {
                actions.Clear();
                XmlDocument doc = new XmlDocument();
                doc.Load(Global.appdatapath + "\\Actions.xml");
                XmlNodeList actionslist = doc.SelectNodes("Actions/Action");
                string name, controls, type, details, extras, extras2;
                Mapping.actionDone.Clear();
                foreach (XmlNode x in actionslist)
                {
                    name = x.Attributes["Name"].Value;
                    controls = x.ChildNodes[0].InnerText;
                    type = x.ChildNodes[1].InnerText;
                    details = x.ChildNodes[2].InnerText;
                    Mapping.actionDone.Add(new Mapping.ActionState());
                    if (type == "Profile")
                    {
                        extras = x.ChildNodes[3].InnerText;
                        actions.Add(new SpecialAction(name, controls, type, details, 0, extras));
                    }
                    else if (type == "Macro")
                    {
                        if (x.ChildNodes[3] != null) extras = x.ChildNodes[3].InnerText;
                        else extras = string.Empty;
                        actions.Add(new SpecialAction(name, controls, type, details, 0, extras));
                    }
                    else if (type == "Key")
                    {
                        if (x.ChildNodes[3] != null)
                        {
                            extras = x.ChildNodes[3].InnerText;
                            extras2 = x.ChildNodes[4].InnerText;
                        }
                        else
                        {
                            extras = string.Empty;
                            extras2 = string.Empty;
                        }
                        if (!string.IsNullOrEmpty(extras))
                            actions.Add(new SpecialAction(name, controls, type, details, 0, extras2 + '\n' + extras));
                        else
                            actions.Add(new SpecialAction(name, controls, type, details));
                    }
                    else if (type == "DisconnectBT")
                    {
                        double doub;
                        if (double.TryParse(details, out doub))
                            actions.Add(new SpecialAction(name, controls, type, "", doub));
                        else
                            actions.Add(new SpecialAction(name, controls, type, ""));
                    }
                    else if (type == "BatteryCheck")
                    {
                        double doub;
                        if (double.TryParse(details.Split('|')[0], out doub))
                            actions.Add(new SpecialAction(name, controls, type, details, doub));
                        else if (double.TryParse(details.Split(',')[0], out doub))
                            actions.Add(new SpecialAction(name, controls, type, details, doub));
                        else
                            actions.Add(new SpecialAction(name, controls, type, details));
                    }
                    else if (type == "Program")
                    {
                        double doub;
                        if (x.ChildNodes[3] != null)
                        {
                            extras = x.ChildNodes[3].InnerText;
                            if (double.TryParse(x.ChildNodes[4].InnerText, out doub))
                                actions.Add(new SpecialAction(name, controls, type, details, doub, extras));
                            else
                                actions.Add(new SpecialAction(name, controls, type, details, 0, extras));
                        }
                        else
                        {
                            actions.Add(new SpecialAction(name, controls, type, details));
                        }
                    }
                    else if (type == "XboxGameDVR" || type == "MultiAction")
                    {
                        actions.Add(new SpecialAction(name, controls, type, details));
                    }
                    else if (type == "SASteeringWheelEmulationCalibrate")
                    {
                        double doub;
                        if (double.TryParse(details, out doub))
                            actions.Add(new SpecialAction(name, controls, type, "", doub));
                        else
                            actions.Add(new SpecialAction(name, controls, type, ""));
                    }
                }
            }
            catch { saved = false; }
            return saved;
        }

        public bool createLinkedProfiles()
        {
            bool saved = true;
            XmlDocument m_Xdoc = new XmlDocument();
            XmlNode Node;

            Node = m_Xdoc.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateComment(string.Format(" Mac Address and Profile Linking Data. {0} ", DateTime.Now));
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateWhitespace("\r\n");
            m_Xdoc.AppendChild(Node);

            Node = m_Xdoc.CreateNode(XmlNodeType.Element, "LinkedControllers", "");
            m_Xdoc.AppendChild(Node);

            try { m_Xdoc.Save(m_linkedProfiles); }
            catch (UnauthorizedAccessException) { AppLogger.LogToGui("Unauthorized Access - Save failed to path: " + m_linkedProfiles, false); saved = false; }

            return saved;
        }

        public bool LoadLinkedProfiles()
        {
            bool loaded = true;
            if (File.Exists(m_linkedProfiles))
            {
                XmlDocument linkedXdoc = new XmlDocument();
                XmlNode Node;
                linkedXdoc.Load(m_linkedProfiles);
                linkedProfiles.Clear();

                try
                {
                    Node = linkedXdoc.SelectSingleNode("/LinkedControllers");
                    XmlNodeList links = Node.ChildNodes;
                    for (int i = 0, listLen = links.Count; i < listLen; i++)
                    {
                        XmlNode current = links[i];
                        string serial = current.Name.Replace("MAC", string.Empty);
                        string profile = current.InnerText;
                        linkedProfiles[serial] = profile;
                    }
                }
                catch { loaded = false; }
            }
            else
            {
                AppLogger.LogToGui("LinkedProfiles.xml can't be found.", false);
                loaded = false;
            }

            return loaded;
        }

        public bool SaveLinkedProfiles()
        {
            bool saved = true;
            if (File.Exists(m_linkedProfiles))
            {
                XmlDocument linkedXdoc = new XmlDocument();
                XmlNode Node;

                Node = linkedXdoc.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
                linkedXdoc.AppendChild(Node);

                Node = linkedXdoc.CreateComment(string.Format(" Mac Address and Profile Linking Data. {0} ", DateTime.Now));
                linkedXdoc.AppendChild(Node);

                Node = linkedXdoc.CreateWhitespace("\r\n");
                linkedXdoc.AppendChild(Node);

                Node = linkedXdoc.CreateNode(XmlNodeType.Element, "LinkedControllers", "");
                linkedXdoc.AppendChild(Node);

                Dictionary<string, string>.KeyCollection serials = linkedProfiles.Keys;
                //for (int i = 0, itemCount = linkedProfiles.Count; i < itemCount; i++)
                for (var serialEnum = serials.GetEnumerator(); serialEnum.MoveNext();)
                {
                    //string serial = serials.ElementAt(i);
                    string serial = serialEnum.Current;
                    string profile = linkedProfiles[serial];
                    XmlElement link = linkedXdoc.CreateElement("MAC" + serial);
                    link.InnerText = profile;
                    Node.AppendChild(link);
                }

                try { linkedXdoc.Save(m_linkedProfiles); }
                catch (UnauthorizedAccessException) { AppLogger.LogToGui("Unauthorized Access - Save failed to path: " + m_linkedProfiles, false); saved = false; }
            }
            else
            {
                saved = createLinkedProfiles();
                saved = saved && SaveLinkedProfiles();
            }

            return saved;
        }

        public bool createControllerConfigs()
        {
            bool saved = true;
            XmlDocument configXdoc = new XmlDocument();
            XmlNode Node;

            Node = configXdoc.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
            configXdoc.AppendChild(Node);

            Node = configXdoc.CreateComment(string.Format(" Controller config data. {0} ", DateTime.Now));
            configXdoc.AppendChild(Node);

            Node = configXdoc.CreateWhitespace("\r\n");
            configXdoc.AppendChild(Node);

            Node = configXdoc.CreateNode(XmlNodeType.Element, "Controllers", "");
            configXdoc.AppendChild(Node);

            try { configXdoc.Save(m_controllerConfigs); }
            catch (UnauthorizedAccessException) { AppLogger.LogToGui("Unauthorized Access - Save failed to path: " + m_controllerConfigs, false); saved = false; }

            return saved;
        }

        public bool LoadControllerConfigsForDevice(DS4Device device)
        {
            bool loaded = false;

            if (device == null) return false;
            if (!File.Exists(m_controllerConfigs)) createControllerConfigs();

            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(m_controllerConfigs);

                XmlNode node = xmlDoc.SelectSingleNode("/Controllers/Controller[@Mac=\"" + device.getMacAddress() + "\"]");
                if (node != null)
                {
                    Int32 intValue;
                    if (Int32.TryParse(node["wheelCenterPoint"].InnerText.Split(',')[0], out intValue)) device.wheelCenterPoint.X = intValue;
                    if (Int32.TryParse(node["wheelCenterPoint"].InnerText.Split(',')[1], out intValue)) device.wheelCenterPoint.Y = intValue;
                    if (Int32.TryParse(node["wheel90DegPointLeft"].InnerText.Split(',')[0], out intValue)) device.wheel90DegPointLeft.X = intValue;
                    if (Int32.TryParse(node["wheel90DegPointLeft"].InnerText.Split(',')[1], out intValue)) device.wheel90DegPointLeft.Y = intValue;
                    if (Int32.TryParse(node["wheel90DegPointRight"].InnerText.Split(',')[0], out intValue)) device.wheel90DegPointRight.X = intValue;
                    if (Int32.TryParse(node["wheel90DegPointRight"].InnerText.Split(',')[1], out intValue)) device.wheel90DegPointRight.Y = intValue;

                    loaded = true;
                }
            }
            catch
            {
                AppLogger.LogToGui("ControllerConfigs.xml can't be found.", false);
                loaded = false;
            }

            return loaded;
        }

        public bool SaveControllerConfigsForDevice(DS4Device device)
        {
            bool saved = true;

            if (device == null) return false;
            if (!File.Exists(m_controllerConfigs)) createControllerConfigs();

            try
            {
                //XmlNode node = null;
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(m_controllerConfigs);

                XmlNode node = xmlDoc.SelectSingleNode("/Controllers/Controller[@Mac=\"" + device.getMacAddress() + "\"]");
                if (node == null)
                {
                    XmlNode xmlControllersNode = xmlDoc.SelectSingleNode("/Controllers");
                    XmlElement el = xmlDoc.CreateElement("Controller");
                    el.SetAttribute("Mac", device.getMacAddress());

                    el.AppendChild(xmlDoc.CreateElement("wheelCenterPoint"));
                    el.AppendChild(xmlDoc.CreateElement("wheel90DegPointLeft"));
                    el.AppendChild(xmlDoc.CreateElement("wheel90DegPointRight"));

                    node = xmlControllersNode.AppendChild(el);
                }

                node["wheelCenterPoint"].InnerText = $"{device.wheelCenterPoint.X},{device.wheelCenterPoint.Y}";
                node["wheel90DegPointLeft"].InnerText = $"{device.wheel90DegPointLeft.X},{device.wheel90DegPointLeft.Y}";
                node["wheel90DegPointRight"].InnerText = $"{device.wheel90DegPointRight.X},{device.wheel90DegPointRight.Y}";

                xmlDoc.Save(m_controllerConfigs);
            }
            catch (UnauthorizedAccessException)
            {
                AppLogger.LogToGui("Unauthorized Access - Save failed to path: " + m_controllerConfigs, false);
                saved = false;
            }

            return saved;
        }

        public void UpdateDS4CSetting(int deviceNum, string buttonName, bool shift, object action, string exts, DS4KeyType kt, int trigger = 0)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                dcs.UpdateSettings(shift, action, exts, kt, trigger);
            }
        }


        private void UpdateDS4CKeyType(int deviceNum, string buttonName, bool shift, DS4KeyType keyType)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                if (shift)
                    dcs.shiftKeyType = keyType;
                else
                    dcs.keyType = keyType;
            }
        }

        public object GetDS4Action(int deviceNum, string buttonName, bool shift)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                if (shift)
                {
                    return dcs.shiftAction;
                }
                else
                {
                    return dcs.action;
                }
            }

            return null;
        }

        public object GetDS4Action(int deviceNum, DS4Controls dc, bool shift)
        {
            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                if (shift)
                {
                    return dcs.shiftAction;
                }
                else
                {
                    return dcs.action;
                }
            }

            return null;
        }

        public string GetDS4Extra(int deviceNum, string buttonName, bool shift)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                if (shift)
                    return dcs.shiftExtras;
                else
                    return dcs.extras;
            }

            return null;
        }

        public DS4KeyType GetDS4KeyType(int deviceNum, string buttonName, bool shift)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                if (shift)
                    return dcs.shiftKeyType;
                else
                    return dcs.keyType;
            }

            return DS4KeyType.None;
        }

        public int GetDS4STrigger(int deviceNum, string buttonName)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                return dcs.shiftTrigger;
            }

            return 0;
        }

        public int GetDS4STrigger(int deviceNum, DS4Controls dc)
        {
            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                return dcs.shiftTrigger;
            }

            return 0;
        }

        public DS4ControlSettings getDS4CSetting(int deviceNum, string buttonName)
        {
            DS4Controls dc;
            if (buttonName.StartsWith("bn"))
                dc = getDS4ControlsByName(buttonName);
            else
                dc = (DS4Controls)Enum.Parse(typeof(DS4Controls), buttonName, true);

            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                return dcs;
            }

            return null;
        }

        public DS4ControlSettings getDS4CSetting(int deviceNum, DS4Controls dc)
        {
            int temp = (int)dc;
            if (temp > 0)
            {
                int index = temp - 1;
                DS4ControlSettings dcs = dev[deviceNum].DS4Settings[index];
                return dcs;
            }

            return null;
        }




        private void ResetProfile(int device)
        {
            dev[device] = new DeviceBackingStore(device);
        }
    }

    public class SpecialAction
    {
        public enum ActionTypeId { None, Key, Program, Profile, Macro, DisconnectBT, BatteryCheck, MultiAction, XboxGameDVR, SASteeringWheelEmulationCalibrate }

        public string name;
        public List<DS4Controls> trigger = new List<DS4Controls>();
        public string type;
        public ActionTypeId typeID;
        public string controls;
        public List<int> macro = new List<int>();
        public string details;
        public List<DS4Controls> uTrigger = new List<DS4Controls>();
        public string ucontrols;
        public double delayTime = 0;
        public string extra;
        public bool pressRelease = false;
        public DS4KeyType keyType;
        public bool tappedOnce = false;
        public bool firstTouch = false;
        public bool secondtouchbegin = false;
        public DateTime pastTime;
        public DateTime firstTap;
        public DateTime TimeofEnd;
        public bool automaticUntrigger = false;
        public string prevProfileName;  // Name of the previous profile where automaticUntrigger would jump back to (could be regular or temporary profile. Empty name is the same as regular profile)

        public SpecialAction(string name, string controls, string type, string details, double delay = 0, string extras = "")
        {
            this.name = name;
            this.type = type;
            this.typeID = ActionTypeId.None;
            this.controls = controls;
            delayTime = delay;
            string[] ctrls = controls.Split('/');
            foreach (string s in ctrls)
                trigger.Add(getDS4ControlsByName(s));

            if (type == "Key")
            {
                typeID = ActionTypeId.Key;
                this.details = details.Split(' ')[0];
                if (!string.IsNullOrEmpty(extras))
                {
                    string[] exts = extras.Split('\n');
                    pressRelease = exts[0] == "Release";
                    this.ucontrols = exts[1];
                    string[] uctrls = exts[1].Split('/');
                    foreach (string s in uctrls)
                        uTrigger.Add(getDS4ControlsByName(s));
                }
                if (details.Contains("Scan Code"))
                    keyType |= DS4KeyType.ScanCode;
            }
            else if (type == "Program")
            {
                typeID = ActionTypeId.Program;
                this.details = details;
                if (extras != string.Empty)
                    extra = extras;
            }
            else if (type == "Profile")
            {
                typeID = ActionTypeId.Profile;
                this.details = details;
                if (extras != string.Empty)
                {
                    extra = extras;
                }
            }
            else if (type == "Macro")
            {
                typeID = ActionTypeId.Macro;
                string[] macs = details.Split('/');
                foreach (string s in macs)
                {
                    int v;
                    if (int.TryParse(s, out v))
                        macro.Add(v);
                }
                if (extras.Contains("Scan Code"))
                    keyType |= DS4KeyType.ScanCode;
            }
            else if (type == "DisconnectBT")
            {
                typeID = ActionTypeId.DisconnectBT;
            }
            else if (type == "BatteryCheck")
            {
                typeID = ActionTypeId.BatteryCheck;
                string[] dets = details.Split('|');
                this.details = string.Join(",", dets);
            }
            else if (type == "MultiAction")
            {
                typeID = ActionTypeId.MultiAction;
                this.details = details;
            }
            else if (type == "XboxGameDVR")
            {
                this.typeID = ActionTypeId.XboxGameDVR;
                string[] dets = details.Split(',');
                List<string> macros = new List<string>();
                //string dets = "";
                int typeT = 0;
                for (int i = 0; i < 3; i++)
                {
                    if (int.TryParse(dets[i], out typeT))
                    {
                        switch (typeT)
                        {
                            case 0: macros.Add("91/71/71/91"); break;
                            case 1: macros.Add("91/164/82/82/164/91"); break;
                            case 2: macros.Add("91/164/44/44/164/91"); break;
                            case 3: macros.Add(dets[3] + "/" + dets[3]); break;
                            case 4: macros.Add("91/164/71/71/164/91"); break;
                        }
                    }
                }
                this.type = "MultiAction";
                type = "MultiAction";
                this.details = string.Join(",", macros);
            }
            else if (type == "SASteeringWheelEmulationCalibrate")
            {
                typeID = ActionTypeId.SASteeringWheelEmulationCalibrate;
            }
            else
                this.details = details;

            if (type != "Key" && !string.IsNullOrEmpty(extras))
            {
                this.ucontrols = extras;
                string[] uctrls = extras.Split('/');
                foreach (string s in uctrls)
                {
                    if (s == "AutomaticUntrigger") this.automaticUntrigger = true;
                    else uTrigger.Add(getDS4ControlsByName(s));
                }
            }
        }

        private DS4Controls getDS4ControlsByName(string key)
        {
            switch (key)
            {
                case "Share": return DS4Controls.Share;
                case "L3": return DS4Controls.L3;
                case "R3": return DS4Controls.R3;
                case "Options": return DS4Controls.Options;
                case "Up": return DS4Controls.DpadUp;
                case "Right": return DS4Controls.DpadRight;
                case "Down": return DS4Controls.DpadDown;
                case "Left": return DS4Controls.DpadLeft;

                case "L1": return DS4Controls.L1;
                case "R1": return DS4Controls.R1;
                case "Triangle": return DS4Controls.Triangle;
                case "Circle": return DS4Controls.Circle;
                case "Cross": return DS4Controls.Cross;
                case "Square": return DS4Controls.Square;

                case "PS": return DS4Controls.PS;
                case "Left Stick Left": return DS4Controls.LXNeg;
                case "Left Stick Up": return DS4Controls.LYNeg;
                case "Right Stick Left": return DS4Controls.RXNeg;
                case "Right Stick Up": return DS4Controls.RYNeg;

                case "Left Stick Right": return DS4Controls.LXPos;
                case "Left Stick Down": return DS4Controls.LYPos;
                case "Right Stick Right": return DS4Controls.RXPos;
                case "Right Stick Down": return DS4Controls.RYPos;
                case "L2": return DS4Controls.L2;
                case "R2": return DS4Controls.R2;

                case "Left Touch": return DS4Controls.TouchLeft;
                case "Multitouch": return DS4Controls.TouchMulti;
                case "Upper Touch": return DS4Controls.TouchUpper;
                case "Right Touch": return DS4Controls.TouchRight;

                case "Swipe Up": return DS4Controls.SwipeUp;
                case "Swipe Down": return DS4Controls.SwipeDown;
                case "Swipe Left": return DS4Controls.SwipeLeft;
                case "Swipe Right": return DS4Controls.SwipeRight;

                case "Tilt Up": return DS4Controls.GyroZNeg;
                case "Tilt Down": return DS4Controls.GyroZPos;
                case "Tilt Left": return DS4Controls.GyroXPos;
                case "Tilt Right": return DS4Controls.GyroXNeg;
            }

            return 0;
        }
    }
}
