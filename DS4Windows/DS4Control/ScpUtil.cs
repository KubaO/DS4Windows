﻿using System;""
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
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Windows.Forms;
using Alba.Framework.Collections;
using System.Collections.ObjectModel;

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

    internal class AppState
    {
        private bool? isAdministrator;
        public bool IsAdministrator
        {
            get
            {
                if (isAdministrator is bool result) return result;
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return (bool)(isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator));
            }
        }

        private bool? exePathNeedsAdmin;
        public bool ExePathNeedsAdmin
        {
            get
            {
                if (exePathNeedsAdmin is bool result) return result;
                try
                {
                    File.WriteAllText($"{API.ExePath}\\test.txt", "test");
                    File.Delete($"{API.ExePath}\\test.txt");
                    return (bool)(exePathNeedsAdmin = false);
                }
                catch (UnauthorizedAccessException)
                {
                    return (bool)(exePathNeedsAdmin = true);
                }
            }
        }

        public static void SetCulture(string culture)
        {
            try
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(culture);
            }
            catch { /* Skip setting culture that we cannot set */ }
        }

        public static string ExePath = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;

        public string ProfilePath { get; private set; }
        public string ActionsPath { get; private set; }
        public string LinkedProfilesPath { get; private set; }
        public string ControllerConfigsPath { get; private set; }

        string appDataPath;
        public string AppDataPath {
            get => appDataPath;
            set
            {
                appDataPath = value;
                //ProfilePath = $"{appDataPath}\\Profiles.xml";
                ProfilePath = Directory.GetParent(ExePath).FullName + "\\Profiles.xml";
                ActionsPath = $"{appDataPath}\\Actions.xml";
                LinkedProfilesPath = $"{appDataPath}\\LinkedProfiles.xml";
                ControllerConfigsPath = $"{appDataPath}\\ControllerConfigs.xml";
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

        public bool IsFirstRun { get; private set; } = false;
        public bool MultiSaveSpots { get; private set; } = false;
        public bool RunHotPlug { get; set; } = false;

        internal AppState()
        {
            AppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Windows";
        }

        public static DS4Controls GetDS4ControlsByName(string key)
        {
            DS4Controls ds4c;
            if (Enum.TryParse(key, true, out ds4c))
                return ds4c;

            bool bnShift = key.StartsWith("bnShift");
            bool bn = !bnShift && key.StartsWith("bn");
            bool sbn = key.StartsWith("sbn");

            if (bn) key = key.Remove(0, 2);
            else if (sbn) key = key.Remove(0, 3);
            else /*bnShift*/ key = key.Remove(0, 7);

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
                case "LSLeft": return DS4Controls.LXNeg;
                case "LSUp": return DS4Controls.LYNeg;
                case "RSLeft": return DS4Controls.RXNeg;
                case "RSUp": return DS4Controls.RYNeg;

                case "LSRight": return DS4Controls.LXPos;
                case "LSDown": return DS4Controls.LYPos;
                case "RSRight": return DS4Controls.RXPos;
                case "RSDown": return DS4Controls.RYPos;
                case "L2": return DS4Controls.L2;
                case "R2": return DS4Controls.R2;

                case "TouchLeft": return DS4Controls.TouchLeft;
                case "TouchMulti": return DS4Controls.TouchMulti;
                case "TouchUpper": return DS4Controls.TouchUpper;
                case "TouchRight": return DS4Controls.TouchRight;
                case "GyroXP": return DS4Controls.GyroXPos;
                case "GyroXN": return DS4Controls.GyroXNeg;
                case "GyroZP": return DS4Controls.GyroZPos;
                case "GyroZN": return DS4Controls.GyroZNeg;

                case "SwipeUp": return DS4Controls.SwipeUp;
                case "SwipeDown": return DS4Controls.SwipeDown;
                case "SwipeLeft": return DS4Controls.SwipeLeft;
                case "SwipeRight": return DS4Controls.SwipeRight;

                // TODO: There was a typo: "sbnGsyroXP" - check in the repo
                // whether it was written to the file that way, or was it a
                // a read bug. For now, accept both spellings.
                case "GsyroXP": return DS4Controls.GyroXPos;
                default: return DS4Controls.None;
            }
        }

        public static X360Controls GetX360ControlsByName(string key)
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
                default: return X360Controls.Unbound;
            }
        }

        public static X360Controls[] DefaultButtonMapping { get; } = { X360Controls.None, X360Controls.LXNeg, X360Controls.LXPos,
            X360Controls.LYNeg, X360Controls.LYPos, X360Controls.RXNeg, X360Controls.RXPos, X360Controls.RYNeg, X360Controls.RYPos,
            X360Controls.LB, X360Controls.LT, X360Controls.LS, X360Controls.RB, X360Controls.RT, X360Controls.RS, X360Controls.X,
            X360Controls.Y, X360Controls.B, X360Controls.A, X360Controls.DpadUp, X360Controls.DpadRight, X360Controls.DpadDown,
            X360Controls.DpadLeft, X360Controls.Guide, X360Controls.None, X360Controls.None, X360Controls.None, X360Controls.None,
            X360Controls.Back, X360Controls.Start, X360Controls.None, X360Controls.None, X360Controls.None, X360Controls.None,
            X360Controls.None, X360Controls.None, X360Controls.None, X360Controls.None
        };

        private static DS4Controls[] revButtonMapping;
        public static DS4Controls[] RevButtonMapping
        {
            get
            {
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


    }

    public class Global
    {
        Int32 idleTimeout = 600000;


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

        public static void SaveProfile(int device, string propath)
        {
            m_Config.SaveProfile(device, propath);
        }

        public static bool Save()
        {
            return m_Config.Save();
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

    public class SquareStickConfig : ISquareStickConfig
    {
        public bool LSMode { get; set; } = false;
        public bool RSMode { get; set; } = false;
        public double Roundness { get; set; }= 5.0;
    }

    public class Trigger2Config : ITrigger2Config
    {
        public double Sensitivity { get; set; } = 1.0;
        public int DeadZone { get; set; } = 0;  // Trigger deadzone is expressed in axis units
        public int AntiDeadZone { get; set; } = 0;
        public int MaxZone { get; set; } = 100;
        public int OutCurvePreset
        {
            get => (int)OutBezierCurve.Preset;
            set => OutBezierCurve.Preset = (BezierPreset)value;
        }
        public BezierCurve OutBezierCurve { get; set; } = new BezierCurve(BezierCurve.L2R2Range);
    }

    public class StickConfig : IStickConfig
    {
        public double Sensitivity { get; set; } = 1.0;
        public int DeadZone { get; set; } = 0;
        public int AntiDeadZone { get; set; } = 0;
        public int MaxZone { get; set; } = 0;
        public double Rotation { get; set; } = 0.0;
        public int Curve { get; set; } = 0;
        public int OutCurvePreset {
            get => (int) OutBezierCurve.Preset;
            set => OutBezierCurve.Preset = (BezierPreset)value;
        }
        public BezierCurve OutBezierCurve { get; } = new BezierCurve(BezierCurve.LSRSRange);
    }

    public class GyroConfig : IGyroConfig
    {
        public double Sensitivity { get; set; } = 1.0;
        public double DeadZone { get; set; } = 0.25;
        public double MaxZone { get; set; } = 1.0;
        public double AntiDeadZone { get; set; } = 0.0;
        public int OutCurvePreset
        {
            get => (int)OutBezierCurve.Preset;
            set => OutBezierCurve.Preset = (BezierPreset)value;
        }
        public BezierCurve OutBezierCurve { get; } = new BezierCurve(BezierCurve.SARange);
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
        public OutContType PreviousOutputDevType { get; set; }
    }

    public class DeviceConfig : IDeviceConfig
    {
        private readonly int devIndex;

        public DeviceConfig(int devIndex)
        {
            this.devIndex = devIndex; // FIXME: This is a hack

            Color tempColor = Color.Blue;
            switch (devIndex)
            {
                case 0: tempColor = Color.Blue; break;
                case 1: tempColor = Color.Red; break;
                case 2: tempColor = Color.Green; break;
                case 3: tempColor = Color.Pink; break;
                case 4: tempColor = Color.White; break;
                default: tempColor = Color.Blue; break;
            }
            MainColor = new DS4Color(tempColor);

            foreach (DS4Controls dc in Enum.GetValues(typeof(DS4Controls)))
            {
                if (dc != DS4Controls.None)
                    DS4CSettings.Add(new DS4ControlSettings(dc));
            }

            profileActions.Add("Disconnect Controller");
        }

        public string ProfilePath { get; set; } = string.Empty;
        public string OlderProfilePath { get; set; } = string.Empty;
        public string LaunchProgram { get; set; } = string.Empty;

        public int BTPollRate { get; set; } = 4;
        public bool FlushHIDQueue { get; set; } = false;
        public int IdleDisconnectTimeout { get; set; } = 0;
        public byte RumbleBoost { get; set; } = 100;
        public bool LowerRCOn { get; set; } = false;
        public int ChargingType { get; set; } = 0;
        public bool DInputOnly { get; set; } = false;

        public byte FlashType { get; set; } = 0;
        public int FlashBatteryAt { get; set; } = 0;
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
        public int GyroMouseDeadZone { get; private set; } = MouseCursor.GYRO_MOUSE_DEADZONE;
        public bool GyroMouseToggle { get; private set; } = false;

        public void SetGyroMouseDeadZone(int value, ControlService control)
        {
            GyroMouseDeadZone = value;
            if (devIndex < 4 && control.touchPad[devIndex] != null)
                control.touchPad[devIndex].CursorGyroDead = value;
        }

        public void SetGyroMouseToggle(bool value, ControlService control)
        {
            GyroMouseToggle = value;
            if (devIndex < 4 && control.touchPad[devIndex] != null)
                control.touchPad[devIndex].ToggleGyroMouse = value;
        }

        public int ButtonMouseSensitivity { get; set; } = 25;
        public bool MouseAccel { get; set; } = false;

        public bool TrackballMode { get; set; } = false;
        public double TrackballFriction { get; set; } = 10.0;

        public bool UseSAforMouse { get; set; } = false;
        public string SATriggers { get; set; } = string.Empty;
        public SATriggerCondType SATriggerCond { get; set; } = SATriggerCondType.And;
        public SASteeringWheelEmulationAxisType SASteeringWheelEmulationAxis { get; set; } =
            SASteeringWheelEmulationAxisType.None;
        public int SASteeringWheelEmulationRange { get; set; } = 360;

        public ITrigger2Config L2 { get; set; } = new Trigger2Config();
        public ITrigger2Config R2 { get; set; } = new Trigger2Config();
        public IStickConfig LS { get; set; } = new StickConfig();
        public IStickConfig RS { get; set; } = new StickConfig();
        public IGyroConfig SX { get; set; } = new GyroConfig();
        public IGyroConfig SZ { get; set; } = new GyroConfig();
        public ISquareStickConfig SquStick { get; set; } = new SquareStickConfig();

        public OutContType OutputDevType { get; set; } = OutContType.X360;
        public bool DistanceProfiles { get; set; } = false;

        private List<string> profileActions;
        private Dictionary<string, SpecialAction> profileActionDict = new Dictionary<string, SpecialAction>();
        private Dictionary<string, int> profileActionIndexDict = new Dictionary<string, int>();

        public ReadOnlyCollection<string> ProfileActions {
            get => profileActions.AsReadOnly();
            set => SetProfileActions(value.ToList());
        }

        public void SetProfileActions(List<string> newActions) {
            profileActions = newActions;
            profileActionDict = null;
            profileActionIndexDict = null;
            foreach (var action in profileActions) {
                int index = API.Config.LookupActionIndexOf(action);
                profileActionIndexDict[action] = index;
                profileActionDict[action] = API.Config.ActionByIndex(index);
            }
        }

        public SpecialAction GetProfileAction(string name)
        {
            SpecialAction sA;
            return profileActionDict.TryGetValue(name, out sA) ? sA : null;
        }

        public int GetProfileActionIndexOf(string name)
        {
            int index;
            return profileActionIndexDict.TryGetValue(name, out index) ? index : -1;
        }

        public List<DS4ControlSettings> DS4CSettings { get; } = new List<DS4ControlSettings>();

        public void UpdateDS4CSetting(string buttonName, bool shift, object action, string exts, DS4KeyType kt, int trigger = 0)
        {
            DS4ControlSettings dcs = GetDS4CSetting(buttonName);
            dcs.UpdateSettings(shift, action, exts, kt, trigger);
            hasCustomActions = hasCustomExtras = null;
        }

        public void UpdateDS4CExtra(string buttonName, bool shift, string exts)
        {
            DS4ControlSettings dcs = GetDS4CSetting(buttonName);
            if (shift)
                dcs.shiftExtras = exts;
            else
                dcs.extras = exts;            
            hasCustomActions = hasCustomExtras = null;
        }

        private void UpdateDS4CKeyType(string buttonName, bool shift, DS4KeyType keyType)
        {
            DS4ControlSettings dcs = GetDS4CSetting(buttonName);
            if (shift)
                dcs.shiftKeyType = keyType;
            else
                dcs.keyType = keyType;
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
            foreach (var dcs in DS4CSettings) {
                actions = actions || dcs.action != null || dcs.shiftAction != null;
                extras = extras || dcs.extras != null || dcs.shiftExtras != null;
                if (actions && extras) break;
            }
            hasCustomActions = actions;
            hasCustomExtras = extras;
            return true;
        }

        public DS4ControlSettings GetDS4CSetting(string buttonName)
        {
            DS4Controls dc = AppState.GetDS4ControlsByName(buttonName);
            return GetDS4CSetting(dc);
        }

        public DS4ControlSettings GetDS4CSetting(DS4Controls dc)
        {
            if (dc != DS4Controls.None)
            {
                int index = (int)dc - 1;
                return DS4CSettings[index];
            }
            return null;
        }

        public object GetDS4Action(string buttonName, bool shift)
        {
            var dcs = GetDS4CSetting(buttonName);
            return shift ? dcs?.shiftAction : dcs?.action;
        }

        public object GetDS4Action(DS4Controls dc, bool shift)
        {
            var dcs = GetDS4CSetting(dc);
            return shift ? dcs?.shiftAction : dcs?.action;
        }

        public string GetDS4Extra(string buttonName, bool shift)
        {
            var dcs = GetDS4CSetting(buttonName);
            return shift ? dcs?.shiftExtras : dcs?.extras;
        }

        public DS4KeyType GetDS4KeyType(string buttonName, bool shift)
        {
            var dcs = GetDS4CSetting(buttonName);
            if (dcs != null) return shift ? dcs.shiftKeyType : dcs.keyType;
            return DS4KeyType.None;
        }

        public int GetDS4STrigger(string buttonName)
        {
            var dcs = GetDS4CSetting(buttonName);
            return dcs?.shiftTrigger ?? 0;
        }

        public int GetDS4STrigger(DS4Controls dc)
        {
            var dcs = GetDS4CSetting(dc);
            return dcs?.shiftTrigger ?? 0;
        }

        // TODO: These dictionaries are way too heavy and too spread out. We have
        // so few items that lookup can be done via linear search in an array.
        private static BiDictionary<string, SATriggerCondType> saTriggerCondDict =
            new Func<BiDictionary<string, SATriggerCondType>>(() => {
                var dict = new BiDictionary<string, SATriggerCondType>();
                dict["and"] = SATriggerCondType.And;
                dict["or"] = SATriggerCondType.Or;
                return dict;
            })();

        private static string saTriggerCond(SATriggerCondType value) =>
            saTriggerCondDict.RevValueOr(value, "and");

        private static SATriggerCondType saTriggerCond(string text) =>
            saTriggerCondDict.ValueOr(text, SATriggerCondType.And);

        private static BiDictionary<string, BezierPreset> outputCurveDict =
            new Func<BiDictionary<string, BezierPreset>>(() => {
                var dict = new BiDictionary<string, BezierPreset>();
                dict["linear"] = BezierPreset.Linear;
                dict["enhanced-precision"] = BezierPreset.EnhancedPrecision;
                dict["quadratic"] = BezierPreset.Quadric;
                dict["cubic"] = BezierPreset.Cubic;
                dict["easeout-quad"] = BezierPreset.EaseOutQuad;
                dict["easeout-cubic"] = BezierPreset.EaseOutCubic;
                dict["custom"] = BezierPreset.Custom;
                return dict;
            })();

        private string outputCurve(BezierPreset preset) =>
            outputCurveDict.RevValueOr(preset, "linear");

        private BezierPreset outputCurve(string name) =>
            outputCurveDict.ValueOr(name, BezierPreset.Linear);

        private string outContDevice(OutContType id)
        {
            switch (id)
            {
                case OutContType.None:
                case OutContType.X360: return "X360";;
                case OutContType.DS4: return "DS4";
                default: return "X360";
            }
        }

        public static OutContType outContDevice(string name)
        {
            switch (name)
            {
                case "None":
                case "X360": return OutContType.X360; break;
                case "DS4": return OutContType.DS4; break;
                default: return OutContType.X360;
            }
        }

        public class Loader
        {
            public XmlDocument Xdoc;
            public bool missingSetting;
            public string rootname;

            public void Open(XmlDocument doc)
            {
                Xdoc = doc;
                missingSetting = false;
                rootname = "DS4Windows";
                if (Xdoc.SelectSingleNode(rootname) == null)
                {
                    rootname = "ScpControl";
                    missingSetting = true;
                }
            }
            public string LoadText(string path)
            {
                XmlNode Item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                string result = Item?.InnerText;
                if (result == null) missingSetting = true;
                return result;
            }
            public bool? LoadBool(string path)
            {
                XmlNode Item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                bool result;
                if (Item == null || !bool.TryParse(Item.InnerText, out result))
                {
                    missingSetting = true;
                    return null;
                }
                return result;
            }
            public byte? LoadByte(string path)
            {
                XmlNode Item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                byte result;
                if (Item == null || !byte.TryParse(Item.InnerText, out result))
                {
                    missingSetting = true;
                    return null;
                }
                return result;
            }
            public int? LoadInt(string path)
            {
                XmlNode Item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                int result;
                if (Item == null || !int.TryParse(Item.InnerText, out result))
                {
                    missingSetting = true;
                    return null;
                }
                return result;
            }
            public double? LoadDouble(string path)
            {
                XmlNode Item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                double result;
                if (Item == null || !double.TryParse(Item.InnerText, out result))
                {
                    missingSetting = true;
                    return null;
                }
                return result;
            }
            public DS4Color? LoadDS4Color(string path)
            {
                XmlNode Item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                DS4Color result = new DS4Color();
                if (Item == null || !DS4Color.TryParse(Item.InnerText, ref result))
                {
                    missingSetting = true;
                    return null;
                }
                return result;
            }
            public DS4Color LoadOldDS4Color(string prefix, DS4Color cur, DS4Color def)
            {
                //Old method of color saving
                cur.red = LoadByte($"{prefix}Red") ?? def.red;
                cur.green = LoadByte($"{prefix}Green") ?? def.green;
                cur.blue = LoadByte($"{prefix}Blue") ?? def.blue;
                return cur;
            }
            public SASteeringWheelEmulationAxisType? LoadSASWEmulationAxis(string path)
            {
                XmlNode Item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                SASteeringWheelEmulationAxisType result;
                if (Item == null || !SASteeringWheelEmulationAxisType.TryParse(Item.InnerText, out result))
                {
                    missingSetting = true;
                    return null;
                }
                return result;
            }
            public string[] LoadStrings(string path, char sep)
            {
                XmlNode item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                return ParseStrings(item?.InnerText ?? "", sep);
            }
            public int[] LoadInts(string path, char sep)
            {
                XmlNode item = Xdoc.SelectSingleNode($"/{rootname}/{path}");
                return ParseInts(item?.InnerText ?? "", sep);
            }

            public bool HasNode(string path) => Xdoc.SelectSingleNode($"/{rootname}/{path}") != null;

            public IEnumerable<XmlNode> ChildNodes(string path)
            {
                return (IEnumerable<XmlNode>)Xdoc.SelectSingleNode("$/{rootname}/{path}")?.ChildNodes ?? new XmlNode[0];
            }

            public double? ParseDouble(string text)
            {
                double result;
                if (text == null || !double.TryParse(text, out result)) {
                    missingSetting = true;
                    return null;
                }
                return result;
            }
            public double ParseSensitivity(string[] text, int index, double min)
            {
                double result;
                if (text.Length <= index || !double.TryParse(text[index], out result) || result < min) {
                    missingSetting = true;
                    return 1.0;
                }
                return result;
            }
            public string[] ParseStrings(string input, char sep)
            {
                if (input?.Split(sep) is string[] result)
                    return result;
                missingSetting = true;
                return new string[0];
            }
            public int[] ParseInts(string input, char sep)
            {
                string[] strs = ParseStrings(input, sep);
                int[] result = new int[strs.Length];
                int i = 0;
                foreach (string str in strs) {
                    if (!int.TryParse(str, out result[i])) missingSetting = true;
                    else i++;
                }
                return result.Take(i).ToArray();
            }
        }

        public bool LoadProfile(bool launchProgram, ControlService control,
            bool xinputChange = true, bool postLoad = true)
        {
            var aux = API.Aux(devIndex);
            bool loaded = LoadProfile(launchProgram, control, null, xinputChange, postLoad);
            aux.TempProfileName = string.Empty;
            aux.UseTempProfile = false;
            aux.TempProfileDistance = false;
            return loaded;
        }

        public bool LoadTempProfile(string name, bool launchProgram,
            ControlService control, bool xinputChange = true)
        {
            var aux = API.Aux(devIndex);
            var profilePath = $"{API.AppDataPath}\\Profiles\\{name}.xml";
            bool loaded = LoadProfile(launchProgram, control, profilePath, xinputChange, true);
            aux.TempProfileName = name;
            aux.UseTempProfile = true;
            aux.TempProfileDistance = name.ToLower().Contains("distance");
            return loaded;
        }

        private bool LoadProfile(bool launchProgram, ControlService control,
                    string profilePath, bool xinputChange, bool postLoad)
        {
            if (string.IsNullOrEmpty(profilePath))
                profilePath = $"{API.AppDataPath}\\Profiles\\{this.ProfilePath}.xml";

            bool xinputPlug = false;
            bool xinputStatus = false;

            bool missingSetting = false;
            bool loaded = false;

            if (File.Exists(profilePath)) {
                var Xdoc = new XmlDocument();
                Xdoc.Load(profilePath);
                missingSetting = !Load(Xdoc);
                loaded = true;
            }

            // Only add missing settings if the actual load was graceful
            if (missingSetting && loaded)// && buttons != null)
                SaveProfile(profilePath);

            hasCustomActions = hasCustomExtras = false; // FIXME - unneceessary; DS4 settings setter takes care of it

            PostLoad(launchProgram, control, xinputChange, postLoad);

            return loaded;
        }

        public bool Load(XmlDocument doc)
        {
            var aux = API.Aux(devIndex);
            Loader ldr = new Loader();
            var def = new DeviceConfig(devIndex);

            ldr.Open(doc);
            if (devIndex < 4) // TODO: this doesn't belong here!
            {
                DS4LightBar.forcelight[devIndex] = false;
                DS4LightBar.forcedFlash[devIndex] = 0;
            }

            aux.PreviousOutputDevType = OutputDevType;

            FlushHIDQueue = ldr.LoadBool("flushHIDQueue") ?? def.FlushHIDQueue;
            EnableTouchToggle = ldr.LoadBool("touchToggle") ?? def.EnableTouchToggle;
            IdleDisconnectTimeout = ldr.LoadInt("idleDisconnectTimeout") ?? def.IdleDisconnectTimeout;
            MainColor = ldr.LoadDS4Color("Color") ?? def.MainColor;
            if (!ldr.HasNode("Color"))
                MainColor = ldr.LoadOldDS4Color("", MainColor, def.MainColor);

            RumbleBoost = ldr.LoadByte("RumbleBoost") ?? def.RumbleBoost;
            LedAsBatteryIndicator = ldr.LoadBool("ledAsBatteryIndicator") ?? def.LedAsBatteryIndicator;
            FlashType = ldr.LoadByte("FlashType") ?? def.FlashType;

            FlashBatteryAt = ldr.LoadInt("flashBatteryAt") ?? def.FlashBatteryAt;
            TouchSensitivity = ldr.LoadByte("touchSensitivity") ?? def.TouchSensitivity;
            LowColor = ldr.LoadDS4Color("LowColor") ?? def.LowColor;
            if (!ldr.HasNode("LowColor"))
                LowColor = ldr.LoadOldDS4Color("Low", LowColor, def.LowColor);
            ChargingColor = ldr.LoadDS4Color("ChargingColor") ?? def.ChargingColor;
            if (!ldr.HasNode("ChargingColor"))
                ChargingColor = ldr.LoadOldDS4Color("Charging", ChargingColor, def.ChargingColor);
            FlashColor = ldr.LoadDS4Color("FlashColor") ?? def.FlashColor;
            TouchpadJitterCompensation = ldr.LoadBool("touchpadJitterCompensation") ?? def.TouchpadJitterCompensation;
            LowerRCOn = ldr.LoadBool("lowerRCOn") ?? def.LowerRCOn;
            TapSensitivity = ldr.LoadByte("tapSensitivity") ?? def.TapSensitivity;
            DoubleTap = ldr.LoadBool("doubleTap") ?? def.DoubleTap;
            ScrollSensitivity = ldr.LoadInt("scrollSensitivity") ?? def.ScrollSensitivity;
            TouchpadInvert = Util.Clamp(ldr.LoadInt("TouchpadInvert") ?? def.TouchpadInvert, 0,3);

            L2.DeadZone = ldr.LoadInt("LeftTriggerMiddle") ?? def.L2.DeadZone;
            R2.DeadZone = ldr.LoadInt("RightTriggerMiddle") ?? def.R2.DeadZone;

            L2.AntiDeadZone = ldr.LoadInt("L2AntiDeadZone") ?? def.L2.AntiDeadZone;
            R2.AntiDeadZone = ldr.LoadInt("R2AntiDeadZone") ?? def.R2.AntiDeadZone;

            L2.MaxZone = Util.Clamp(ldr.LoadInt("L2MaxZone") ?? def.L2.MaxZone, 0, 100);
            R2.MaxZone = Util.Clamp(ldr.LoadInt("R2MaxZone") ?? def.R2.MaxZone, 0, 100);

            LS.Rotation = Util.Clamp(ldr.LoadInt("LSRotation") ?? def.LS.Rotation * 180.0 / Math.PI, -180, 180) * Math.PI / 180.0;
            RS.Rotation = Util.Clamp(ldr.LoadInt("RSRotation") ?? def.RS.Rotation * 180.0 / Math.PI, -180, 180) * Math.PI / 180.0;

            ButtonMouseSensitivity = ldr.LoadInt("buttonMouseSensitivity") ?? def.ButtonMouseSensitivity;
            Rainbow = ldr.LoadDouble("Rainbow") ?? def.Rainbow;

            LS.DeadZone = ldr.LoadInt("LSDeadZone") ?? def.LS.DeadZone;
            RS.DeadZone = ldr.LoadInt("RSDeadZone") ?? def.RS.DeadZone;

            LS.AntiDeadZone = ldr.LoadInt("LSAntiDeadZone") ?? def.LS.AntiDeadZone;
            RS.AntiDeadZone = ldr.LoadInt("RSAntiDeadZone") ?? def.RS.AntiDeadZone;

            LS.MaxZone = Util.Clamp(ldr.LoadInt("LSMaxZone") ?? def.LS.MaxZone, 0, 100);
            RS.MaxZone = Util.Clamp(ldr.LoadInt("RSMaxZone") ?? def.RS.MaxZone, 0, 100);

            SX.DeadZone = ldr.LoadDouble("SXDeadZone") ?? def.SX.DeadZone;
            SZ.DeadZone = ldr.LoadDouble("SZDeadZone") ?? def.SZ.DeadZone;

            SX.MaxZone = Util.Clamp(ldr.LoadInt("SXMaxZone") ?? def.SX.MaxZone * 100, 0, 100) * 0.01;
            SZ.MaxZone = Util.Clamp(ldr.LoadInt("SZMaxZone") ?? def.SZ.MaxZone * 100, 0, 100) * 0.01;

            SX.AntiDeadZone = Util.Clamp(ldr.LoadInt("SXAntiDeadZone") ?? def.SX.AntiDeadZone * 100, 0, 100) * 0.01;
            SZ.AntiDeadZone = Util.Clamp(ldr.LoadInt("SZAntiDeadZone") ?? def.SZ.AntiDeadZone * 100, 0, 100) * 0.01;

            if (ldr.LoadText("Sensitivity") is string text) {
                string[] sens = text.Split('|');
                if (sens.Length == 1) text.Split(',');
                LS.Sensitivity = ldr.ParseSensitivity(sens, 0, 0.5f);
                RS.Sensitivity = ldr.ParseSensitivity(sens, 1, 0.5f);
                L2.Sensitivity = ldr.ParseSensitivity(sens, 2, 0.1f);
                R2.Sensitivity = ldr.ParseSensitivity(sens, 3, 0.1f);
                SX.Sensitivity = ldr.ParseSensitivity(sens, 4, 0.5f);
                SZ.Sensitivity = ldr.ParseSensitivity(sens, 5, 0.5f);
            }

            ChargingType = ldr.LoadInt("ChargingType") ?? def.ChargingType;
            MouseAccel = ldr.LoadBool("MouseAcceleration") ?? def.MouseAccel;

            int shiftM = ldr.LoadInt("ShiftModifier") ?? 0;

            LaunchProgram = ldr.LoadText("LaunchProgram") ?? def.LaunchProgram;

            DInputOnly = ldr.LoadBool("DinputOnly") ?? def.DInputOnly;

            StartTouchpadOff = ldr.LoadBool("StartTouchpadOff") ?? def.StartTouchpadOff;

            UseTPforControls = ldr.LoadBool("UseTPforControls") ?? def.UseTPforControls;
            UseSAforMouse = ldr.LoadBool("UseSAforMouse") ?? def.UseSAforMouse;
            SATriggers = ldr.LoadText("SATriggers") ?? def.SATriggers;
            SATriggerCond = (ldr.LoadText("SATriggerCond") is string t1) ? saTriggerCond(t1) : def.SATriggerCond;
            SASteeringWheelEmulationAxis = ldr.LoadSASWEmulationAxis("SASteeringWheelEmulationAxis") ?? def.SASteeringWheelEmulationAxis;
            SASteeringWheelEmulationRange = ldr.LoadInt("SASteeringWheelEmulationRange") ?? def.SASteeringWheelEmulationRange;

            TouchDisInvertTriggers = ldr.LoadInts("TouchDisInvTriggers", ',') ?? def.TouchDisInvertTriggers;

            GyroSensitivity = ldr.LoadInt("GyroSensitivity") ?? def.GyroSensitivity;
            GyroSensVerticalScale = ldr.LoadInt("GyroSensVerticalScale") ?? def.GyroSensVerticalScale;
            GyroInvert = ldr.LoadInt("GyroInvert") ?? def.GyroInvert;
            GyroTriggerTurns = ldr.LoadBool("GyroTriggerTurns") ?? def.GyroTriggerTurns;
            GyroSmoothing = ldr.LoadBool("GyroSmoothing") ?? def.GyroSmoothing;
            GyroSmoothingWeight = Util.Clamp(ldr.LoadInt("GyroSmoothingWeight") ?? GyroSmoothingWeight * 100, 0, 100) * 0.01;
            GyroMouseHorizontalAxis = Util.Clamp(ldr.LoadInt("GyroMouseHAxis") ?? def.GyroMouseHorizontalAxis, 0, 1);
            GyroMouseDeadZone = ldr.LoadInt("GyroMouseDeadZone") ?? def.GyroMouseDeadZone;
            GyroMouseToggle = ldr.LoadBool("GyroMouseToggle") ?? def.GyroMouseToggle;

            LS.Curve = ldr.LoadInt("LSCurve") ?? def.LS.Curve;
            RS.Curve = ldr.LoadInt("RSCurve") ?? def.RS.Curve;

            BTPollRate = Util.Clamp(ldr.LoadInt("BTPollRate") ?? def.BTPollRate, 0, 16);

            // Note! xxOutputCurveCustom property needs to be read before xxOutputCurveMode property in case the curveMode is value 6

            LS.OutBezierCurve.CustomDefinition = ldr.LoadText("LSOutputCurveCustom") ?? def.LS.OutBezierCurve.CustomDefinition;
            LS.OutBezierCurve.Preset = (ldr.LoadText("LSOutputCurveMode") is string t3) ? outputCurve(t3) : def.LS.OutBezierCurve.Preset;
            RS.OutBezierCurve.CustomDefinition = ldr.LoadText("RSOutputCurveCustom") ?? def.RS.OutBezierCurve.CustomDefinition;
            RS.OutBezierCurve.Preset = (ldr.LoadText("RSOutputCurveMode") is string t4) ? outputCurve(t4) : def.RS.OutBezierCurve.Preset;

            SquStick.LSMode = ldr.LoadBool("LSSquareStick") ?? def.SquStick.LSMode;
            SquStick.Roundness = ldr.LoadDouble("SquareStickRoundness") ?? def.SquStick.Roundness;
            SquStick.RSMode = ldr.LoadBool("RSSquareStick") ?? def.SquStick.RSMode;

            L2.OutBezierCurve.CustomDefinition = ldr.LoadText("L2OutputCurveCustom") ?? def.L2.OutBezierCurve.CustomDefinition;
            L2.OutBezierCurve.Preset = (ldr.LoadText("L2OutputCurveMode") is string t5) ? outputCurve(t5) : def.L2.OutBezierCurve.Preset;
            R2.OutBezierCurve.CustomDefinition = ldr.LoadText("R2OutputCurveCustom") ?? def.R2.OutBezierCurve.CustomDefinition;
            R2.OutBezierCurve.Preset = (ldr.LoadText("R2OutputCurveMode") is string t6) ? outputCurve(t6) : def.R2.OutBezierCurve.Preset;

            SX.OutBezierCurve.CustomDefinition = ldr.LoadText("SXOutputCurveCustom") ?? def.SX.OutBezierCurve.CustomDefinition;
            SX.OutBezierCurve.Preset = (ldr.LoadText("SXOutputCurveMode") is string t7) ? outputCurve(t7) : def.SX.OutBezierCurve.Preset;
            SZ.OutBezierCurve.CustomDefinition = ldr.LoadText("SZOutputCurveCustom") ?? def.SZ.OutBezierCurve.CustomDefinition;
            SZ.OutBezierCurve.Preset = (ldr.LoadText("SZOutputCurveMode") is string t8) ? outputCurve(t8) : def.SZ.OutBezierCurve.Preset;

            TrackballMode = ldr.LoadBool("TrackballMode") ?? def.TrackballMode;
            TrackballFriction = ldr.LoadDouble("TrackballFriction") ?? def.TrackballFriction;
            OutputDevType = (ldr.LoadText("OutputContDevice") is string t9) ? outContDevice(t9) : def.OutputDevType;

            if (ldr.LoadStrings("ProfileActions", '/') is string[] actions) {
                var newActions = new List<string>();
                foreach (var actionName in actions) {
                    if (!newActions.Contains(actionName))
                        newActions.Add(actionName);
                }
                SetProfileActions(newActions);
            }

            foreach (DS4ControlSettings dcs in DS4CSettings)
                dcs.Reset();

#if false
            containsCustomAction = false;
            containsCustomExtras = false;
#endif
            DS4KeyType keyType;

            Dictionary<DS4Controls, X360Controls> customMapButtons = new Dictionary<DS4Controls, X360Controls>();
            Dictionary<DS4Controls, String> customMapMacros = new Dictionary<DS4Controls, String>();
            Dictionary<DS4Controls, UInt16> customMapKeys = new Dictionary<DS4Controls, UInt16>();
            Dictionary<DS4Controls, String> customMapExtras = new Dictionary<DS4Controls, String>();
            Dictionary<DS4Controls, DS4KeyType> customMapKeyTypes = new Dictionary<DS4Controls, DS4KeyType>();
            Dictionary<DS4Controls, X360Controls> shiftCustomMapButtons = new Dictionary<DS4Controls, X360Controls>();
            Dictionary<DS4Controls, String> shiftCustomMapMacros = new Dictionary<DS4Controls, String>();
            Dictionary<DS4Controls, UInt16> shiftCustomMapKeys = new Dictionary<DS4Controls, UInt16>();
            Dictionary<DS4Controls, String> shiftCustomMapExtras = new Dictionary<DS4Controls, String>();
            Dictionary<DS4Controls, DS4KeyType> shiftCustomMapKeyTypes = new Dictionary<DS4Controls, DS4KeyType>();

            {
                foreach (XmlNode item in ldr.ChildNodes("Control/Button")) {
                    UpdateDS4CSetting(item.Name, false, AppState.GetX360ControlsByName(item.InnerText), "", DS4KeyType.None, 0);
                    customMapButtons.Add(AppState.GetDS4ControlsByName(item.Name), AppState.GetX360ControlsByName(item.InnerText));
                }

                foreach (XmlNode item in ldr.ChildNodes("Control/Macro")) {
                    customMapMacros.Add(AppState.GetDS4ControlsByName(item.Name), item.InnerText);
                    int[] keys = ldr.ParseInts(item.InnerText, '/');
                    UpdateDS4CSetting(item.Name, false, keys, "", DS4KeyType.None, 0);
                }

                foreach (XmlNode item in ldr.ChildNodes("Control/Key")) {
                    ushort wvk;
                    if (ushort.TryParse(item.InnerText, out wvk)) {
                        UpdateDS4CSetting(item.Name, false, wvk, "", DS4KeyType.None, 0);
                        customMapKeys.Add(AppState.GetDS4ControlsByName(item.Name), wvk);
                    }
                }

                foreach (XmlNode item in ldr.ChildNodes("/Control/Extras")) {
                    if (item.InnerText != string.Empty) {
                        UpdateDS4CExtra(item.Name, false, item.InnerText);
                        customMapExtras.Add(AppState.GetDS4ControlsByName(item.Name), item.InnerText);
                    }
                    else item.ParentNode.RemoveChild(item);
                }

                foreach (XmlNode item in ldr.ChildNodes("Control/KeyType")) {
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
                    if (keyType != DS4KeyType.None) {
                        UpdateDS4CKeyType(item.Name, false, keyType);
                        customMapKeyTypes.Add(AppState.GetDS4ControlsByName(item.Name), keyType);
                    }
                }

                foreach (XmlElement item in ldr.ChildNodes("ShiftControl/Button")) {
                    int shiftT = shiftM;
                    if (item.HasAttribute("Trigger"))
                        int.TryParse(item.Attributes["Trigger"].Value, out shiftT);
                    UpdateDS4CSetting(item.Name, true, AppState.GetX360ControlsByName(item.InnerText), "", DS4KeyType.None, shiftT);
                    shiftCustomMapButtons.Add(AppState.GetDS4ControlsByName(item.Name), AppState.GetX360ControlsByName(item.InnerText));
                }

                foreach (XmlElement item in ldr.ChildNodes("ShiftControl/Macro")) {
                    shiftCustomMapMacros.Add(AppState.GetDS4ControlsByName(item.Name), item.InnerText);
                    int[] keys = ldr.ParseInts(item.InnerText, '/');
                    int shiftT = shiftM;
                    if (item.HasAttribute("Trigger"))
                        int.TryParse(item.Attributes["Trigger"].Value, out shiftT);
                    UpdateDS4CSetting(item.Name, true, keys, "", DS4KeyType.None, shiftT);
                }

                foreach (XmlElement item in ldr.ChildNodes("ShiftControl/Key")) {
                    ushort wvk;
                    if (ushort.TryParse(item.InnerText, out wvk)) {
                        int shiftT = shiftM;
                        if (item.HasAttribute("Trigger"))
                            int.TryParse(item.Attributes["Trigger"].Value, out shiftT);
                        UpdateDS4CSetting(item.Name, true, wvk, "", DS4KeyType.None, shiftT);
                        shiftCustomMapKeys.Add(AppState.GetDS4ControlsByName(item.Name), wvk);
                    }
                }

                foreach (XmlElement item in ldr.ChildNodes("ShiftControl/Extras")) {
                    if (item.InnerText != string.Empty) {
                        UpdateDS4CExtra(item.Name, true, item.InnerText);
                        shiftCustomMapExtras.Add(AppState.GetDS4ControlsByName(item.Name), item.InnerText);
                    }
                    else
                        item.ParentNode.RemoveChild(item);
                }

                foreach (XmlElement item in ldr.ChildNodes("ShiftControl/KeyType")) {
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
                        UpdateDS4CKeyType(item.Name, true, keyType);
                        shiftCustomMapKeyTypes.Add(AppState.GetDS4ControlsByName(item.Name), keyType);
                    }
                }
            }

            return !ldr.missingSetting;
        }

        public void PostLoad(bool launchProgram, ControlService control, bool xinputChange, bool postLoadTransfer)
        {
            var aux = API.Aux(devIndex);
            bool oldUseDInputOnly = API.Aux(devIndex).UseDInputOnly;
            bool xinputPlug = false;
            bool xinputStatus = false;

            if (launchProgram && LaunchProgram != string.Empty)
            {
                string programPath = LaunchProgram;
                System.Diagnostics.Process[] localAll = System.Diagnostics.Process.GetProcesses();
                bool procFound = false;
                foreach (var process in localAll)
                {
                    try
                    {
                        string temp = process.MainModule.FileName;
                        if (temp == programPath)
                        {
                            procFound = true;
                            break;
                        }
                    }
                    catch { }
                    // Ignore any process for which this information
                    // is not exposed
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

            if (StartTouchpadOff) control.StartTPOff(devIndex);
            SetGyroMouseDeadZone(GyroMouseDeadZone, control);
            SetGyroMouseToggle(GyroMouseToggle, control);
            // Only change xinput devices under certain conditions. Avoid
            // performing this upon program startup before loading devices.

            if (xinputChange && devIndex < 4)
            {
                DS4Device tempDevice = control.DS4Controllers[devIndex];
                bool exists = (tempDevice != null);
                bool synced = exists ? tempDevice.isSynced() : false;
                bool isAlive = exists ? tempDevice.IsAlive() : false;
                if (DInputOnly != oldUseDInputOnly)
                {
                    if (DInputOnly)
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
                else if (aux.PreviousOutputDevType != OutputDevType)
                {
                    xinputPlug = true;
                    xinputStatus = true;
                }
            }

            // If a device exists, make sure to transfer relevant profile device
            // options to device instance
            if (postLoadTransfer && devIndex < 4) { 
                DS4Device tempDev = control.DS4Controllers[devIndex];
                if (tempDev != null && tempDev.isSynced()) {
                    tempDev.queueEvent(() =>
                    {
                        tempDev.setIdleTimeout(IdleDisconnectTimeout);
                        tempDev.setBTPollRate(BTPollRate);
                        if (xinputStatus && xinputPlug)
                        {
                            OutputDevice tempOutDev = control.outputDevices[devIndex];
                            if (tempOutDev != null)
                            {
                                string tempType = tempOutDev.GetDeviceType();
                                AppLogger.LogToGui("Unplug " + tempType + " Controller #" + (devIndex + 1), false);
                                tempOutDev.Disconnect();
                                tempOutDev = null;
                                control.outputDevices[devIndex] = null;
                            }

                            OutContType tempContType = OutputDevType;
                            if (tempContType == OutContType.X360)
                            {
                                Xbox360OutDevice tempXbox = new Xbox360OutDevice(control.vigemTestClient);
                                control.outputDevices[devIndex] = tempXbox;
                                tempXbox.cont.FeedbackReceived += (eventsender, args) =>
                                {
                                    control.SetDevRumble(tempDev, args.LargeMotor, args.SmallMotor, devIndex);
                                };

                                tempXbox.Connect();
                                AppLogger.LogToGui("X360 Controller #" + (devIndex + 1) + " connected", false);
                            }
                            else if (tempContType == OutContType.DS4)
                            {
                                DS4OutDevice tempDS4 = new DS4OutDevice(control.vigemTestClient);
                                control.outputDevices[devIndex] = tempDS4;
                                tempDS4.cont.FeedbackReceived += (eventsender, args) =>
                                {
                                    control.SetDevRumble(tempDev, args.LargeMotor, args.SmallMotor, devIndex);
                                };

                                tempDS4.Connect();
                                AppLogger.LogToGui("DS4 Controller #" + (devIndex + 1) + " connected", false);
                            }

                            aux.UseDInputOnly = false;
                        }
                        else if (xinputStatus && !xinputPlug)
                        {
                            string tempType = control.outputDevices[devIndex].GetDeviceType();
                            control.outputDevices[devIndex].Disconnect();
                            control.outputDevices[devIndex] = null;
                            aux.UseDInputOnly = true;
                            AppLogger.LogToGui(tempType + " Controller #" + (devIndex + 1) + " unplugged", false);
                        }

                        tempDev.setRumble(0, 0);
                    });

                    Program.rootHub.touchPad[devIndex]?.ResetTrackAccel(TrackballFriction);
                }
            }
        }

        public class Saver
        {
            public XmlDocument doc = new XmlDocument();
            public XmlNode node;

            public void Append(string name, string value)
            {
                var child = doc.CreateNode(XmlNodeType.Element, name, null);
                child.InnerText = value;
                node.AppendChild(child);
            }
            public void Append(string name, bool value) => Append(name, value.ToString());
            public void Append(string name, int value) => Append(name, value.ToString());
            public void Append(string name, double value) => Append(name, value.ToString());
            public void Append(string name, DS4Color color) => Append(name, color.toXMLText());
        }

        public bool SaveProfile(string profilePath)
        {
            IDeviceAuxiliaryConfig aux = API.Aux(devIndex);
            bool Saved = true;
            string path = $"{API.Config.AppDataPath}\\Profiles\\{Path.GetFileNameWithoutExtension(profilePath)}.xml";

            try {
                Saver svr = new Saver();
                var Xdoc = svr.doc;
                XmlNode Node;
                XmlNode xmlControls = Xdoc.SelectSingleNode("/DS4Windows/Control");
                XmlNode xmlShiftControls = Xdoc.SelectSingleNode("/DS4Windows/ShiftControl");

                Node = Xdoc.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
                Xdoc.AppendChild(Node);

                Node = Xdoc.CreateComment($" DS4Windows Configuration Data. {DateTime.Now} ");
                Xdoc.AppendChild(Node);

                Node = Xdoc.CreateWhitespace("\r\n");
                Xdoc.AppendChild(Node);

                Node = Xdoc.CreateNode(XmlNodeType.Element, "DS4Windows", null);
                svr.node = Node;

                svr.Append("flushHIDQueue", FlushHIDQueue);
                svr.Append("touchToggle", EnableTouchToggle);
                svr.Append("idleDisconnectTimeout", IdleDisconnectTimeout);
                svr.Append("Color", MainColor);
                svr.Append("RumbleBoost", RumbleBoost);
                svr.Append("ledAsBatteryIndicator", LedAsBatteryIndicator);
                svr.Append("FlashType", FlashType);
                svr.Append("flashBatteryAt", FlashBatteryAt);
                svr.Append("touchSensitivity", TouchSensitivity);
                svr.Append("LowColor", LowColor);
                svr.Append("ChargingColor", ChargingColor);
                svr.Append("FlashColor", FlashColor);

                svr.Append("touchpadJitterCompensation", TouchpadJitterCompensation);
                svr.Append("lowerRCOn", LowerRCOn);
                svr.Append("tapSensitivity", TapSensitivity);
                svr.Append("doubleTap", DoubleTap);
                svr.Append("scrollSensitivity", ScrollSensitivity);

                svr.Append("LeftTriggerMiddle", L2.DeadZone);
                svr.Append("RightTriggerMiddle", R2.DeadZone);
                svr.Append("TouchpadInvert", TouchpadInvert);
                svr.Append("L2AntiDeadZone", L2.AntiDeadZone);
                svr.Append("R2AntiDeadZone", R2.AntiDeadZone);
                svr.Append("L2MaxZone", L2.MaxZone);
                svr.Append("R2MaxZone", R2.MaxZone);
                svr.Append("buttonMouseSensitivity", ButtonMouseSensitivity);
                svr.Append("Rainbow", Rainbow);

                svr.Append("LSDeadZone", LS.DeadZone);
                svr.Append("RSDeadZone", RS.DeadZone);
                svr.Append("LSAntiDeadZone", LS.AntiDeadZone);
                svr.Append("RSAntiDeadZone", RS.AntiDeadZone);
                svr.Append("LSMaxZone", LS.MaxZone);
                svr.Append("RSMaxZone", RS.MaxZone);
                svr.Append("LSRotation", (int)(LS.Rotation * 180.0 / Math.PI));
                svr.Append("RSRotation", (int)(RS.Rotation * 180.0 / Math.PI));
                
                svr.Append("SXDeadZone", SX.DeadZone);
                svr.Append("SZDeadZone", SZ.DeadZone);
                svr.Append("SXMaxZone", (int)(SX.MaxZone * 100.0));
                svr.Append("SZMaxZone", (int)(SZ.MaxZone * 100.0));

                svr.Append("SXAntiDeadZone", (int)(SX.AntiDeadZone * 100.0));
                svr.Append("SZAntiDeadZone", (int)(SZ.AntiDeadZone * 100.0));

                svr.Append("Sensitivity", $"{LS.Sensitivity}|{RS.Sensitivity}|{L2.Sensitivity}|{R2.Sensitivity}|{SX.Sensitivity}|{SZ.Sensitivity}";

                svr.Append("ChargingType", ChargingType);
                svr.Append("MouseAcceleration", MouseAccel);
                //svr.Append("ShiftModifier", ShiftModifier);
                svr.Append("LaunchProgram", LaunchProgram);
                svr.Append("DinputOnly", DInputOnly);
                svr.Append("StartTouchpadOff", StartTouchpadOff);
                svr.Append("UseTPforControls", UseTPforControls);
                svr.Append("UseSAforMouse", UseSAforMouse);
                svr.Append("SATriggers", SATriggers);
                svr.Append("SATriggerCond", saTriggerCond(SATriggerCond));
                svr.Append("SASteeringWheelEmulationAxis", SASteeringWheelEmulationAxis.ToString("G"));
                svr.Append("SASteeringWheelEmulationRange", SASteeringWheelEmulationRange);
                svr.Append("TouchDisInvTriggers", string.Join(",", TouchDisInvertTriggers));

                svr.Append("GyroSensitivity", GyroSensitivity);
                svr.Append("GyroSensVerticalScale", GyroSensVerticalScale);
                svr.Append("GyroInvert", GyroInvert);
                svr.Append("GyroTriggerTurns", GyroTriggerTurns);
                svr.Append("GyroSmoothingWeight", (int)(GyroSmoothingWeight * 100.0));
                svr.Append("GyroSmoothing", GyroSmoothingWeight);
                svr.Append("GyroMouseHAxis", GyroMouseHorizontalAxis);
                svr.Append("GyroMouseDeadZone", GyroMouseDeadZone);
                svr.Append("GyroMouseToggle", GyroMouseToggle);
                svr.Append("LSCurve", LS.Curve);
                svr.Append("RSCurve", RS.Curve);
                svr.Append("ProfileActions", string.Join("/", ProfileActions));
                svr.Append("BTPollRate", BTPollRate);

                svr.Append("LSOutputCurveMode", outputCurve(LS.OutCurvePreset));
                svr.Append("LSOutputCurveCustom", LS.OutBezierCurve.ToString());
                svr.Append("RSOutputCurveMode", outputCurve(RS.OutCurvePreset));
                svr.Append("RSOutputCurveCustom", RS.OutBezierCurve.ToString());

                svr.Append("LSSquareStick", SquStick.LSMode);
                svr.Append("RSSquareStick", SquStick.RSMode);
                svr.Append("SquareStickRoundness", SquStick.Roundness);

                svr.Append("L2OutputCurveMode", outputCurve(L2.OutCurvePreset));
                svr.Append("L2OutputCurveCustom", L2.OutBezierCurve.ToString());
                svr.Append("R2OutputCurveMode", outputCurve(R2.OutCurvePreset));
                svr.Append("R2OutputCurveCustom", R2.OutBezierCurve.ToString());

                svr.Append("SXOutputCurveMode", outputCurve(L2.OutCurvePreset));
                svr.Append("SXOutputCurveCustom", L2.OutBezierCurve.ToString());
                svr.Append("SZOutputCurveMode", outputCurve(R2.OutCurvePreset));
                svr.Append("SZOutputCurveCustom", R2.OutBezierCurve.ToString());


                XmlNode xmlSZOutputCurveMode = Xdoc.CreateNode(XmlNodeType.Element, "SZOutputCurveMode", null); xmlSZOutputCurveMode.InnerText = axisOutputCurveString(dev.szOutCurveMode); Node.AppendChild(xmlSZOutputCurveMode);
                XmlNode xmlSZOutputCurveCustom = Xdoc.CreateNode(XmlNodeType.Element, "SZOutputCurveCustom", null); xmlSZOutputCurveCustom.InnerText = dev.szOutBezierCurveObj.ToString(); Node.AppendChild(xmlSZOutputCurveCustom);

                XmlNode xmlTrackBallMode = Xdoc.CreateNode(XmlNodeType.Element, "TrackballMode", null); xmlTrackBallMode.InnerText = dev.trackballMode.ToString(); Node.AppendChild(xmlTrackBallMode);
                XmlNode xmlTrackBallFriction = Xdoc.CreateNode(XmlNodeType.Element, "TrackballFriction", null); xmlTrackBallFriction.InnerText = dev.trackballFriction.ToString(); Node.AppendChild(xmlTrackBallFriction);

                XmlNode xmlOutContDevice = Xdoc.CreateNode(XmlNodeType.Element, "OutputContDevice", null); xmlOutContDevice.InnerText = OutContDeviceString(dev.outputDevType); Node.AppendChild(xmlOutContDevice);

                XmlNode NodeControl = Xdoc.CreateNode(XmlNodeType.Element, "Control", null);
                XmlNode Key = Xdoc.CreateNode(XmlNodeType.Element, "Key", null);
                XmlNode Macro = Xdoc.CreateNode(XmlNodeType.Element, "Macro", null);
                XmlNode KeyType = Xdoc.CreateNode(XmlNodeType.Element, "KeyType", null);
                XmlNode Button = Xdoc.CreateNode(XmlNodeType.Element, "Button", null);
                XmlNode Extras = Xdoc.CreateNode(XmlNodeType.Element, "Extras", null);

                XmlNode NodeShiftControl = Xdoc.CreateNode(XmlNodeType.Element, "ShiftControl", null);

                XmlNode ShiftKey = Xdoc.CreateNode(XmlNodeType.Element, "Key", null);
                XmlNode ShiftMacro = Xdoc.CreateNode(XmlNodeType.Element, "Macro", null);
                XmlNode ShiftKeyType = Xdoc.CreateNode(XmlNodeType.Element, "KeyType", null);
                XmlNode ShiftButton = Xdoc.CreateNode(XmlNodeType.Element, "Button", null);
                XmlNode ShiftExtras = Xdoc.CreateNode(XmlNodeType.Element, "Extras", null);

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
                            buttonNode = Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
                            buttonNode.InnerText = keyType;
                            KeyType.AppendChild(buttonNode);
                        }

                        buttonNode = Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
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
                        XmlNode extraNode = Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
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
                            buttonNode = Xdoc.CreateElement(dcs.control.ToString());
                            buttonNode.InnerText = keyType;
                            ShiftKeyType.AppendChild(buttonNode);
                        }

                        buttonNode = Xdoc.CreateElement(dcs.control.ToString());
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
                        XmlNode extraNode = Xdoc.CreateNode(XmlNodeType.Element, dcs.control.ToString(), null);
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

                Xdoc.AppendChild(Node);
                Xdoc.Save(path);
            }
            catch { Saved = false; }
            return Saved;
        }

    }

    public class GlobalConfig : IGlobalConfig
    {
        // fifth value used for options, not fifth controller
        readonly DeviceConfig[] cfg = new DeviceConfig[5];
        readonly DeviceAuxiliaryConfig[] aux = new DeviceAuxiliaryConfig[5];
        public IDeviceConfig Cfg(int index) => cfg[index];
        public IDeviceAuxiliaryConfig Aux(int index) => aux[index];

        public Dictionary<string, string> linkedProfiles = new Dictionary<string, string>();

        // general values
        public bool UseExclusiveMode { get; set; } = false;
        public int FormWidth { get; set; } = 782;
        public int FormHeight { get; set; } = 550;
        public int FormLocationX { get; set; } = 0;
        public int FormLocationY { get; set; } = 0;
        public bool StartMinimized { get; set; } = false;
        public bool MinToTaskbar { get; set; } = false;
        public DateTime LastChecked { get; set; }
        public int CheckWhen { get; set; } = 1;
        public int Notifications { get; set; } = 2;
        public bool DisconnectBTAtStop { get; set; } = false;
        public bool SwipeProfiles { get; set; } = true;
        public bool DS4Mapping { get; set; } = false;
        public bool QuickCharge { get; set; } = false;
        public bool CloseMini { get; set; } = false;

        public string UseLang { get; set; } = string.Empty;
        public bool DownloadLang { get; set; } = true;
        public bool UseWhiteIcon { get; set; }
        public bool FlashWhenLate { get; set; } = true;
        public int FlashWhenLateAt { get; set; } = 20;
        public bool UseUDPServer { get; set; } = false;
        public int UDPServerPort { get; set; } = 26760;
        public string UDPServerListenAddress { get; set; } = "127.0.0.1"; // 127.0.0.1=IPAddress.Loopback (default), 0.0.0.0=IPAddress.Any as all interfaces, x.x.x.x = Specific ipv4 interface address or hostname
        public bool UseCustomSteamFolder { get; set; }
        public string CustomSteamFolder { get; set; }

        private void CreateStdActions()
        {
            XmlDocument xDoc = new XmlDocument();
            try
            {
                string[] profiles = Directory.GetFiles($"{API.AppDataPath}\\Profiles\\");
                foreach (var s in profiles.Where(s => Path.GetExtension(s) == ".xml"))
                {
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

        private List<SpecialAction> actions;
        private Dictionary<string, SpecialAction> actionsDict;
        public List<SpecialAction> Actions { get => actions; }

        public SpecialAction ActionByName(string name)
        {
            SpecialAction sA;
            if (!actionsDict.TryGetValue(name, out sA))
                return new SpecialAction();
            return sA;
        }

        public SpecialAction ActionByIndex(int index)
        {
            return (index >= 0 && index < Actions.Count) ? Actions[index] : null;
        }

        public int LookupActionIndexOf(string name)
        {
            int i = 0;
            foreach (var action in actions)
            {
                if (action.name == name) return i;
                i++;
            }
            return -1;
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

        public bool Load()
        {
            bool Loaded = true;
            bool missingSetting = false;

            try
            {
                if (File.Exists(m_Profile))
                {
                    XmlNode Item;

                    Xdoc.Load(m_Profile);

                    try { Item = Xdoc.SelectSingleNode("/Profile/useExclusiveMode"); Boolean.TryParse(Item.InnerText, out useExclusiveMode); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/startMinimized"); Boolean.TryParse(Item.InnerText, out startMinimized); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/minimizeToTaskbar"); Boolean.TryParse(Item.InnerText, out minToTaskbar); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/formWidth"); Int32.TryParse(Item.InnerText, out formWidth); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/formHeight"); Int32.TryParse(Item.InnerText, out formHeight); }
                    catch { missingSetting = true; }
                    try {
                        int temp = 0;
                        Item = Xdoc.SelectSingleNode("/Profile/formLocationX"); Int32.TryParse(Item.InnerText, out temp);
                        formLocationX = Math.Max(temp, 0);
                    }
                    catch { missingSetting = true; }

                    try {
                        int temp = 0;
                        Item = Xdoc.SelectSingleNode("/Profile/formLocationY"); Int32.TryParse(Item.InnerText, out temp);
                        formLocationY = Math.Max(temp, 0);
                    }
                    catch { missingSetting = true; }

                    for (int i = 0; i < 4; i++) {
                        try {
                            Item = Xdoc.SelectSingleNode($"/Profile/Controller{i + 1}");
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
               
                    try { Item = Xdoc.SelectSingleNode("/Profile/LastChecked"); DateTime.TryParse(Item.InnerText, out lastChecked); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/CheckWhen"); Int32.TryParse(Item.InnerText, out CheckWhen); }
                    catch { missingSetting = true; }

                    try
                    {
                        Item = Xdoc.SelectSingleNode("/Profile/Notifications");
                        if (!int.TryParse(Item.InnerText, out Notifications))
                            Notifications = (Boolean.Parse(Item.InnerText) ? 2 : 0);
                    }
                    catch { missingSetting = true; }

                    try { Item = Xdoc.SelectSingleNode("/Profile/DisconnectBTAtStop"); Boolean.TryParse(Item.InnerText, out disconnectBTAtStop); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/SwipeProfiles"); Boolean.TryParse(Item.InnerText, out swipeProfiles); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/UseDS4ForMapping"); Boolean.TryParse(Item.InnerText, out ds4Mapping); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/QuickCharge"); Boolean.TryParse(Item.InnerText, out quickCharge); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/CloseMinimizes"); Boolean.TryParse(Item.InnerText, out closeMini); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/UseLang"); useLang = Item.InnerText; }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/DownloadLang"); Boolean.TryParse(Item.InnerText, out downloadLang); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/FlashWhenLate"); Boolean.TryParse(Item.InnerText, out flashWhenLate); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/FlashWhenLateAt"); int.TryParse(Item.InnerText, out flashWhenLateAt); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/WhiteIcon"); Boolean.TryParse(Item.InnerText, out useWhiteIcon); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/UseUDPServer"); Boolean.TryParse(Item.InnerText, out useUDPServ); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/UDPServerPort"); int temp; int.TryParse(Item.InnerText, out temp); udpServPort = Math.Min(Math.Max(temp, 1024), 65535); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/UDPServerListenAddress"); udpServListenAddress = Item.InnerText; }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/UseCustomSteamFolder"); Boolean.TryParse(Item.InnerText, out useCustomSteamFolder); }
                    catch { missingSetting = true; }
                    try { Item = Xdoc.SelectSingleNode("/Profile/CustomSteamFolder"); customSteamFolder = Item.InnerText; }
                    catch { missingSetting = true; }

                    for (int i = 0; i < 4; i++)
                    {
                        var dev = this.dev[i];
                        try
                        {
                            Item = Xdoc.SelectSingleNode("/Profile/CustomLed" + (i + 1));
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

            Xdoc.RemoveAll();

            Node = Xdoc.CreateXmlDeclaration("1.0", "utf-8", String.Empty);
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateComment(String.Format(" Profile Configuration Data. {0} ", DateTime.Now));
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateWhitespace("\r\n");
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateNode(XmlNodeType.Element, "Profile", null);

            XmlNode xmlUseExclNode = Xdoc.CreateNode(XmlNodeType.Element, "useExclusiveMode", null); xmlUseExclNode.InnerText = useExclusiveMode.ToString(); Node.AppendChild(xmlUseExclNode);
            XmlNode xmlStartMinimized = Xdoc.CreateNode(XmlNodeType.Element, "startMinimized", null); xmlStartMinimized.InnerText = startMinimized.ToString(); Node.AppendChild(xmlStartMinimized);
            XmlNode xmlminToTaskbar = Xdoc.CreateNode(XmlNodeType.Element, "minimizeToTaskbar", null); xmlminToTaskbar.InnerText = minToTaskbar.ToString(); Node.AppendChild(xmlminToTaskbar);
            XmlNode xmlFormWidth = Xdoc.CreateNode(XmlNodeType.Element, "formWidth", null); xmlFormWidth.InnerText = formWidth.ToString(); Node.AppendChild(xmlFormWidth);
            XmlNode xmlFormHeight = Xdoc.CreateNode(XmlNodeType.Element, "formHeight", null); xmlFormHeight.InnerText = formHeight.ToString(); Node.AppendChild(xmlFormHeight);
            XmlNode xmlFormLocationX = Xdoc.CreateNode(XmlNodeType.Element, "formLocationX", null); xmlFormLocationX.InnerText = formLocationX.ToString(); Node.AppendChild(xmlFormLocationX);
            XmlNode xmlFormLocationY = Xdoc.CreateNode(XmlNodeType.Element, "formLocationY", null); xmlFormLocationY.InnerText = formLocationY.ToString(); Node.AppendChild(xmlFormLocationY);

            XmlNode xmlController1 = Xdoc.CreateNode(XmlNodeType.Element, "Controller1", null); xmlController1.InnerText = !Global.aux[0].LinkedProfileCheck ? dev[0].profilePath : dev[0].olderProfilePath; Node.AppendChild(xmlController1);
            XmlNode xmlController2 = Xdoc.CreateNode(XmlNodeType.Element, "Controller2", null); xmlController2.InnerText = !Global.aux[1].LinkedProfileCheck ? dev[1].profilePath : dev[1].olderProfilePath; Node.AppendChild(xmlController2);
            XmlNode xmlController3 = Xdoc.CreateNode(XmlNodeType.Element, "Controller3", null); xmlController3.InnerText = !Global.aux[2].LinkedProfileCheck ? dev[2].profilePath : dev[2].olderProfilePath; Node.AppendChild(xmlController3);
            XmlNode xmlController4 = Xdoc.CreateNode(XmlNodeType.Element, "Controller4", null); xmlController4.InnerText = !Global.aux[3].LinkedProfileCheck ? dev[3].profilePath : dev[3].olderProfilePath; Node.AppendChild(xmlController4);

            XmlNode xmlLastChecked = Xdoc.CreateNode(XmlNodeType.Element, "LastChecked", null); xmlLastChecked.InnerText = lastChecked.ToString(); Node.AppendChild(xmlLastChecked);
            XmlNode xmlCheckWhen = Xdoc.CreateNode(XmlNodeType.Element, "CheckWhen", null); xmlCheckWhen.InnerText = CheckWhen.ToString(); Node.AppendChild(xmlCheckWhen);
            XmlNode xmlNotifications = Xdoc.CreateNode(XmlNodeType.Element, "Notifications", null); xmlNotifications.InnerText = Notifications.ToString(); Node.AppendChild(xmlNotifications);
            XmlNode xmlDisconnectBT = Xdoc.CreateNode(XmlNodeType.Element, "DisconnectBTAtStop", null); xmlDisconnectBT.InnerText = disconnectBTAtStop.ToString(); Node.AppendChild(xmlDisconnectBT);
            XmlNode xmlSwipeProfiles = Xdoc.CreateNode(XmlNodeType.Element, "SwipeProfiles", null); xmlSwipeProfiles.InnerText = swipeProfiles.ToString(); Node.AppendChild(xmlSwipeProfiles);
            XmlNode xmlDS4Mapping = Xdoc.CreateNode(XmlNodeType.Element, "UseDS4ForMapping", null); xmlDS4Mapping.InnerText = ds4Mapping.ToString(); Node.AppendChild(xmlDS4Mapping);
            XmlNode xmlQuickCharge = Xdoc.CreateNode(XmlNodeType.Element, "QuickCharge", null); xmlQuickCharge.InnerText = quickCharge.ToString(); Node.AppendChild(xmlQuickCharge);
            XmlNode xmlCloseMini = Xdoc.CreateNode(XmlNodeType.Element, "CloseMinimizes", null); xmlCloseMini.InnerText = closeMini.ToString(); Node.AppendChild(xmlCloseMini);
            XmlNode xmlUseLang = Xdoc.CreateNode(XmlNodeType.Element, "UseLang", null); xmlUseLang.InnerText = useLang.ToString(); Node.AppendChild(xmlUseLang);
            XmlNode xmlDownloadLang = Xdoc.CreateNode(XmlNodeType.Element, "DownloadLang", null); xmlDownloadLang.InnerText = downloadLang.ToString(); Node.AppendChild(xmlDownloadLang);
            XmlNode xmlFlashWhenLate = Xdoc.CreateNode(XmlNodeType.Element, "FlashWhenLate", null); xmlFlashWhenLate.InnerText = flashWhenLate.ToString(); Node.AppendChild(xmlFlashWhenLate);
            XmlNode xmlFlashWhenLateAt = Xdoc.CreateNode(XmlNodeType.Element, "FlashWhenLateAt", null); xmlFlashWhenLateAt.InnerText = flashWhenLateAt.ToString(); Node.AppendChild(xmlFlashWhenLateAt);
            XmlNode xmlWhiteIcon = Xdoc.CreateNode(XmlNodeType.Element, "WhiteIcon", null); xmlWhiteIcon.InnerText = useWhiteIcon.ToString(); Node.AppendChild(xmlWhiteIcon);
            XmlNode xmlUseUDPServ = Xdoc.CreateNode(XmlNodeType.Element, "UseUDPServer", null); xmlUseUDPServ.InnerText = useUDPServ.ToString(); Node.AppendChild(xmlUseUDPServ);
            XmlNode xmlUDPServPort = Xdoc.CreateNode(XmlNodeType.Element, "UDPServerPort", null); xmlUDPServPort.InnerText = udpServPort.ToString(); Node.AppendChild(xmlUDPServPort);
            XmlNode xmlUDPServListenAddress = Xdoc.CreateNode(XmlNodeType.Element, "UDPServerListenAddress", null); xmlUDPServListenAddress.InnerText = udpServListenAddress; Node.AppendChild(xmlUDPServListenAddress);
            XmlNode xmlUseCustomSteamFolder = Xdoc.CreateNode(XmlNodeType.Element, "UseCustomSteamFolder", null); xmlUseCustomSteamFolder.InnerText = useCustomSteamFolder.ToString(); Node.AppendChild(xmlUseCustomSteamFolder);
            XmlNode xmlCustomSteamFolder = Xdoc.CreateNode(XmlNodeType.Element, "CustomSteamFolder", null); xmlCustomSteamFolder.InnerText = customSteamFolder; Node.AppendChild(xmlCustomSteamFolder);

            for (int i = 0; i < 4; i++)
            {
                var dev = this.dev[i];
                XmlNode xmlCustomLed = Xdoc.CreateNode(XmlNodeType.Element, "CustomLed" + (1 + i), null);
                xmlCustomLed.InnerText = dev.useCustomColor.ToString() + ":" + dev.customColor.toXMLText();
                Node.AppendChild(xmlCustomLed);
            }

            Xdoc.AppendChild(Node);

            try { Xdoc.Save(m_Profile); }
            catch (UnauthorizedAccessException) { Saved = false; }
            return Saved;
        }

        private void CreateAction()
        {
            XmlDocument Xdoc = new XmlDocument();
            XmlNode Node;

            Node = Xdoc.CreateXmlDeclaration("1.0", "utf-8", String.Empty);
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateComment(String.Format(" Special Actions Configuration Data. {0} ", DateTime.Now));
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateWhitespace("\r\n");
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateNode(XmlNodeType.Element, "Actions", "");
            Xdoc.AppendChild(Node);
            Xdoc.Save(m_Actions);
        }

        public bool SaveAction(string name, string controls, int mode, string details, bool edit, string extras = "")
        {
            bool saved = true;
            if (!File.Exists(m_Actions))
                CreateAction();
            Xdoc.Load(m_Actions);
            XmlNode Node;

            Node = Xdoc.CreateComment(String.Format(" Special Actions Configuration Data. {0} ", DateTime.Now));
            foreach (XmlNode node in Xdoc.SelectNodes("//comment()"))
                node.ParentNode.ReplaceChild(Node, node);

            Node = Xdoc.SelectSingleNode("Actions");
            XmlElement el = Xdoc.CreateElement("Action");
            el.SetAttribute("Name", name);
            el.AppendChild(Xdoc.CreateElement("Trigger")).InnerText = controls;
            switch (mode)
            {
                case 1:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "Macro";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details;
                    if (extras != string.Empty)
                        el.AppendChild(Xdoc.CreateElement("Extras")).InnerText = extras;
                    break;
                case 2:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "Program";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details.Split('?')[0];
                    el.AppendChild(Xdoc.CreateElement("Arguements")).InnerText = extras;
                    el.AppendChild(Xdoc.CreateElement("Delay")).InnerText = details.Split('?')[1];
                    break;
                case 3:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "Profile";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details;
                    el.AppendChild(Xdoc.CreateElement("UnloadTrigger")).InnerText = extras;
                    break;
                case 4:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "Key";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details;
                    if (!string.IsNullOrEmpty(extras))
                    {
                        string[] exts = extras.Split('\n');
                        el.AppendChild(Xdoc.CreateElement("UnloadTrigger")).InnerText = exts[1];
                        el.AppendChild(Xdoc.CreateElement("UnloadStyle")).InnerText = exts[0];
                    }
                    break;
                case 5:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "DisconnectBT";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details;
                    break;
                case 6:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "BatteryCheck";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details;
                    break;
                case 7:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "MultiAction";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details;
                    break;
                case 8:
                    el.AppendChild(Xdoc.CreateElement("Type")).InnerText = "SASteeringWheelEmulationCalibrate";
                    el.AppendChild(Xdoc.CreateElement("Details")).InnerText = details;
                    break;
            }

            if (edit)
            {
                XmlNode oldxmlprocess = Xdoc.SelectSingleNode("/Actions/Action[@Name=\"" + name + "\"]");
                Node.ReplaceChild(el, oldxmlprocess);
            }
            else { Node.AppendChild(el); }

            Xdoc.AppendChild(Node);
            try { Xdoc.Save(m_Actions); }
            catch { saved = false; }
            LoadActions();

            Mapping.actionDone.Add(new Mapping.ActionState());
            return saved;
        }

        public void RemoveAction(string name)
        {
            Xdoc.Load(m_Actions);
            XmlNode Node = Xdoc.SelectSingleNode("Actions");
            XmlNode Item = Xdoc.SelectSingleNode("/Actions/Action[@Name=\"" + name + "\"]");
            if (Item != null)
                Node.RemoveChild(Item);

            Xdoc.AppendChild(Node);
            Xdoc.Save(m_Actions);
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
            XmlDocument Xdoc = new XmlDocument();
            XmlNode Node;

            Node = Xdoc.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateComment(string.Format(" Mac Address and Profile Linking Data. {0} ", DateTime.Now));
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateWhitespace("\r\n");
            Xdoc.AppendChild(Node);

            Node = Xdoc.CreateNode(XmlNodeType.Element, "LinkedControllers", "");
            Xdoc.AppendChild(Node);

            try { Xdoc.Save(m_linkedProfiles); }
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

        internal SpecialAction()
        {
            Init("null", "null", "null", "null", 0, "");
        }

        public SpecialAction(string name, string controls, string type, string details, double delay = 0, string extras = "")
        {
            Init(name, controls, type, details, delay, extras);
        }

        private void Init(string name, string controls, string type, string details, double delay, string extras)
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

        private static DS4Controls getDS4ControlsByName(string key)
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
