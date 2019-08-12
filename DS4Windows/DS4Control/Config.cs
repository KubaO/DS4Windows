// Copyright Ⓒ 2019 Kuba Ober
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is furnished
// to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using DS4Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml;

namespace DS4Windows
{
    // These are the public configuration APIs that interface between modules.

    public class API
    {
        // That's our interface between modules.
        // It used to be configuration-related, but isn't anymore. It's the glue that integrates
        // the modules. It needs to be moved out of this file, probably to Program.cs
        public const int DS4_CONTROLLER_COUNT = 4;

        private static AppState app = new AppState();
        private static GlobalConfig config = new GlobalConfig();
        private static DS4LightBar[] lightBar = makePerDevice(i => new DS4LightBar(i));
        private static Mapping[] mappings = makePerDevice(i => new Mapping(i));

        public static IGlobalConfig Config = config;
        public static IDeviceConfig Cfg(int index) => Config.Cfg(index);
        public static IDeviceAuxiliaryConfig Aux(int index) => Config.Aux(index);
        public static DS4LightBar Bar(int index) => (index < lightBar.Length) ? lightBar[index] : null;
        public static Mapping Mapping(int index) => mappings[index];

        public static bool IsAdministrator { get => app.IsAdministrator; }

        public static bool IsViGEmBusInstalled() => DeviceDetection.IsViGEmBusInstalled();
        public static string ViGEmBusVersion { get; } = DeviceDetection.ViGEmBusVersion();

        internal static string GetDeviceProperty(string deviceInstanceId, NativeMethods.DEVPROPKEY prop) =>
            AppState.GetDeviceProperty(deviceInstanceId, prop);

        public static string AppDataPath { get => app.AppDataPath; set => app.AppDataPath = value; }
        public static string ExePath { get => AppState.ExePath; }
		public static bool ExePathNeedsAdmin { get => app.ExePathNeedsAdmin; }

        public static void FindConfigLocation() => app.FindConfigLocation();
        public static void SetCulture(string culture) => AppState.SetCulture(culture);

        public static string ProfileExePath { get => app.ProfileExePath; }
        public static string AutoProfileExePath { get => app.AutoProfileExePath; }
        public static string ProfileDataPath { get => app.ProfileDataPath; }
        public static string AutoProfileDataPath { get => app.AutoProfileDataPath; }
        public static string ActionsPath { get => app.ActionsPath; }
        public static string LinkedProfilesPath { get => app.LinkedProfilesPath; }
        public static string ControllerConfigsPath { get => app.ControllerConfigsPath; }

        public static bool IsFirstRun { get => app.IsFirstRun; }
        public static bool MultiSaveSpots { get => app.MultiSaveSpots; }
        public static bool RunHotPlug { get => app.RunHotPlug; set => app.RunHotPlug = value; }

        public static T[] makePerDevice<T>(Func<int, T> maker) where T : class
        {
            T[] result = new T[DS4_CONTROLLER_COUNT];
            for (int i = 0; i < result.Length; i++) result[i] = maker(i);
            return result;
        }

    }

    public interface IGlobalConfig
    {
        IDeviceConfig Cfg(int index);
        IDeviceAuxiliaryConfig Aux(int index);

        bool Load();
        bool Save();
        bool LoadActions();
        void CreateStdActions();
        bool LoadLinkedProfiles();
        bool SaveLinkedProfiles();

        bool UseExclusiveMode { get; set; }
        DateTime LastChecked { get; set; }
        int CheckWhen { get; set; }
        int Notifications { get; set; }
        bool DisconnectBTAtStop { get; set; }
        bool SwipeProfiles { get; set; }
        bool DS4Mapping { get; set; }
        bool QuickCharge { get; set; }
        bool CloseMinimizes { get; set; }
        bool StartMinimized { get; set; }
        bool MinToTaskbar { get; set; }
        System.Drawing.Size FormSize { get; set; }
        System.Drawing.Point FormLocation { get; set; }
        string UseLang { get; set; }
        bool DownloadLang { get; set; }
        bool FlashWhenLate { get; set; }
        int FlashWhenLateAt { get; set; }
        bool UseUDPServer { get; set; }
        int UDPServerPort { get; set; }
        string UDPServerListenAddress { get; set; }
        bool UseWhiteIcon { get; set; }
        bool UseCustomSteamFolder { get; set; }
        string CustomSteamFolder { get; set; }

        bool SaveControllerConfigs(DS4Device device = null);
        bool LoadControllerConfigs(DS4Device device = null);

        bool ContainsLinkedProfile(string serial);
        string GetLinkedProfile(string serial);
        void SetLinkedProfile(string serial, string profile);
        void RemoveLinkedProfile(string serial);

        List<SpecialAction> Actions { get; }
        SpecialAction ActionByName(string name);
        SpecialAction ActionByIndex(int index);
        int LookupActionIndexOf(string name);
        void RemoveAction(string name);
        bool SaveAction(string name, string controls, int mode, string details, bool edt, string extras = "");

        // These are in AppState
        //X360Controls[] DefaultButtonMapping { get;  }
        //DS4Controls[] ReverseX360ButtonMapping { get; }
    }

    public interface IDeviceAuxiliaryConfig
    {
        // These are implementation details that we most likely
        // shouldn't expose here.
        string TempProfileName { get; set; }
        bool UseTempProfile { get; }
        bool TempProfileDistance { get; }
        bool UseDInputOnly { get; set; }
        bool LinkedProfileCheck { get; set; } // applies between this and successor profile
        bool TouchpadActive { get; set; }

        OutContType PreviousOutputDevType { get; set; }
    }

    public enum SATriggerCondType { Or = 0, And = 1 };

    public interface IDeviceConfig
    {
        int DevIndex { get; }
        string ProfilePath { get; set; }
        string OlderProfilePath { get; set; }
        string LaunchProgram { get; set; }

        bool LoadProfile(bool launchProgram, DeviceControlService control, bool xinputChange = true, bool postLoad = true);
        bool LoadTempProfile(string name, bool launchProgram, DeviceControlService control, bool xinputChange = true);
        bool SaveProfile(string profilePath);

        bool Load(XmlDocument doc);
        void Save(XmlDocument doc);
        
        void PostLoad(bool launchProgram, DeviceControlService control,
            bool xinputChange = true, bool postLoadTransfer = true);
        // TODO: A hack that doesn't belong here - it's the leftover old code
        // that should be moved somewhere else.

        int BTPollRate { get; set; }
        bool FlushHIDQueue { get; set; }
        int IdleDisconnectTimeout { get; set; }
        byte RumbleBoost { get; set; }
        bool LowerRCOn { get; set; }
        int ChargingType { get; set; }
        bool DInputOnly { get; set; }

        byte FlashType { get; set; }
        int FlashBatteryAt { get; set; }
        double Rainbow { get; set; }
        bool LedAsBatteryIndicator { get; set; }
        DS4Color MainColor { get; set; }
        DS4Color LowColor { get; set; }
        DS4Color ChargingColor { get; set; }
        DS4Color CustomColor { get; set; }
        bool UseCustomColor { get; set; }
        DS4Color FlashColor { get; set; }

        bool EnableTouchToggle { get; set; }
        byte TouchSensitivity { get; set; }
        bool TouchpadJitterCompensation { get; set; }
        int TouchpadInvert { get; set; }
        bool StartTouchpadOff { get; set; } 
        bool UseTPforControls { get; set; }
        int[] TouchDisInvertTriggers { get; set; }
        byte TapSensitivity { get; set; }
        bool DoubleTap { get; set; }
        int ScrollSensitivity { get; set; }

        int GyroSensitivity { get; set; }
        int GyroSensVerticalScale { get; set; }
        int GyroInvert { get; set; }
        bool GyroTriggerTurns { get; set; }
        bool GyroSmoothing { get; set; }
        double GyroSmoothingWeight { get; set; }
        int GyroMouseHorizontalAxis { get; set; }
        int GyroMouseDeadZone { get; }
        bool GyroMouseToggle { get; }
        void SetGyroMouseDeadZone(int value, Mouse touchPad);
        void SetGyroMouseToggle(bool value, Mouse touchPad);

        int ButtonMouseSensitivity { get; set; }
        bool MouseAccel { get; set; }

        bool TrackballMode { get; set; }
        double TrackballFriction { get; set; }

        bool UseSAforMouse { get; set; }
        int[] SATriggers { get; set; }

        SATriggerCondType SATriggerCond { get; set; }
        string SATriggerCondStr { get; set; } // TODO: remove
        SASteeringWheelEmulationAxisType SASteeringWheelEmulationAxis { get; set; } 
        int SASteeringWheelEmulationRange { get; set; }

        ITrigger2Config L2 { get; }
        ITrigger2Config R2 { get; }
        IStickConfig LS { get; }
        IStickConfig RS { get; }
        IGyroConfig SX { get; }
        IGyroConfig SZ { get; }

        ISquareStickConfig SquStick { get; set; }

        OutContType OutputDevType { get; set; }
        bool DistanceProfiles { get; set; }

        ReadOnlyCollection<string> ProfileActions { get; set; }
        void SetProfileActions(List<string> actions);
        SpecialAction GetProfileAction(string name);
        int GetProfileActionIndexOf(string name);

        List<DS4ControlSettings> DS4CSettings { get; }
        void UpdateDS4CSetting(string buttonName, bool shift, object action, string exts, DS4KeyType kt, int trigger = 0);
        void UpdateDS4CExtra(string buttonName, bool shift, string exts);

        object GetDS4Action(string buttonName, bool shift);
        object GetDS4Action(DS4Controls control, bool shift);
        DS4KeyType GetDS4KeyType(string buttonName, bool shift);
        string GetDS4Extra(string buttonName, bool shift);
        int GetDS4STrigger(string buttonName);
        int GetDS4STrigger(DS4Controls control);
        DS4ControlSettings GetDS4CSetting(string control);
        DS4ControlSettings GetDS4CSetting(DS4Controls control);
        bool HasCustomActions { get; }
        bool HasCustomExtras { get; }
    }

    public interface ITrigger2Config
    {
        double Sensitivity { get; set; }
        int DeadZone { get; set; }
        int AntiDeadZone { get; set; }
        int MaxZone { get; set; }
        BezierPreset OutCurvePreset { get; set; }
        BezierCurve OutBezierCurve { get; }
    }

    public interface IStickConfig
    {
        double Sensitivity { get; set; }
        int DeadZone { get; set; }
        int AntiDeadZone { get; set; }
        int MaxZone { get; set; }
        double Rotation { get; set; }
        int Curve { get; set; }
        BezierPreset OutCurvePreset { get; set; }
        BezierCurve OutBezierCurve { get; }
    }

    public interface IGyroConfig
    {
        double Sensitivity { get; set; }
        double DeadZone { get; set; }
        double AntiDeadZone { get; set; }
        double MaxZone { get; set; }
        BezierPreset OutCurvePreset { get; set; }
        BezierCurve OutBezierCurve { get; }
    }

    public interface ISquareStickConfig
    {
        bool LSMode { get; set; }
        bool RSMode { get; set; }
        double Roundness { get; set; }
    }
}
