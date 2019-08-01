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

    public interface IGlobalConfig
    {
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

		List<SpecialAction> Actions { get; }
        SpecialAction ActionByName(string name);
        SpecialAction ActionByIndex(int index);
        int LookupActionIndex(string name);
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
        int ButtonMouseSensitivity { get; set; }
        byte RumbleBoost { get; set; }
        double Rainbow { get; set; } 
        bool FlushHIDQueue { get; set; }
        bool EnableTouchToggle { get; set; }
        int IdleDisconnectTimeout { get; set; }
        byte TouchSensitivity { get; set; }
        bool TouchActive { get; set; }
        byte FlashType { get; set; }
        int FlashAt { get; set; } 
        bool LedAsBatteryIndicator { get; set; }
        int ChargingType { get; set; }
        bool DinputOnly { get; set; }

#if false
        public Dictionary<string, string> linkedProfiles = new Dictionary<string, string>();

        public int[] gyroMouseDZ = new int[5] { MouseCursor.GYRO_MOUSE_DEADZONE, MouseCursor.GYRO_MOUSE_DEADZONE,
            MouseCursor.GYRO_MOUSE_DEADZONE, MouseCursor.GYRO_MOUSE_DEADZONE,
            MouseCursor.GYRO_MOUSE_DEADZONE };
        public bool[] gyroMouseToggle = new bool[5] { false, false, false,
            false, false };

        private void setOutBezierCurveObjArrayItem(BezierCurve[] bezierCurveArray, int device, int curveOptionValue, BezierCurve.AxisType axisType)

        public List<DS4ControlSettings>[] ds4settings = new List<DS4ControlSettings>[5]

        public int[] profileActionCount = new int[5] { 0, 0, 0, 0, 0 };
        public Dictionary<string, SpecialAction>[] profileActionDict = new Dictionary<string, SpecialAction>[5]

        public Dictionary<string, int>[] profileActionIndexDict = new Dictionary<string, int>[5]

        public string useLang = "";
        public bool downloadLang = true;
        public bool flashWhenLate = true;
        public int flashWhenLateAt = 20;
        public bool useUDPServ = false;
        public int udpServPort = 26760;
        public string udpServListenAddress = "127.0.0.1"; // 127.0.0.1=IPAddress.Loopback (default), 0.0.0.0=IPAddress.Any as all interfaces, x.x.x.x = Specific ipv4 interface address or hostname
        public bool useCustomSteamFolder;
        public string customSteamFolder;
        // Cache whether profile has custom action
        public bool[] containsCustomAction = new bool[5] { false, false, false, false, false };

        // Cache whether profile has custom extras
        public bool[] containsCustomExtras = new bool[5] { false, false, false, false, false };
#endif

        bool StartTouchpadOff { get; set; } 
        bool UseTPforControls { get; set; }
        
        bool UseSAforMouse { get; set; }

        string SATriggers { get; set; }
        bool SATriggerCond { get; set; }
        SASteeringWheelEmulationAxisType SASteeringWheelEmulationAxis { get; set; } 
        int SASteeringWheelEmulationRange { get; set; }

        int TouchDisInvertTriggers { get; set; }
        int GyroSensitivity { get; set; } 
        int GyroSensVerticalScale { get; set; } 
        int GyroInvert { get; set; } 
        bool GyroTriggerTurns { get; set; }
        bool GyroSmoothing { get; set; }
        double GyroSmoothingWeight { get; set; }
        int GyroMouseHorizontalAxis { get; set; } 
        int GyroMouseDeadZone { get; set; } 
        bool GyroMouseToggle { get; set; }

        DS4Color MainColor { get; set; } 
		DS4Color LowColor { get; set; } 
        DS4Color ChargingColor { get; set; }
        DS4Color CustomColor { get; set; }
		bool UseCustomColor { get; set; } 
        DS4Color FlashColor { get; set; }

        byte TapSensitivity { get; set; } 
        bool DoubleTap { get; set; } 
        int ScrollSensitivity { get; set; }

        bool LowerRCOn { get; set; }
        bool TouchpadJitterCompensation { get; set; }
        int TouchpadInvert { get; set; }

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

        bool MouseAccel { get; set; } 

        int BTPollRate { get; set; }
        
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

        bool TrackballMode { get; set; }
        double TrackballFriction { get; set; }

        OutContType OutContType { get; set; }
        string LaunchProgram { get; set; }
        string ProfilePath { get; set; }
        string OlderProfilePath { get; set; }
        bool DistanceProfiles { get; set; }

        List<string> ProfileActions { get; set; }
        SpecialAction ProfileActionByName(string name);
        SpecialAction ProfileActionByIndex(int index);
        int LookupProfileActionIndex(string name);

        void UpdateDS4CSetting(string buttonName, bool shift, object action, string exts, DS4KeyType kt, int trigger = 0);
        void UpdateDS4Extra(string buttonName, bool shift, string exts);

        object GetDS4Action(string buttonName, bool shift);
        object GetDS4Action(DS4Controls control, bool shift);
        DS4KeyType GetDS4KeyType(string buttonName, bool shift);
        string GetDS4Extra(string buttonName, bool shift);
        int GetDS4STrigger(string buttonName);
        int GetDS4STrigger(DS4Controls control);
        List<DS4ControlSettings> GetDS4CSettings();
        DS4ControlSettings getDS4CSetting(string control);
        DS4ControlSettings getDS4CSetting(DS4Controls control);
        bool HasCustomActions { get; set; }
        bool HasCustomExtras { get; set; }
        
        bool ContainsCustomAction { get; set; }
        bool ContainsCustomExtras { get; set; }
    }

}
