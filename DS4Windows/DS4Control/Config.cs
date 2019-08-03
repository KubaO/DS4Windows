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

namespace DS4Windows
{
    // These are the public configuration APIs that interface between modules.

    public class API
    {
        // That's our interface to the outside world
        protected static Global global = new Global();
        public static IGlobalConfig Config = global;
        public static IDeviceConfig Cfg(int index) => Config.Cfg(index);
        public static IDeviceAuxiliaryConfig Aux(int index) => Config.Aux(index);

        public static bool IsAdministrator { get => global.IsAdministrator; }

        public static bool IsViGEmBusInstalled() => DeviceDetection.IsViGEmBusInstalled();
        public string VigemBusVersion { get; } = DeviceDetection.ViGEmBusVersion();

        public static void FindConfigLocation() => global.FindConfigLocation();
        public static void SetCulture(string culture) => global.SetCulture(culture);
    }

    public interface IGlobalConfig
    {
        IDeviceConfig Cfg(int index);
        IDeviceAuxiliaryConfig Aux(int index);

        bool UseExclusiveMode { get; set; }
        DateTime LastChecked { get; set; }
        int CheckWhen { get; set; }
        int Notifications { get; set; }
        bool DisconnectBTatStop { get; set; }
        bool SwipeProfiles { get; set; }
        bool DS4Mapping { get; set; }
        bool QuickCharge { get; set; }
        bool CloseMini { get; set; }
        bool StartMinimized { get; set; }
        bool MinToTaskbar { get; set; }
        int FormWidth { get; set; }
        int FormHeight { get; set; }
		int FormLocationX { get; set; }
        int FormLocationY { get; set; }
        string UseLang { get; set; }
        bool DownloadLang { get; set; }
        bool FlashWhenLate { get; set; }
        int FlashWhenLateAt { get; set; }
        bool isUsingUDPServer { get; set; }
        int UDPServerPortNum { get; set; }
        string UDPServerListenAddress { get; set; }
        bool UseWhiteIcon { get; set; }
        bool UseCustomSteamFolder { get; set; }
        string CustomSteamFolder { get; set; }

		string ProfilePath { get; set; }
		string ActionsPath { get; set; }
		string LinkedProfilesPath { get; set; }
		string ControllerConfigsPath { get; set; }
		Dictionary<string, string> LinkedProfiles { get; set; }

		string ExePath { get; }
        bool ExePathNeedsAdmin { get; }
		string AppDataPath { get; }
		bool AppDataPathNeedsAdmin { get; }
		string AppDataPPath { get; set; }
		bool IsFirstRun { get; }
		bool MultiSaveSpots { get; set; }
		bool RunHotPLug { get; set; }

        bool VigemInstalled { get; }
		string VigemBusVersion { get; }

        List<SpecialAction> Actions { get; }
        SpecialAction ActionByName(string name);
        SpecialAction ActionByIndex(int index);
        int LookupActionIndex(string name);

        X360Controls[] DefaultButtonMapping { get;  }
        DS4Controls[] ReverseX360ButtonMapping { get; }
    }

    public interface IDeviceAuxiliaryConfig
    {
        string TempProfileName { get; set; }
        bool UseTempProfile { get; set; }
        bool TempProfileDistance { get; set; }
        bool UseDInputOnly { get; set; }
        bool LinkedProfileCheck { get; set; } // applies between this and successor profile
        bool TouchpadActive { get; set; }
    }

    public interface IDeviceConfig
    {
        int BTPollRate { get; set; }
        bool FlushHIDQueue { get; set; }
        int IdleDisconnectTimeout { get; set; }
        byte RumbleBoost { get; set; }
        bool LowerRCOn { get; set; }
        int ChargingType { get; set; }
        bool DInputOnly { get; set; }

        byte FlashType { get; set; }
        int FlashAt { get; set; }
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
        bool TouchActive { get; set; }
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
        int GyroMouseDeadZone { get; set; }
        bool GyroMouseToggle { get; set; }

        int ButtonMouseSensitivity { get; set; }
        bool MouseAccel { get; set; }

        bool TrackballMode { get; set; }
        double TrackballFriction { get; set; }

        bool UseSAforMouse { get; set; }
        string SATriggers { get; set; }
        bool SATriggerCond { get; set; }
        SASteeringWheelEmulationAxisType SASteeringWheelEmulationAxis { get; set; } 
        int SASteeringWheelEmulationRange { get; set; }

        TriggerDeadZoneZInfo L2ModInfo { get; set; }
        byte L2Deadzone { get; set; }        
        TriggerDeadZoneZInfo R2ModInfo { get; set; } 
        byte R2Deadzone { get; set; }

        double SXDeadzone { get; set; }
        double SZDeadzone { get; set; }

        int LSDeadzone { get; set; } 
        int RSDeadzone { get; set; }

        int LSAntiDeadzone { get; set; } 
        int RSAntiDeadzone { get; set; }

        StickDeadZoneInfo LSModInfo { get; set; } 
        StickDeadZoneInfo RSModInfo { get; set; }

        double SXAntiDeadzone { get; set; }
        double SZAntiDeadzone { get; set; }

        int LSMaxzone { get; set; }
        int RSMaxzone { get; set; }
        
        double SXMaxzone { get; set; } 
		double SZMaxzone { get; set; }

        int L2AntiDeadzone { get; set; }
        int R2AntiDeadzone { get; set; }
        
        int L2Maxzone { get; set; } 
        int R2Maxzone { get; set; }
        
        int LSCurve { get; set; }
        int RSCurve { get; set; }

        double LSRotation { get; set; } 
        double RSRotation { get; set; } 

        double L2Sens { get; set; } 
		double R2Sens { get; set; }

        double SXSens { get; set; } 
        double SZSens { get; set; } 

        double LSSens { get; set; }
        double RSSens { get; set; }

        SquareStickInfo SquStickInfo { get; set; }

        int LsOutCurveMode { get; set; }
        BezierCurve lsOutBezierCurveObj { get; set; } 
        int RsOutCurveMode { get; set; }
        BezierCurve[] rsOutBezierCurveObj { get; set; }

        int L2OutCurveMode { get; set; }
        BezierCurve l2OutBezierCurveObj { get; set; }
        int R2OutCurveMode { get; set; }
        BezierCurve[] r2OutBezierCurveObj { get; set; }

        int SXOutCurveMode { get; set; }
        BezierCurve SXOutBezierCurveObj { get; set; }
        int SZOutCurveMode { get; set; }
        BezierCurve[] SZOutBezierCurveObj { get; set; }

        void SetOutBezierCurveObjects(BezierCurve[] bezierCurves, int curveOptionValue, BezierCurve.AxisType axisType);

        OutContType OutContType { get; set; }
        string LaunchProgram { get; set; }
        string ProfilePath { get; set; }
        string OlderProfilePath { get; set; }
        bool DistanceProfiles { get; set; }

        List<string> ProfileActions { get; set; }
        SpecialAction ProfileActionByName(string name);
        SpecialAction ProfileActionByIndex(int index);
        int LookupProfileActionIndex(string name);

        List<DS4ControlSettings> DS4Settings { get; }
        void UpdateDS4CSetting(string buttonName, bool shift, object action, string exts, DS4KeyType kt, int trigger = 0);
        void UpdateDS4CExtra(string buttonName, bool shift, string exts);

        object GetDS4Action(string buttonName, bool shift);
        object GetDS4Action(DS4Controls control, bool shift);
        DS4KeyType GetDS4KeyType(string buttonName, bool shift);
        string GetDS4Extra(string buttonName, bool shift);
        int GetDS4STrigger(string buttonName);
        int GetDS4STrigger(DS4Controls control);
        List<DS4ControlSettings> GetDS4CSettings();
        DS4ControlSettings getDS4CSetting(string control);
        DS4ControlSettings getDS4CSetting(DS4Controls control);
        bool HasCustomActions { get; }
        bool HasCustomExtras { get; }
    }

}
