using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Linq;

using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using static DS4Windows.Global;
using System.Drawing; // Point struct

namespace DS4Windows
{
    public class Mapping
    {
        private readonly int devIndex;
        private readonly IDeviceConfig cfg;
        private readonly IDeviceAuxiliaryConfig aux;
        private readonly DS4LightBar lightBar;
        private readonly DeviceControlService ctrl;

        internal Mapping(int devIndex)
        {
            this.devIndex = devIndex;
            cfg = API.Cfg(devIndex);
            aux = API.Aux(devIndex);
            if (devIndex < 4) lightBar = API.Bar(devIndex);
            ctrl = Program.RootHub(devIndex);
        }

        /*
         * Represent the synthetic keyboard and mouse events.  Maintain counts for each so we don't duplicate events.
         */
        public class SyntheticState
        {
            public struct MouseClick
            {
                public int leftCount, middleCount, rightCount, fourthCount, fifthCount, wUpCount, wDownCount, toggleCount;
                public bool toggle;
            }
            public MouseClick previousClicks, currentClicks;
            public struct KeyPress
            {
                public int vkCount, scanCodeCount, repeatCount, toggleCount; // repeat takes priority over non-, and scancode takes priority over non-
                public bool toggle;
            }
            public class KeyPresses
            {
                public KeyPress previous, current;
            }
            public Dictionary<UInt16, KeyPresses> keyPresses = new Dictionary<UInt16, KeyPresses>();

            public void SaveToPrevious(bool performClear)
            {
                previousClicks = currentClicks;
                if (performClear)
                    currentClicks.leftCount = currentClicks.middleCount = currentClicks.rightCount = currentClicks.fourthCount = currentClicks.fifthCount = currentClicks.wUpCount = currentClicks.wDownCount = currentClicks.toggleCount = 0;

                foreach (KeyPresses kp in keyPresses.Values)
                {
                    kp.previous = kp.current;
                    if (performClear)
                    {
                        kp.current.repeatCount = kp.current.scanCodeCount = kp.current.vkCount = kp.current.toggleCount = 0;
                        //kp.current.toggle = false;
                    }
                }
            }
        }

        struct ControlToXInput
        {
            public DS4Controls ds4input;
            public DS4Controls xoutput;

            public ControlToXInput(DS4Controls input, DS4Controls output)
            {
                ds4input = input; xoutput = output;
            }
        }

        private Queue<ControlToXInput> customMapQueue = new Queue<ControlToXInput>();

        struct DS4Vector2
        {
            public double x;
            public double y;

            public DS4Vector2(double x, double y)
            {
                this.x = x;
                this.y = y;
            }
        }

        class DS4SquareStick
        {
            public DS4Vector2 current;
            public DS4Vector2 squared;

            public DS4SquareStick()
            {
                current = new DS4Vector2(0.0, 0.0);
                squared = new DS4Vector2(0.0, 0.0);
            }

            public void CircleToSquare(double roundness)
            {
                const double PiOverFour = Math.PI / 4.0;

                // Determine the theta angle
                double angle = Math.Atan2(current.y, -current.x);
                angle += Math.PI;
                double cosAng = Math.Cos(angle);
                // Scale according to which wall we're clamping to
                // X+ wall
                if (angle <= PiOverFour || angle > 7.0 * PiOverFour)
                {
                    double tempVal = 1.0 / cosAng;
                    //Console.WriteLine("1 ANG: {0} | TEMP: {1}", angle, tempVal);
                    squared.x = current.x * tempVal;
                    squared.y = current.y * tempVal;
                }
                // Y+ wall
                else if (angle > PiOverFour && angle <= 3.0 * PiOverFour)
                {
                    double tempVal = 1.0 / Math.Sin(angle);
                    //Console.WriteLine("2 ANG: {0} | TEMP: {1}", angle, tempVal);
                    squared.x = current.x * tempVal;
                    squared.y = current.y * tempVal;
                }
                // X- wall
                else if (angle > 3.0 * PiOverFour && angle <= 5.0 * PiOverFour)
                {
                    double tempVal = -1.0 / cosAng;
                    //Console.WriteLine("3 ANG: {0} | TEMP: {1}", angle, tempVal);
                    squared.x = current.x * tempVal;
                    squared.y = current.y * tempVal;
                }
                // Y- wall
                else if (angle > 5.0 * PiOverFour && angle <= 7.0 * PiOverFour)
                {
                    double tempVal = -1.0 / Math.Sin(angle);
                    //Console.WriteLine("4 ANG: {0} | TEMP: {1}", angle, tempVal);
                    squared.x = current.x * tempVal;
                    squared.y = current.y * tempVal;
                }
                else return;

                //double lengthOld = Math.Sqrt((x * x) + (y * y));
                double length = current.x / cosAng;
                //Console.WriteLine("LENGTH TEST ({0}) ({1}) {2}", lengthOld, length, (lengthOld == length).ToString());
                double factor = Math.Pow(length, roundness);
                //double ogX = current.x, ogY = current.y;
                current.x += (squared.x - current.x) * factor;
                current.y += (squared.y - current.y) * factor;
                //Console.WriteLine("INPUT: {0} {1} | {2} {3} | {4} {5} | {6} {7}",
                //    ogX, ogY, current.x, current.y, squared.x, squared.y, length, factor);
            }
        }

        private DS4SquareStick outSqrStk = new DS4SquareStick();
        public SyntheticState deviceState = new SyntheticState();
        public DS4StateFieldMapping fieldMapping = new DS4StateFieldMapping();
        public DS4StateFieldMapping outputFieldMapping = new DS4StateFieldMapping();
        public DS4StateFieldMapping previousFieldMapping = new DS4StateFieldMapping();

        static ReaderWriterLockSlim syncStateLock = new ReaderWriterLockSlim();

        public static SyntheticState globalState = new SyntheticState();

        // TODO When we disconnect, process a null/dead state to release any keys or buttons.
        public static DateTime oldnow = DateTime.UtcNow;
        private static bool pressagain = false;
        private static int wheel = 0, keyshelddown = 0;

        //mapcustom
        public static bool[] pressedonce = new bool[261], macrodone = new bool[38];
        static bool[] macroControl = new bool[25];
        static uint macroCount = 0;

        //actions
        public int fadetimer = 0;
        public int prevFadetimer = 0;
        public DS4Color lastColor;
        public bool[] actionDone = new bool[0];
        public SpecialAction untriggeraction = new SpecialAction();
        public DateTime nowAction = DateTime.MinValue;
        public DateTime oldnowAction = DateTime.MinValue;
        public int untriggerindex = -1;
        public DateTime oldnowKeyAct = DateTime.MinValue;

        private static DS4Controls[] shiftTriggerMapping = new DS4Controls[26] { DS4Controls.None, DS4Controls.Cross, DS4Controls.Circle, DS4Controls.Square,
            DS4Controls.Triangle, DS4Controls.Options, DS4Controls.Share, DS4Controls.DpadUp, DS4Controls.DpadDown,
            DS4Controls.DpadLeft, DS4Controls.DpadRight, DS4Controls.PS, DS4Controls.L1, DS4Controls.R1, DS4Controls.L2,
            DS4Controls.R2, DS4Controls.L3, DS4Controls.R3, DS4Controls.TouchLeft, DS4Controls.TouchUpper, DS4Controls.TouchMulti,
            DS4Controls.TouchRight, DS4Controls.GyroZNeg, DS4Controls.GyroZPos, DS4Controls.GyroXPos, DS4Controls.GyroXNeg,
        };

        private static int[] ds4ControlMapping = new int[38] { 0, // DS4Control.None
            16, // DS4Controls.LXNeg
            20, // DS4Controls.LXPos
            17, // DS4Controls.LYNeg
            21, // DS4Controls.LYPos
            18, // DS4Controls.RXNeg
            22, // DS4Controls.RXPos
            19, // DS4Controls.RYNeg
            23, // DS4Controls.RYPos
            3,  // DS4Controls.L1
            24, // DS4Controls.L2
            5,  // DS4Controls.L3
            4,  // DS4Controls.R1
            25, // DS4Controls.R2
            6,  // DS4Controls.R3
            13, // DS4Controls.Square
            14, // DS4Controls.Triangle
            15, // DS4Controls.Circle
            12, // DS4Controls.Cross
            7,  // DS4Controls.DpadUp
            10, // DS4Controls.DpadRight
            8,  // DS4Controls.DpadDown
            9,  // DS4Controls.DpadLeft
            11, // DS4Controls.PS
            27, // DS4Controls.TouchLeft
            29, // DS4Controls.TouchUpper
            26, // DS4Controls.TouchMulti
            28, // DS4Controls.TouchRight
            1,  // DS4Controls.Share
            2,  // DS4Controls.Options
            31, // DS4Controls.GyroXPos
            30, // DS4Controls.GyroXNeg
            33, // DS4Controls.GyroZPos
            32, // DS4Controls.GyroZNeg
            34, // DS4Controls.SwipeLeft
            35, // DS4Controls.SwipeRight
            36, // DS4Controls.SwipeUp
            37  // DS4Controls.SwipeDown
        };

        // Special macros
        static bool altTabDone = true;
        static DateTime altTabNow = DateTime.UtcNow,
            oldAltTabNow = DateTime.UtcNow - TimeSpan.FromSeconds(1);

        // Mouse
        public static int mcounter = 34;
        public static int mouseaccel = 0;
        public static int prevmouseaccel = 0;
        private static double horizontalRemainder = 0.0, verticalRemainder = 0.0;
        private const int MOUSESPEEDFACTOR = 48;
        private const double MOUSESTICKOFFSET = 0.0495;

        public void Commit()
        {
            SyntheticState state = deviceState;
            syncStateLock.EnterWriteLock();

            globalState.currentClicks.leftCount += state.currentClicks.leftCount - state.previousClicks.leftCount;
            globalState.currentClicks.middleCount += state.currentClicks.middleCount - state.previousClicks.middleCount;
            globalState.currentClicks.rightCount += state.currentClicks.rightCount - state.previousClicks.rightCount;
            globalState.currentClicks.fourthCount += state.currentClicks.fourthCount - state.previousClicks.fourthCount;
            globalState.currentClicks.fifthCount += state.currentClicks.fifthCount - state.previousClicks.fifthCount;
            globalState.currentClicks.wUpCount += state.currentClicks.wUpCount - state.previousClicks.wUpCount;
            globalState.currentClicks.wDownCount += state.currentClicks.wDownCount - state.previousClicks.wDownCount;
            globalState.currentClicks.toggleCount += state.currentClicks.toggleCount - state.previousClicks.toggleCount;
            globalState.currentClicks.toggle = state.currentClicks.toggle;

            if (globalState.currentClicks.toggleCount != 0 && globalState.previousClicks.toggleCount == 0 && globalState.currentClicks.toggle)
            {
                if (globalState.currentClicks.leftCount != 0 && globalState.previousClicks.leftCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTDOWN);
                if (globalState.currentClicks.rightCount != 0 && globalState.previousClicks.rightCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTDOWN);
                if (globalState.currentClicks.middleCount != 0 && globalState.previousClicks.middleCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEDOWN);
                if (globalState.currentClicks.fourthCount != 0 && globalState.previousClicks.fourthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 1);
                if (globalState.currentClicks.fifthCount != 0 && globalState.previousClicks.fifthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 2);
            }
            else if (globalState.currentClicks.toggleCount != 0 && globalState.previousClicks.toggleCount == 0 && !globalState.currentClicks.toggle)
            {
                if (globalState.currentClicks.leftCount != 0 && globalState.previousClicks.leftCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTUP);
                if (globalState.currentClicks.rightCount != 0 && globalState.previousClicks.rightCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTUP);
                if (globalState.currentClicks.middleCount != 0 && globalState.previousClicks.middleCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEUP);
                if (globalState.currentClicks.fourthCount != 0 && globalState.previousClicks.fourthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 1);
                if (globalState.currentClicks.fifthCount != 0 && globalState.previousClicks.fifthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 2);
            }

            if (globalState.currentClicks.toggleCount == 0 && globalState.previousClicks.toggleCount == 0)
            {
                if (globalState.currentClicks.leftCount != 0 && globalState.previousClicks.leftCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTDOWN);
                else if (globalState.currentClicks.leftCount == 0 && globalState.previousClicks.leftCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTUP);

                if (globalState.currentClicks.middleCount != 0 && globalState.previousClicks.middleCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEDOWN);
                else if (globalState.currentClicks.middleCount == 0 && globalState.previousClicks.middleCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEUP);

                if (globalState.currentClicks.rightCount != 0 && globalState.previousClicks.rightCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTDOWN);
                else if (globalState.currentClicks.rightCount == 0 && globalState.previousClicks.rightCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTUP);

                if (globalState.currentClicks.fourthCount != 0 && globalState.previousClicks.fourthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 1);
                else if (globalState.currentClicks.fourthCount == 0 && globalState.previousClicks.fourthCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 1);

                if (globalState.currentClicks.fifthCount != 0 && globalState.previousClicks.fifthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 2);
                else if (globalState.currentClicks.fifthCount == 0 && globalState.previousClicks.fifthCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 2);

                if (globalState.currentClicks.wUpCount != 0 && globalState.previousClicks.wUpCount == 0)
                {
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_WHEEL, 120);
                    oldnow = DateTime.UtcNow;
                    wheel = 120;
                }
                else if (globalState.currentClicks.wUpCount == 0 && globalState.previousClicks.wUpCount != 0)
                    wheel = 0;

                if (globalState.currentClicks.wDownCount != 0 && globalState.previousClicks.wDownCount == 0)
                {
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_WHEEL, -120);
                    oldnow = DateTime.UtcNow;
                    wheel = -120;
                }
                if (globalState.currentClicks.wDownCount == 0 && globalState.previousClicks.wDownCount != 0)
                    wheel = 0;
            }
            

            if (wheel != 0) //Continue mouse wheel movement
            {
                DateTime now = DateTime.UtcNow;
                if (now >= oldnow + TimeSpan.FromMilliseconds(100) && !pressagain)
                {
                    oldnow = now;
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_WHEEL, wheel);
                }
            }

            // Merge and synthesize all key presses/releases that are present in this device's mapping.
            // TODO what about the rest?  e.g. repeat keys really ought to be on some set schedule
            Dictionary<UInt16, SyntheticState.KeyPresses>.KeyCollection kvpKeys = state.keyPresses.Keys;
            foreach (KeyValuePair<UInt16, SyntheticState.KeyPresses> kvp in state.keyPresses)
            {
                UInt16 kvpKey = kvp.Key;
                SyntheticState.KeyPresses kvpValue = kvp.Value;

                SyntheticState.KeyPresses gkp;
                if (globalState.keyPresses.TryGetValue(kvpKey, out gkp))
                {
                    gkp.current.vkCount += kvpValue.current.vkCount - kvpValue.previous.vkCount;
                    gkp.current.scanCodeCount += kvpValue.current.scanCodeCount - kvpValue.previous.scanCodeCount;
                    gkp.current.repeatCount += kvpValue.current.repeatCount - kvpValue.previous.repeatCount;
                    gkp.current.toggle = kvpValue.current.toggle;
                    gkp.current.toggleCount += kvpValue.current.toggleCount - kvpValue.previous.toggleCount;
                }
                else
                {
                    gkp = new SyntheticState.KeyPresses();
                    gkp.current = kvpValue.current;
                    globalState.keyPresses[kvpKey] = gkp;
                }
                if (gkp.current.toggleCount != 0 && gkp.previous.toggleCount == 0 && gkp.current.toggle)
                {
                    if (gkp.current.scanCodeCount != 0)
                        InputMethods.performSCKeyPress(kvpKey);
                    else
                        InputMethods.performKeyPress(kvpKey);
                }
                else if (gkp.current.toggleCount != 0 && gkp.previous.toggleCount == 0 && !gkp.current.toggle)
                {
                    if (gkp.previous.scanCodeCount != 0) // use the last type of VK/SC
                        InputMethods.performSCKeyRelease(kvpKey);
                    else
                        InputMethods.performKeyRelease(kvpKey);
                }
                else if (gkp.current.vkCount + gkp.current.scanCodeCount != 0 && gkp.previous.vkCount + gkp.previous.scanCodeCount == 0)
                {
                    if (gkp.current.scanCodeCount != 0)
                    {
                        oldnow = DateTime.UtcNow;
                        InputMethods.performSCKeyPress(kvpKey);
                        pressagain = false;
                        keyshelddown = kvpKey;
                    }
                    else
                    {
                        oldnow = DateTime.UtcNow;
                        InputMethods.performKeyPress(kvpKey);
                        pressagain = false;
                        keyshelddown = kvpKey;
                    }
                }
                else if (gkp.current.toggleCount != 0 || gkp.previous.toggleCount != 0 || gkp.current.repeatCount != 0 || // repeat or SC/VK transition
                    ((gkp.previous.scanCodeCount == 0) != (gkp.current.scanCodeCount == 0))) //repeat keystroke after 500ms
                {
                    if (keyshelddown == kvpKey)
                    {
                        DateTime now = DateTime.UtcNow;
                        if (now >= oldnow + TimeSpan.FromMilliseconds(500) && !pressagain)
                        {
                            oldnow = now;
                            pressagain = true;
                        }
                        if (pressagain && gkp.current.scanCodeCount != 0)
                        {
                            now = DateTime.UtcNow;
                            if (now >= oldnow + TimeSpan.FromMilliseconds(25) && pressagain)
                            {
                                oldnow = now;
                                InputMethods.performSCKeyPress(kvpKey);
                            }
                        }
                        else if (pressagain)
                        {
                            now = DateTime.UtcNow;
                            if (now >= oldnow + TimeSpan.FromMilliseconds(25) && pressagain)
                            {
                                oldnow = now;
                                InputMethods.performKeyPress(kvpKey);
                            }
                        }
                    }
                }
                if ((gkp.current.toggleCount == 0 && gkp.previous.toggleCount == 0) && gkp.current.vkCount + gkp.current.scanCodeCount == 0 && gkp.previous.vkCount + gkp.previous.scanCodeCount != 0)
                {
                    if (gkp.previous.scanCodeCount != 0) // use the last type of VK/SC
                    {
                        InputMethods.performSCKeyRelease(kvpKey);
                        pressagain = false;
                    }
                    else
                    {
                        InputMethods.performKeyRelease(kvpKey);
                        pressagain = false;
                    }
                }
            }
            globalState.SaveToPrevious(false);

            syncStateLock.ExitWriteLock();
            state.SaveToPrevious(true);
        }

        public enum Click { None, Left, Middle, Right, Fourth, Fifth, WUP, WDOWN };
        public void MapClick(Click mouseClick)
        {
            switch (mouseClick)
            {
                case Click.Left:
                    deviceState.currentClicks.leftCount++;
                    break;
                case Click.Middle:
                    deviceState.currentClicks.middleCount++;
                    break;
                case Click.Right:
                    deviceState.currentClicks.rightCount++;
                    break;
                case Click.Fourth:
                    deviceState.currentClicks.fourthCount++;
                    break;
                case Click.Fifth:
                    deviceState.currentClicks.fifthCount++;
                    break;
                case Click.WUP:
                    deviceState.currentClicks.wUpCount++;
                    break;
                case Click.WDOWN:
                    deviceState.currentClicks.wDownCount++;
                    break;
                default: break;
            }
        }

        public static int DS4ControltoInt(DS4Controls ctrl)
        {
            int result = 0;
            if (ctrl >= DS4Controls.None && ctrl <= DS4Controls.SwipeDown)
            {
                result = ds4ControlMapping[(int)ctrl];
            }

            return result;
        }

        static double TValue(double value1, double value2, double percent)
        {
            percent /= 100f;
            return value1 * percent + value2 * (1 - percent);
        }

        public DS4State SetCurveAndDeadzone(DS4State cState, DS4State dState)
        {
            double rotation = /*tempDoubleArray[device] =*/  cfg.LS.Rotation;
            if (rotation > 0.0 || rotation < 0.0)
                cState.rotateLSCoordinates(rotation);

            double rotationRS = /*tempDoubleArray[device] =*/ cfg.RS.Rotation;
            if (rotationRS > 0.0 || rotationRS < 0.0)
                cState.rotateRSCoordinates(rotationRS);

            cState.CopyTo(dState);
            //DS4State dState = new DS4State(cState);
            int x;
            int y;
            int curve;

            /* TODO: Look into curve options and make sure maximum axes values are being respected */
            int lsCurve = cfg.LS.Curve;
            if (lsCurve > 0)
            {
                x = cState.LX;
                y = cState.LY;
                float max = x + y;
                double curvex;
                double curvey;
                curve = lsCurve;
                double multimax = TValue(382.5, max, curve);
                double multimin = TValue(127.5, max, curve);
                if ((x > 127.5f && y > 127.5f) || (x < 127.5f && y < 127.5f))
                {
                    curvex = (x > 127.5f ? Math.Min(x, (x / max) * multimax) : Math.Max(x, (x / max) * multimin));
                    curvey = (y > 127.5f ? Math.Min(y, (y / max) * multimax) : Math.Max(y, (y / max) * multimin));
                }
                else
                {
                    if (x < 127.5f)
                    {
                        curvex = Math.Min(x, (x / max) * multimax);
                        curvey = Math.Min(y, (-(y / max) * multimax + 510));
                    }
                    else
                    {
                        curvex = Math.Min(x, (-(x / max) * multimax + 510));
                        curvey = Math.Min(y, (y / max) * multimax);
                    }
                }

                dState.LX = (byte)Math.Round(curvex, 0);
                dState.LY = (byte)Math.Round(curvey, 0);
            }

            /* TODO: Look into curve options and make sure maximum axes values are being respected */
            int rsCurve = cfg.RS.Curve;
            if (rsCurve > 0)
            {
                x = cState.RX;
                y = cState.RY;
                float max = x + y;
                double curvex;
                double curvey;
                curve = rsCurve;
                double multimax = TValue(382.5, max, curve);
                double multimin = TValue(127.5, max, curve);
                if ((x > 127.5f && y > 127.5f) || (x < 127.5f && y < 127.5f))
                {
                    curvex = (x > 127.5f ? Math.Min(x, (x / max) * multimax) : Math.Max(x, (x / max) * multimin));
                    curvey = (y > 127.5f ? Math.Min(y, (y / max) * multimax) : Math.Max(y, (y / max) * multimin));
                }
                else
                {
                    if (x < 127.5f)
                    {
                        curvex = Math.Min(x, (x / max) * multimax);
                        curvey = Math.Min(y, (-(y / max) * multimax + 510));
                    }
                    else
                    {
                        curvex = Math.Min(x, (-(x / max) * multimax + 510));
                        curvey = Math.Min(y, (y / max) * multimax);
                    }
                }

                dState.RX = (byte)Math.Round(curvex, 0);
                dState.RY = (byte)Math.Round(curvey, 0);
            }

            /*int lsDeadzone = getLSDeadzone(device);
            int lsAntiDead = getLSAntiDeadzone(device);
            int lsMaxZone = getLSMaxzone(device);
            */
            IStickConfig ls = cfg.LS;
            int lsDeadzone = ls.DeadZone;
            int lsAntiDead = ls.AntiDeadZone;
            int lsMaxZone = ls.MaxZone;

            if (lsDeadzone > 0 || lsAntiDead > 0 || lsMaxZone != 100)
            {
                double lsSquared = Math.Pow(cState.LX - 128f, 2) + Math.Pow(cState.LY - 128f, 2);
                double lsDeadzoneSquared = Math.Pow(lsDeadzone, 2);
                if (lsDeadzone > 0 && lsSquared <= lsDeadzoneSquared)
                {
                    dState.LX = 128;
                    dState.LY = 128;
                }
                else if ((lsDeadzone > 0 && lsSquared > lsDeadzoneSquared) || lsAntiDead > 0 || lsMaxZone != 100)
                {
                    double r = Math.Atan2(-(dState.LY - 128.0), (dState.LX - 128.0));
                    double maxXValue = dState.LX >= 128.0 ? 127.0 : -128;
                    double maxYValue = dState.LY >= 128.0 ? 127.0 : -128;
                    double ratio = lsMaxZone / 100.0;

                    double maxZoneXNegValue = (ratio * -128) + 128;
                    double maxZoneXPosValue = (ratio * 127) + 128;
                    double maxZoneYNegValue = maxZoneXNegValue;
                    double maxZoneYPosValue = maxZoneXPosValue;
                    double maxZoneX = dState.LX >= 128.0 ? (maxZoneXPosValue - 128.0) : (maxZoneXNegValue - 128.0);
                    double maxZoneY = dState.LY >= 128.0 ? (maxZoneYPosValue - 128.0) : (maxZoneYNegValue - 128.0);

                    double tempLsXDead = 0.0, tempLsYDead = 0.0;
                    double tempOutputX = 0.0, tempOutputY = 0.0;
                    if (lsDeadzone > 0)
                    {
                        tempLsXDead = Math.Abs(Math.Cos(r)) * (lsDeadzone / 127.0) * maxXValue;
                        tempLsYDead = Math.Abs(Math.Sin(r)) * (lsDeadzone / 127.0) * maxYValue;

                        if (lsSquared > lsDeadzoneSquared)
                        {
                            double currentX = Util.Clamp(maxZoneXNegValue, dState.LX, maxZoneXPosValue);
                            double currentY = Util.Clamp(maxZoneYNegValue, dState.LY, maxZoneYPosValue);
                            tempOutputX = ((currentX - 128.0 - tempLsXDead) / (maxZoneX - tempLsXDead));
                            tempOutputY = ((currentY - 128.0 - tempLsYDead) / (maxZoneY - tempLsYDead));
                        }
                    }
                    else
                    {
                        double currentX = Util.Clamp(maxZoneXNegValue, dState.LX, maxZoneXPosValue);
                        double currentY = Util.Clamp(maxZoneYNegValue, dState.LY, maxZoneYPosValue);
                        tempOutputX = (currentX - 128.0) / maxZoneX;
                        tempOutputY = (currentY - 128.0) / maxZoneY;
                    }

                    double tempLsXAntiDeadPercent = 0.0, tempLsYAntiDeadPercent = 0.0;
                    if (lsAntiDead > 0)
                    {
                        tempLsXAntiDeadPercent = (lsAntiDead * 0.01) * Math.Abs(Math.Cos(r));
                        tempLsYAntiDeadPercent = (lsAntiDead * 0.01) * Math.Abs(Math.Sin(r));
                    }

                    if (tempOutputX > 0.0)
                    {
                        dState.LX = (byte)((((1.0 - tempLsXAntiDeadPercent) * tempOutputX + tempLsXAntiDeadPercent)) * maxXValue + 128.0);
                    }
                    else
                    {
                        dState.LX = 128;
                    }

                    if (tempOutputY > 0.0)
                    {
                        dState.LY = (byte)((((1.0 - tempLsYAntiDeadPercent) * tempOutputY + tempLsYAntiDeadPercent)) * maxYValue + 128.0);
                    }
                    else
                    {
                        dState.LY = 128;
                    }
                }
            }

            IStickConfig rs = cfg.RS;
            int rsDeadzone = rs.DeadZone;
            int rsAntiDead = rs.AntiDeadZone;
            int rsMaxZone = rs.MaxZone;
            if (rsDeadzone > 0 || rsAntiDead > 0 || rsMaxZone != 100)
            {
                double rsSquared = Math.Pow(cState.RX - 128.0, 2) + Math.Pow(cState.RY - 128.0, 2);
                double rsDeadzoneSquared = Math.Pow(rsDeadzone, 2);
                if (rsDeadzone > 0 && rsSquared <= rsDeadzoneSquared)
                {
                    dState.RX = 128;
                    dState.RY = 128;
                }
                else if ((rsDeadzone > 0 && rsSquared > rsDeadzoneSquared) || rsAntiDead > 0 || rsMaxZone != 100)
                {
                    double r = Math.Atan2(-(dState.RY - 128.0), (dState.RX - 128.0));
                    double maxXValue = dState.RX >= 128.0 ? 127 : -128;
                    double maxYValue = dState.RY >= 128.0 ? 127 : -128;
                    double ratio = rsMaxZone / 100.0;

                    double maxZoneXNegValue = (ratio * -128.0) + 128.0;
                    double maxZoneXPosValue = (ratio * 127.0) + 128.0;
                    double maxZoneYNegValue = maxZoneXNegValue;
                    double maxZoneYPosValue = maxZoneXPosValue;
                    double maxZoneX = dState.RX >= 128.0 ? (maxZoneXPosValue - 128.0) : (maxZoneXNegValue - 128.0);
                    double maxZoneY = dState.RY >= 128.0 ? (maxZoneYPosValue - 128.0) : (maxZoneYNegValue - 128.0);

                    double tempRsXDead = 0.0, tempRsYDead = 0.0;
                    double tempOutputX = 0.0, tempOutputY = 0.0;
                    if (rsDeadzone > 0)
                    {
                        tempRsXDead = Math.Abs(Math.Cos(r)) * (rsDeadzone / 127.0) * maxXValue;
                        tempRsYDead = Math.Abs(Math.Sin(r)) * (rsDeadzone / 127.0) * maxYValue;

                        if (rsSquared > rsDeadzoneSquared)
                        {
                            double currentX = Util.Clamp(maxZoneXNegValue, dState.RX, maxZoneXPosValue);
                            double currentY = Util.Clamp(maxZoneYNegValue, dState.RY, maxZoneYPosValue);

                            tempOutputX = ((currentX - 128.0 - tempRsXDead) / (maxZoneX - tempRsXDead));
                            tempOutputY = ((currentY - 128.0 - tempRsYDead) / (maxZoneY - tempRsYDead));
                        }
                    }
                    else
                    {
                        double currentX = Util.Clamp(maxZoneXNegValue, dState.RX, maxZoneXPosValue);
                        double currentY = Util.Clamp(maxZoneYNegValue, dState.RY, maxZoneYPosValue);

                        tempOutputX = (currentX - 128.0) / maxZoneX;
                        tempOutputY = (currentY - 128.0) / maxZoneY;
                    }

                    double tempRsXAntiDeadPercent = 0.0, tempRsYAntiDeadPercent = 0.0;
                    if (rsAntiDead > 0)
                    {
                        tempRsXAntiDeadPercent = (rsAntiDead * 0.01) * Math.Abs(Math.Cos(r));
                        tempRsYAntiDeadPercent = (rsAntiDead * 0.01) * Math.Abs(Math.Sin(r));
                    }

                    if (tempOutputX > 0.0)
                    {
                        dState.RX = (byte)((((1.0 - tempRsXAntiDeadPercent) * tempOutputX + tempRsXAntiDeadPercent)) * maxXValue + 128.0);
                    }
                    else
                    {
                        dState.RX = 128;
                    }

                    if (tempOutputY > 0.0)
                    {
                        dState.RY = (byte)((((1.0 - tempRsYAntiDeadPercent) * tempOutputY + tempRsYAntiDeadPercent)) * maxYValue + 128.0);
                    }
                    else
                    {
                        dState.RY = 128;
                    }
                }
            }

            void mapL2R2(ITrigger2Config l2r2, byte cState_L2R2, ref byte dState_L2R2)
            {
                int deadzone = l2r2.DeadZone;
                int antiDeadzone = l2r2.AntiDeadZone;
                int maxzone = l2r2.MaxZone;
                if (deadzone > 0 || antiDeadzone > 0 || maxzone != 100) {
                    double tempOutput = cState_L2R2 / 255.0;
                    double tempAntiDead = 0.0;
                    double ratio = maxzone / 100.0;
                    double maxValue = 255.0 * ratio;

                    if (deadzone > 0) {
                        if (cState_L2R2 > deadzone) {
                            double current = Util.Clamp(0, dState_L2R2, maxValue);
                            tempOutput = (current - deadzone) / (maxValue - deadzone);
                        }
                        else {
                            tempOutput = 0.0;
                        }
                    }

                    if (antiDeadzone > 0) {
                        tempAntiDead = antiDeadzone * 0.01;
                    }

                    if (tempOutput > 0.0) {
                        dState_L2R2 = (byte) (((1.0 - tempAntiDead) * tempOutput + tempAntiDead) * 255.0);
                    }
                    else {
                        dState_L2R2 = 0;
                    }
                }
            }
            mapL2R2(cfg.L2, cState.L2, ref dState.L2);
            mapL2R2(cfg.R2, cState.R2, ref dState.R2);

            void mapLSRSSensitivity(double sens, ref byte dState_LXRX, ref byte dState_LYRY)
            {
                if (sens != 1.0)
                {
                    dState_LXRX = (byte)Util.Clamp(0, sens * (dState_LXRX - 128.0) + 128.0, 255);
                    dState_LYRY = (byte)Util.Clamp(0, sens * (dState_LYRY - 128.0) + 128.0, 255);
                }
            }
            mapLSRSSensitivity(cfg.LS.Sensitivity, ref dState.LX, ref dState.LY);
            mapLSRSSensitivity(cfg.RS.Sensitivity, ref dState.RX, ref dState.RY);

            double l2Sens = cfg.L2.Sensitivity;
            if (l2Sens != 1.0)
                dState.L2 = (byte)Util.Clamp(0, l2Sens * dState.L2, 255);

            double r2Sens = cfg.R2.Sensitivity;
            if (r2Sens != 1.0)
                dState.R2 = (byte)Util.Clamp(0, r2Sens * dState.R2, 255);

            ISquareStickConfig squStk = cfg.SquStick;
            if (squStk.LSMode && (dState.LX != 128 || dState.LY != 128))
            {
                double capX = dState.LX >= 128 ? 127.0 : 128.0;
                double capY = dState.LY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.LX - 128.0) / capX;
                double tempY = (dState.LY - 128.0) / capY;
                DS4SquareStick sqstick = outSqrStk;
                sqstick.current.x = tempX; sqstick.current.y = tempY;
                sqstick.CircleToSquare(squStk.Roundness);
                //Console.WriteLine("Input ({0}) | Output ({1})", tempY, sqstick.current.y);
                tempX = sqstick.current.x < -1.0 ? -1.0 : sqstick.current.x > 1.0
                    ? 1.0 : sqstick.current.x;
                tempY = sqstick.current.y < -1.0 ? -1.0 : sqstick.current.y > 1.0
                    ? 1.0 : sqstick.current.y;
                dState.LX = (byte)(tempX * capX + 128.0);
                dState.LY = (byte)(tempY * capY + 128.0);
            }

            int lsOutCurveMode = (int)ls.OutCurvePreset;
            if (lsOutCurveMode > 0 && (dState.LX != 128 || dState.LY != 128))
            {
                double capX = dState.LX >= 128 ? 127.0 : 128.0;
                double capY = dState.LY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.LX - 128.0) / capX;
                double tempY = (dState.LY - 128.0) / capY;
                double signX = tempX >= 0.0 ? 1.0 : -1.0;
                double signY = tempY >= 0.0 ? 1.0 : -1.0;

                if (lsOutCurveMode == 1)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = 0.0;
                    double outputY = 0.0;

                    if (absX <= 0.4)
                    {
                        outputX = 0.55 * absX;
                    }
                    else if (absX <= 0.75)
                    {
                        outputX = absX - 0.18;
                    }
                    else if (absX > 0.75)
                    {
                        outputX = (absX * 1.72) - 0.72;
                    }

                    if (absY <= 0.4)
                    {
                        outputY = 0.55 * absY;
                    }
                    else if (absY <= 0.75)
                    {
                        outputY = absY - 0.18;
                    }
                    else if (absY > 0.75)
                    {
                        outputY = (absY * 1.72) - 0.72;
                    }

                    dState.LX = (byte)(outputX * signX * capX + 128.0);
                    dState.LY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 2)
                {
                    double outputX = tempX * tempX;
                    double outputY = tempY * tempY;
                    dState.LX = (byte)(outputX * signX * capX + 128.0);
                    dState.LY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 3)
                {
                    double outputX = tempX * tempX * tempX;
                    double outputY = tempY * tempY * tempY;
                    dState.LX = (byte)(outputX * capX + 128.0);
                    dState.LY = (byte)(outputY * capY + 128.0);
                }
                else if (lsOutCurveMode == 4)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = absX * (absX - 2.0);
                    double outputY = absY * (absY - 2.0);
                    dState.LX = (byte)(-1.0 * outputX * signX * capX + 128.0);
                    dState.LY = (byte)(-1.0 * outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 5)
                {
                    double innerX = Math.Abs(tempX) - 1.0;
                    double innerY = Math.Abs(tempY) - 1.0;
                    double outputX = innerX * innerX * innerX + 1.0;
                    double outputY = innerY * innerY * innerY + 1.0;
                    dState.LX = (byte)(1.0 * outputX * signX * capX + 128.0);
                    dState.LY = (byte)(1.0 * outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 6)
                {
                    dState.LX = ls.OutBezierCurve.LUT[dState.LX];
                    dState.LY = ls.OutBezierCurve.LUT[dState.LY];
                }
            }
            
            if (squStk.RSMode && (dState.RX != 128 || dState.RY != 128))
            {
                double capX = dState.RX >= 128 ? 127.0 : 128.0;
                double capY = dState.RY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.RX - 128.0) / capX;
                double tempY = (dState.RY - 128.0) / capY;
                DS4SquareStick sqstick = outSqrStk;
                sqstick.current.x = tempX; sqstick.current.y = tempY;
                sqstick.CircleToSquare(squStk.Roundness);
                tempX = sqstick.current.x < -1.0 ? -1.0 : sqstick.current.x > 1.0
                    ? 1.0 : sqstick.current.x;
                tempY = sqstick.current.y < -1.0 ? -1.0 : sqstick.current.y > 1.0
                    ? 1.0 : sqstick.current.y;
                //Console.WriteLine("Input ({0}) | Output ({1})", tempY, sqstick.current.y);
                dState.RX = (byte)(tempX * capX + 128.0);
                dState.RY = (byte)(tempY * capY + 128.0);
            }

            int rsOutCurveMode = (int)rs.OutCurvePreset;
            if (rsOutCurveMode > 0 && (dState.RX != 128 || dState.RY != 128))
            {
                double capX = dState.RX >= 128 ? 127.0 : 128.0;
                double capY = dState.RY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.RX - 128.0) / capX;
                double tempY = (dState.RY - 128.0) / capY;
                double signX = tempX >= 0.0 ? 1.0 : -1.0;
                double signY = tempY >= 0.0 ? 1.0 : -1.0;

                if (rsOutCurveMode == 1)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = 0.0;
                    double outputY = 0.0;

                    if (absX <= 0.4)
                    {
                        outputX = 0.55 * absX;
                    }
                    else if (absX <= 0.75)
                    {
                        outputX = absX - 0.18;
                    }
                    else if (absX > 0.75)
                    {
                        outputX = (absX * 1.72) - 0.72;
                    }

                    if (absY <= 0.4)
                    {
                        outputY = 0.55 * absY;
                    }
                    else if (absY <= 0.75)
                    {
                        outputY = absY - 0.18;
                    }
                    else if (absY > 0.75)
                    {
                        outputY = (absY * 1.72) - 0.72;
                    }

                    dState.RX = (byte)(outputX * signX * capX + 128.0);
                    dState.RY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 2)
                {
                    double outputX = tempX * tempX;
                    double outputY = tempY * tempY;
                    dState.RX = (byte)(outputX * signX * capX + 128.0);
                    dState.RY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 3)
                {
                    double outputX = tempX * tempX * tempX;
                    double outputY = tempY * tempY * tempY;
                    dState.RX = (byte)(outputX * capX + 128.0);
                    dState.RY = (byte)(outputY * capY + 128.0);
                }
                else if (rsOutCurveMode == 4)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = absX * (absX - 2.0);
                    double outputY = absY * (absY - 2.0);
                    dState.RX = (byte)(-1.0 * outputX * signX * capX + 128.0);
                    dState.RY = (byte)(-1.0 * outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 5)
                {
                    double innerX = Math.Abs(tempX) - 1.0;
                    double innerY = Math.Abs(tempY) - 1.0;
                    double outputX = innerX * innerX * innerX + 1.0;
                    double outputY = innerY * innerY * innerY + 1.0;
                    dState.RX = (byte)(1.0 * outputX * signX * capX + 128.0);
                    dState.RY = (byte)(1.0 * outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 6)
                {
                    dState.RX = rs.OutBezierCurve.LUT[dState.RX];
                    dState.RY = rs.OutBezierCurve.LUT[dState.RY];
                }
            }

            ITrigger2Config l2 = cfg.L2;
            int l2OutCurveMode = (int)l2.OutCurvePreset;
            if (l2OutCurveMode > 0 && dState.L2 != 0)
            {
                double temp = dState.L2 / 255.0;
                if (l2OutCurveMode == 1)
                {
                    double output;

                    if (temp <= 0.4)
                        output = 0.55 * temp;
                    else if (temp <= 0.75)
                        output = temp - 0.18;
                    else // if (temp > 0.75)
                        output = (temp * 1.72) - 0.72;
                    dState.L2 = (byte)(output * 255.0);
                }
                else if (l2OutCurveMode == 2)
                {
                    double output = temp * temp;
                    dState.L2 = (byte)(output * 255.0);
                }
                else if (l2OutCurveMode == 3)
                {
                    double output = temp * temp * temp;
                    dState.L2 = (byte)(output * 255.0);
                }
                else if (l2OutCurveMode == 4)
                {
                    double output = temp * (temp - 2.0);
                    dState.L2 = (byte)(-1.0 * output * 255.0);
                }
                else if (l2OutCurveMode == 5)
                {
                    double inner = Math.Abs(temp) - 1.0;
                    double output = inner * inner * inner + 1.0;
                    dState.L2 = (byte)(-1.0 * output * 255.0);
                }
                else if (l2OutCurveMode == 6)
                {
                    dState.L2 = l2.OutBezierCurve.LUT[dState.L2];
                }
            }

            ITrigger2Config r2 = cfg.R2;
            int r2OutCurveMode = (int)r2.OutCurvePreset;
            if (r2OutCurveMode > 0 && dState.R2 != 0)
            {
                double temp = dState.R2 / 255.0;
                if (r2OutCurveMode == 1)
                {
                    double output;

                    if (temp <= 0.4)
                        output = 0.55 * temp;
                    else if (temp <= 0.75)
                        output = temp - 0.18;
                    else // if (temp > 0.75)
                        output = (temp * 1.72) - 0.72;
                    dState.R2 = (byte)(output * 255.0);
                }
                else if (r2OutCurveMode == 2)
                {
                    double output = temp * temp;
                    dState.R2 = (byte)(output * 255.0);
                }
                else if (r2OutCurveMode == 3)
                {
                    double output = temp * temp * temp;
                    dState.R2 = (byte)(output * 255.0);
                }
                else if (r2OutCurveMode == 4)
                {
                    double output = temp * (temp - 2.0);
                    dState.R2 = (byte)(-1.0 * output * 255.0);
                }
                else if (r2OutCurveMode == 5)
                {
                    double inner = Math.Abs(temp) - 1.0;
                    double output = inner * inner * inner + 1.0;
                    dState.R2 = (byte)(-1.0 * output * 255.0);
                }
                else if (r2OutCurveMode == 6)
                {
                    dState.R2 = r2.OutBezierCurve.LUT[dState.R2];
                }
            }

            bool sOff = /*tempBool =*/ cfg.UseSAforMouse;
            if (sOff == false) {
                var sx = cfg.SX;
                var sz = cfg.SZ;
                int SXD = (int)(128d * sx.DeadZone);
                int SZD = (int)(128d * sz.DeadZone);
                double SXMax = sx.MaxZone;
                double SZMax = sz.MaxZone;
                double sxAntiDead = sx.AntiDeadZone;
                double szAntiDead = sz.AntiDeadZone;
                double sxsens = sx.Sensitivity;
                double szsens = sz.Sensitivity;
                int result = 0;

                int gyroX = cState.Motion.accelX, gyroZ = cState.Motion.accelZ;
                int absx = Math.Abs(gyroX), absz = Math.Abs(gyroZ);

                if (SXD > 0 || SXMax < 1.0 || sxAntiDead > 0)
                {
                    int maxValue = (int)(SXMax * 128d);
                    if (absx > SXD)
                    {
                        double ratioX = absx < maxValue ? (absx - SXD) / (double)(maxValue - SXD) : 1.0;
                        dState.Motion.outputAccelX = Math.Sign(gyroX) *
                            (int)Math.Min(128d, sxsens * 128d * ((1.0 - sxAntiDead) * ratioX + sxAntiDead));
                    }
                    else
                    {
                        dState.Motion.outputAccelX = 0;
                    }
                }
                else
                {
                    dState.Motion.outputAccelX = Math.Sign(gyroX) *
                        (int)Math.Min(128d, sxsens * 128d * (absx / 128d));
                }

                if (SZD > 0 || SZMax < 1.0 || szAntiDead > 0)
                {
                    int maxValue = (int)(SZMax * 128d);
                    if (absz > SZD)
                    {
                        double ratioZ = absz < maxValue ? (absz - SZD) / (double)(maxValue - SZD) : 1.0;
                        dState.Motion.outputAccelZ = Math.Sign(gyroZ) *
                            (int)Math.Min(128d, szsens * 128d * ((1.0 - szAntiDead) * ratioZ + szAntiDead));
                    }
                    else
                    {
                        dState.Motion.outputAccelZ = 0;
                    }
                }
                else
                {
                    dState.Motion.outputAccelZ = Math.Sign(gyroZ) *
                        (int)Math.Min(128d, szsens * 128d * (absz / 128d));
                }

                int sxOutCurveMode = (int)sx.OutCurvePreset;
                if (sxOutCurveMode > 0)
                {
                    double temp = dState.Motion.outputAccelX / 128.0;
                    double sign = Math.Sign(temp);
                    if (sxOutCurveMode == 1)
                    {
                        double output;
                        double abs = Math.Abs(temp);

                        if (abs <= 0.4)
                            output = 0.55 * abs;
                        else if (abs <= 0.75)
                            output = abs - 0.18;
                        else // if (abs > 0.75)
                            output = (abs * 1.72) - 0.72;
                        dState.Motion.outputAccelX = (byte)(output * sign * 128.0);
                    }
                    else if (sxOutCurveMode == 2)
                    {
                        double output = temp * temp;
                        result = (int)(output * sign * 128.0);
                        dState.Motion.outputAccelX = result;
                    }
                    else if (sxOutCurveMode == 3)
                    {
                        double output = temp * temp * temp;
                        result = (int)(output * 128.0);
                        dState.Motion.outputAccelX = result;
                    }
                    else if (sxOutCurveMode == 4)
                    {
                        double abs = Math.Abs(temp);
                        double output = abs * (abs - 2.0);
                        dState.Motion.outputAccelX = (byte)(-1.0 * output *
                            sign * 128.0);
                    }
                    else if (sxOutCurveMode == 5)
                    {
                        double inner = Math.Abs(temp) - 1.0;
                        double output = inner * inner * inner + 1.0;
                        dState.Motion.outputAccelX = (byte)(-1.0 * output * 255.0);
                    }
                    else if (sxOutCurveMode == 6)
                    {
                        int signSA = Math.Sign(dState.Motion.outputAccelX);
                        dState.Motion.outputAccelX = sx.OutBezierCurve.LUT[Math.Min(Math.Abs(dState.Motion.outputAccelX), 128)] * signSA;
                    }
                }

                int szOutCurveMode = (int)sz.OutCurvePreset;
                if (szOutCurveMode > 0 && dState.Motion.outputAccelZ != 0)
                {
                    double temp = dState.Motion.outputAccelZ / 128.0;
                    double sign = Math.Sign(temp);
                    if (szOutCurveMode == 1)
                    {
                        double output;
                        double abs = Math.Abs(temp);

                        if (abs <= 0.4)
                            output = 0.55 * abs;
                        else if (abs <= 0.75)
                            output = abs - 0.18;
                        else // if (abs > 0.75)
                            output = (abs * 1.72) - 0.72;
                        dState.Motion.outputAccelZ = (byte)(output * sign * 128.0);
                    }
                    else if (szOutCurveMode == 2)
                    {
                        double output = temp * temp;
                        result = (int)(output * sign * 128.0);
                        dState.Motion.outputAccelZ = result;
                    }
                    else if (szOutCurveMode == 3)
                    {
                        double output = temp * temp * temp;
                        result = (int)(output * 128.0);
                        dState.Motion.outputAccelZ = result;
                    }
                    else if (szOutCurveMode == 4)
                    {
                        double abs = Math.Abs(temp);
                        double output = abs * (abs - 2.0);
                        dState.Motion.outputAccelZ = (byte)(-1.0 * output *
                            sign * 128.0);
                    }
                    else if (szOutCurveMode == 5)
                    {
                        double inner = Math.Abs(temp) - 1.0;
                        double output = inner * inner * inner + 1.0;
                        dState.Motion.outputAccelZ = (byte)(-1.0 * output * 255.0);
                    }
                    else if (szOutCurveMode == 6)
                    {
                        int signSA = Math.Sign(dState.Motion.outputAccelZ);
                        dState.Motion.outputAccelZ = sz.OutBezierCurve.LUT[Math.Min(Math.Abs(dState.Motion.outputAccelZ), 128)] * signSA;
                    }
                }
            }

            return dState;
        }

        /* TODO: Possibly remove usage of this version of the method */
        private bool ShiftTrigger(int trigger, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            bool result = false;
            if (trigger == 0)
            {
                result = false;
            }
            else
            {
                DS4Controls ds = shiftTriggerMapping[trigger];
                result = getBoolMapping(ds, cState, eState, tp);
            }

            return result;
        }

        private bool ShiftTrigger2(int trigger, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            bool result = false;
            if (trigger == 0)
            {
                result = false;
            }
            else if (trigger < 26)
            {
                DS4Controls ds = shiftTriggerMapping[trigger];
                result = getBoolMapping2(ds, cState, eState, tp);
            }
            else if (trigger == 26)
            {
                result = cState.Touch1Finger;
            }

            return result;
        }

        private static X360Controls getX360ControlsByName(string key)
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
                default: break;
            }

            return X360Controls.Unbound;
        }

        /// <summary>
        /// Map DS4 Buttons/Axes to other DS4 Buttons/Axes (largely the same as Xinput ones) and to keyboard and mouse buttons.
        /// </summary>
        private bool held;
        private int oldmouse = -1;

        public void MapCustom(DS4State cState, DS4State MappedState, DS4StateExposed eState,
            Mouse tp)
        {
            /* TODO: This method is slow sauce. Find ways to speed up action execution */
            double tempMouseDeltaX = 0.0;
            double tempMouseDeltaY = 0.0;
            int mouseDeltaX = 0;
            int mouseDeltaY = 0;

            cState.calculateStickAngles();
            fieldMapping.populateFieldMapping(cState, eState, tp);
            outputFieldMapping.populateFieldMapping(cState, eState, tp);
            //DS4StateFieldMapping fieldMapping = new DS4StateFieldMapping(cState, eState, tp);
            //DS4StateFieldMapping outputfieldMapping = new DS4StateFieldMapping(cState, eState, tp);

            if (cfg.ProfileActions.Count > 0 || aux.UseTempProfile)
                MapCustomAction( cState, MappedState, eState, tp);
            //if (ctrl.DS4Controllers[device] == null) return;

            //cState.CopyTo(MappedState);

            //Dictionary<DS4Controls, DS4Controls> tempControlDict = new Dictionary<DS4Controls, DS4Controls>();
            //MultiValueDict<DS4Controls, DS4Controls> tempControlDict = new MultiValueDict<DS4Controls, DS4Controls>();
            DS4Controls usingExtra = DS4Controls.None;
            foreach (DS4ControlSettings dcs in cfg.DS4CSettings)
            {
                object action = null;
                DS4ControlSettings.ActionType actionType = 0;
                DS4KeyType keyType = DS4KeyType.None;
                if (dcs.Shift.Action != null && ShiftTrigger2(dcs.Shift.ShiftTrigger, cState, eState, tp))
                {
                    action = dcs.Shift.Action;
                    actionType = dcs.Shift.ActionType;
                    keyType = dcs.Shift.KeyType;
                }
                else if (dcs.Norm.Action != null)
                {
                    action = dcs.Norm.Action;
                    actionType = dcs.Norm.ActionType;
                    keyType = dcs.Norm.KeyType;
                }

                if (usingExtra == DS4Controls.None || usingExtra == dcs.Control)
                {
                    bool shiftE = !string.IsNullOrEmpty(dcs.Shift.Extras) && ShiftTrigger2(dcs.Shift.ShiftTrigger, cState, eState, tp);
                    bool regE = !string.IsNullOrEmpty(dcs.Norm.Extras);
                    if ((regE || shiftE) && getBoolActionMapping2(dcs.Control, cState, eState, tp))
                    {
                        usingExtra = dcs.Control;
                        string p;
                        if (shiftE)
                            p = dcs.Shift.Extras;
                        else
                            p = dcs.Norm.Extras;

                        string[] extraS = p.Split(',');
                        int extrasSLen = extraS.Length;
                        int[] extras = new int[extrasSLen];
                        for (int i = 0; i < extrasSLen; i++)
                        {
                            int b;
                            if (int.TryParse(extraS[i], out b))
                                extras[i] = b;
                        }

                        held = true;
                        try
                        {
                            if (!(extras[0] == extras[1] && extras[1] == 0))
                                ctrl.setRumble((byte)extras[0], (byte)extras[1]);

                            if (extras[2] == 1)
                            {
                                DS4Color color = new DS4Color { red = (byte)extras[3], green = (byte)extras[4], blue = (byte)extras[5] };
                                lightBar.forcedColor = color;
                                lightBar.forcedFlash = (byte)extras[6];
                                lightBar.forcedLight = true;
                            }

                            if (extras[7] == 1)
                            {
                                if (oldmouse == -1)
                                    oldmouse = cfg.ButtonMouseSensitivity;
                                cfg.ButtonMouseSensitivity = extras[8];
                            }
                        }
                        catch { }
                    }
                    else if ((regE || shiftE) && held)
                    {
                        lightBar.forcedLight = false;
                        lightBar.forcedFlash = 0;
                        if (oldmouse != -1)
                        {
                            cfg.ButtonMouseSensitivity = oldmouse;
                            oldmouse = -1;
                        }

                        ctrl.setRumble(0, 0);
                        held = false;
                        usingExtra = DS4Controls.None;
                    }
                }

                if (action != null)
                {
                    if (actionType == DS4ControlSettings.ActionType.Macro)
                    {
                        bool active = getBoolMapping2(dcs.Control, cState, eState, tp);
                        if (active)
                        {
                            PlayMacro(macroControl, string.Join("/", (int[])action), dcs.Control, keyType);
                        }
                        else
                        {
                            EndMacro(macroControl, string.Join("/", (int[])action), dcs.Control);
                        }

                        // erase default mappings for things that are remapped
                        resetToDefaultValue2(dcs.Control, MappedState);
                    }
                    else if (actionType == DS4ControlSettings.ActionType.Key)
                    {
                        ushort value = Convert.ToUInt16(action);
                        if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                        {
                            SyntheticState.KeyPresses kp;
                            if (!deviceState.keyPresses.TryGetValue(value, out kp))
                                deviceState.keyPresses[value] = kp = new SyntheticState.KeyPresses();

                            if (keyType.HasFlag(DS4KeyType.ScanCode))
                                kp.current.scanCodeCount++;
                            else
                                kp.current.vkCount++;

                            if (keyType.HasFlag(DS4KeyType.Toggle))
                            {
                                if (!pressedonce[value])
                                {
                                    kp.current.toggle = !kp.current.toggle;
                                    pressedonce[value] = true;
                                }
                                kp.current.toggleCount++;
                            }
                            kp.current.repeatCount++;
                        }
                        else
                            pressedonce[value] = false;

                        // erase default mappings for things that are remapped
                        resetToDefaultValue2(dcs.Control, MappedState);
                    }
                    else if (actionType == DS4ControlSettings.ActionType.Button)
                    {
                        int keyvalue = 0;
                        bool isAnalog = false;

                        if (dcs.Control >= DS4Controls.LXNeg && dcs.Control <= DS4Controls.RYPos)
                        {
                            isAnalog = true;
                        }
                        else if (dcs.Control == DS4Controls.L2 || dcs.Control == DS4Controls.R2)
                        {
                            isAnalog = true;
                        }
                        else if (dcs.Control >= DS4Controls.GyroXPos && dcs.Control <= DS4Controls.GyroZNeg)
                        {
                            isAnalog = true;
                        }

                        X360Controls xboxControl = X360Controls.None;
                        if (action is X360Controls)
                        {
                            xboxControl = (X360Controls)action;
                        }
                        else if (action is string)
                        {
                            xboxControl = getX360ControlsByName(action.ToString());
                        }

                        if (xboxControl >= X360Controls.LXNeg && xboxControl <= X360Controls.Start)
                        {
                            DS4Controls tempDS4Control = AppState.RevButtonMapping[(int)xboxControl];
                            customMapQueue.Enqueue(new ControlToXInput(dcs.Control, tempDS4Control));
                            //tempControlDict.Add(dcs.Control, tempDS4Control);
                        }
                        else if (xboxControl >= X360Controls.LeftMouse && xboxControl <= X360Controls.WDOWN)
                        {
                            switch (xboxControl)
                            {
                                case X360Controls.LeftMouse:
                                {
                                    keyvalue = 256;
                                    if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                                        deviceState.currentClicks.leftCount++;

                                    break;
                                }
                                case X360Controls.RightMouse:
                                {
                                    keyvalue = 257;
                                    if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                                        deviceState.currentClicks.rightCount++;

                                    break;
                                }
                                case X360Controls.MiddleMouse:
                                {
                                    keyvalue = 258;
                                    if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                                        deviceState.currentClicks.middleCount++;

                                    break;
                                }
                                case X360Controls.FourthMouse:
                                {
                                    keyvalue = 259;
                                    if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                                        deviceState.currentClicks.fourthCount++;

                                    break;
                                }
                                case X360Controls.FifthMouse:
                                {
                                    keyvalue = 260;
                                    if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                                        deviceState.currentClicks.fifthCount++;

                                    break;
                                }
                                case X360Controls.WUP:
                                {
                                    if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                                    {
                                        if (isAnalog)
                                            getMouseWheelMapping(dcs.Control, cState, eState, tp, false);
                                        else
                                            deviceState.currentClicks.wUpCount++;
                                    }

                                    break;
                                }
                                case X360Controls.WDOWN:
                                {
                                    if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                                    {
                                        if (isAnalog)
                                            getMouseWheelMapping(dcs.Control, cState, eState, tp, true);
                                        else
                                            deviceState.currentClicks.wDownCount++;
                                    }

                                    break;
                                }

                                default: break;
                            }
                        }
                        else if (xboxControl >= X360Controls.MouseUp && xboxControl <= X360Controls.MouseRight)
                        {
                            switch (xboxControl)
                            {
                                case X360Controls.MouseUp:
                                {
                                    if (tempMouseDeltaY == 0)
                                    {
                                        tempMouseDeltaY = getMouseMapping(dcs.Control, cState, eState, 0);
                                        tempMouseDeltaY = -Math.Abs((tempMouseDeltaY == -2147483648 ? 0 : tempMouseDeltaY));
                                    }

                                    break;
                                }
                                case X360Controls.MouseDown:
                                {
                                    if (tempMouseDeltaY == 0)
                                    {
                                        tempMouseDeltaY = getMouseMapping(dcs.Control, cState, eState, 1);
                                        tempMouseDeltaY = Math.Abs((tempMouseDeltaY == -2147483648 ? 0 : tempMouseDeltaY));
                                    }

                                    break;
                                }
                                case X360Controls.MouseLeft:
                                {
                                    if (tempMouseDeltaX == 0)
                                    {
                                        tempMouseDeltaX = getMouseMapping(dcs.Control, cState, eState, 2);
                                        tempMouseDeltaX = -Math.Abs((tempMouseDeltaX == -2147483648 ? 0 : tempMouseDeltaX));
                                    }

                                    break;
                                }
                                case X360Controls.MouseRight:
                                {
                                    if (tempMouseDeltaX == 0)
                                    {
                                        tempMouseDeltaX = getMouseMapping(dcs.Control, cState, eState, 3);
                                        tempMouseDeltaX = Math.Abs((tempMouseDeltaX == -2147483648 ? 0 : tempMouseDeltaX));
                                    }

                                    break;
                                }

                                default: break;
                            }
                        }

                        if (keyType.HasFlag(DS4KeyType.Toggle))
                        {
                            if (getBoolActionMapping2(dcs.Control, cState, eState, tp))
                            {
                                if (!pressedonce[keyvalue])
                                {
                                    deviceState.currentClicks.toggle = !deviceState.currentClicks.toggle;
                                    pressedonce[keyvalue] = true;
                                }
                                deviceState.currentClicks.toggleCount++;
                            }
                            else
                            {
                                pressedonce[keyvalue] = false;
                            }
                        }

                        // erase default mappings for things that are remapped
                        resetToDefaultValue2(dcs.Control, MappedState);
                    }
                }
                else
                {
                    DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[(int)dcs.Control];
                    if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
                    //if (dcs.Control > DS4Controls.None && dcs.Control < DS4Controls.L1)
                    {
                        //int current = (int)dcs.Control;
                        //outputfieldMapping.axisdirs[current] = fieldMapping.axisdirs[current];
                        customMapQueue.Enqueue(new ControlToXInput(dcs.Control, dcs.Control));
                    }
                }
            }

            Queue<ControlToXInput> tempControl = customMapQueue;
            unchecked
            {
                for (int i = 0, len = tempControl.Count; i < len; i++)
                //while(tempControl.Any())
                {
                    ControlToXInput tempMap = tempControl.Dequeue();
                    int controlNum = (int)tempMap.ds4input;
                    int tempOutControl = (int)tempMap.xoutput;
                    if (tempMap.xoutput >= DS4Controls.LXNeg && tempMap.xoutput <= DS4Controls.RYPos)
                    {
                        const byte axisDead = 128;
                        DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[tempOutControl];
                        bool alt = controlType == DS4StateFieldMapping.ControlType.AxisDir && tempOutControl % 2 == 0 ? true : false;
                        byte axisMapping = getXYAxisMapping2(tempMap.ds4input, cState, eState, tp, alt);
                        if (axisMapping != axisDead)
                        {
                            int controlRelation = tempOutControl % 2 == 0 ? tempOutControl - 1 : tempOutControl + 1;
                            outputFieldMapping.axisdirs[tempOutControl] = axisMapping;
                            outputFieldMapping.axisdirs[controlRelation] = axisMapping;
                        }
                    }
                    else
                    {
                        if (tempMap.xoutput == DS4Controls.L2 || tempMap.xoutput == DS4Controls.R2)
                        {
                            const byte axisZero = 0;
                            byte axisMapping = getByteMapping2(tempMap.ds4input, cState, eState, tp);
                            if (axisMapping != axisZero)
                                outputFieldMapping.triggers[tempOutControl] = axisMapping;
                        }
                        else
                        {
                            bool value = getBoolMapping2(tempMap.ds4input, cState, eState, tp);
                            if (value)
                                outputFieldMapping.buttons[tempOutControl] = value;
                        }
                    }
                }
            }

            outputFieldMapping.populateState(MappedState);

            if (macroCount > 0)
            {
                if (macroControl[00]) MappedState.Cross = true;
                if (macroControl[01]) MappedState.Circle = true;
                if (macroControl[02]) MappedState.Square = true;
                if (macroControl[03]) MappedState.Triangle = true;
                if (macroControl[04]) MappedState.Options = true;
                if (macroControl[05]) MappedState.Share = true;
                if (macroControl[06]) MappedState.DpadUp = true;
                if (macroControl[07]) MappedState.DpadDown = true;
                if (macroControl[08]) MappedState.DpadLeft = true;
                if (macroControl[09]) MappedState.DpadRight = true;
                if (macroControl[10]) MappedState.PS = true;
                if (macroControl[11]) MappedState.L1 = true;
                if (macroControl[12]) MappedState.R1 = true;
                if (macroControl[13]) MappedState.L2 = 255;
                if (macroControl[14]) MappedState.R2 = 255;
                if (macroControl[15]) MappedState.L3 = true;
                if (macroControl[16]) MappedState.R3 = true;
                if (macroControl[17]) MappedState.LX = 255;
                if (macroControl[18]) MappedState.LX = 0;
                if (macroControl[19]) MappedState.LY = 255;
                if (macroControl[20]) MappedState.LY = 0;
                if (macroControl[21]) MappedState.RX = 255;
                if (macroControl[22]) MappedState.RX = 0;
                if (macroControl[23]) MappedState.RY = 255;
                if (macroControl[24]) MappedState.RY = 0;
            }

            if (cfg.SASteeringWheelEmulationAxis != SASteeringWheelEmulationAxisType.None)
            {
                MappedState.SASteeringWheelEmulationUnit = Scale360degreeGyroAxis(eState);
            }

            calculateFinalMouseMovement(ref tempMouseDeltaX, ref tempMouseDeltaY,
                out mouseDeltaX, out mouseDeltaY);
            if (mouseDeltaX != 0 || mouseDeltaY != 0)
            {
                InputMethods.MoveCursorBy(mouseDeltaX, mouseDeltaY);
            }
        }

        private bool IfAxisIsNotModified(bool shift, DS4Controls dc)
        {
            return shift ? false : cfg.GetDS4Action(dc, false) == null;
        }

        private async void MapCustomAction(DS4State cState, DS4State MappedState,
            DS4StateExposed eState, Mouse tp)
        {
            /* TODO: This method is slow sauce. Find ways to speed up action execution */
            try
            {
                int totalActionCount = API.Config.Actions.Count;
                DS4StateFieldMapping previousFieldMapping = null;
                var profileActions = cfg.ProfileActions;

                if (actionDone.Length < totalActionCount)
                    Array.Resize(ref actionDone, totalActionCount);

                foreach (string actionname in profileActions)
                {
                    SpecialAction action = cfg.GetProfileAction(actionname);
                    int index = cfg.GetProfileActionIndexOf(actionname);

                    double time = 0.0;
                    //If a key or button is assigned to the trigger, a key special action is used like
                    //a quick tap to use and hold to use the regular custom button/key
                    bool triggerToBeTapped = action.typeID == SpecialAction.ActionTypeId.None && action.trigger.Count == 1 &&
                            cfg.GetDS4Action(action.trigger[0], false) == null;
                    if (!(action.typeID == SpecialAction.ActionTypeId.None || index < 0))
                    {
                        bool triggeractivated = true;
                        if (action.delayTime > 0.0)
                        {
                            triggeractivated = false;
                            bool subtriggeractivated = true;
                            foreach (DS4Controls dc in action.trigger)
                            {
                                if (!getBoolSpecialActionMapping(dc, cState, eState, tp))
                                {
                                    subtriggeractivated = false;
                                    break;
                                }
                            }
                            if (subtriggeractivated)
                            {
                                time = action.delayTime;
                                nowAction = DateTime.UtcNow;
                                if (nowAction >= oldnowAction + TimeSpan.FromSeconds(time))
                                    triggeractivated = true;
                            }
                            else if (nowAction < DateTime.UtcNow - TimeSpan.FromMilliseconds(100))
                                oldnowAction = DateTime.UtcNow;
                        }
                        else if (triggerToBeTapped && oldnowKeyAct == DateTime.MinValue)
                        {
                            triggeractivated = false;
                            bool subtriggeractivated = true;
                            foreach (DS4Controls dc in action.trigger)
                            {
                                if (!getBoolSpecialActionMapping(dc, cState, eState, tp))
                                {
                                    subtriggeractivated = false;
                                    break;
                                }
                            }
                            if (subtriggeractivated)
                            {
                                oldnowKeyAct = DateTime.UtcNow;
                            }
                        }
                        else if (triggerToBeTapped && oldnowKeyAct != DateTime.MinValue)
                        {
                            triggeractivated = false;
                            bool subtriggeractivated = true;
                            foreach (DS4Controls dc in action.trigger)
                            {
                                if (!getBoolSpecialActionMapping(dc, cState, eState, tp))
                                {
                                    subtriggeractivated = false;
                                    break;
                                }
                            }
                            DateTime now = DateTime.UtcNow;
                            if (!subtriggeractivated && now <= oldnowKeyAct + TimeSpan.FromMilliseconds(250))
                            {
                                await Task.Delay(3); //if the button is assigned to the same key use a delay so the key down is the last action, not key up
                                triggeractivated = true;
                                oldnowKeyAct = DateTime.MinValue;
                            }
                            else if (!subtriggeractivated)
                                oldnowKeyAct = DateTime.MinValue;
                        }
                        else
                        {
                            foreach (DS4Controls dc in action.trigger)
                            {
                                if (!getBoolSpecialActionMapping(dc, cState, eState, tp))
                                {
                                    triggeractivated = false;
                                    break;
                                }
                            }
                        }

                        bool utriggeractivated = true;
                        int uTriggerCount = action.uTrigger.Count;
                        if (action.typeID == SpecialAction.ActionTypeId.Key && uTriggerCount > 0)
                        {
                            foreach (DS4Controls dc in action.uTrigger)
                            {
                                if (!getBoolSpecialActionMapping(dc, cState, eState, tp))
                                {
                                    utriggeractivated = false;
                                    break;
                                }
                            }
                            if (action.pressRelease) utriggeractivated = !utriggeractivated;
                        }

                        bool actionFound = false;
                        if (triggeractivated)
                        {
                            if (action.typeID == SpecialAction.ActionTypeId.Program)
                            {
                                actionFound = true;

                                if (!actionDone[index])
                                {
                                    actionDone[index] = true;
                                    if (!string.IsNullOrEmpty(action.extra))
                                        Process.Start(action.details, action.extra);
                                    else
                                        Process.Start(action.details);
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.Profile)
                            {
                                actionFound = true;

                                if (!actionDone[index] && (!aux.UseTempProfile || untriggeraction == null || untriggeraction.typeID != SpecialAction.ActionTypeId.Profile) )
                                {
                                    actionDone[index] = true;
                                    // If Loadprofile special action doesn't have untrigger keys or automatic untrigger option is not set then don't set untrigger status. This way the new loaded profile allows yet another loadProfile action key event.
                                    if (action.uTrigger.Count > 0 || action.automaticUntrigger)
                                    {
                                        untriggeraction = action;
                                        untriggerindex = index;

                                        // If the existing profile is a temp profile then store its name, because automaticUntrigger needs to know where to go back (empty name goes back to default regular profile)
                                        untriggeraction.prevProfileName = (aux.UseTempProfile ? aux.TempProfileName : string.Empty);
                                    }
                                    foreach (DS4Controls dc in action.trigger)
                                    {
                                        DS4ControlSettings dcs = cfg.GetDS4CSetting(dc);
                                        if (dcs.Norm.Action != null)
                                        {
                                            if (dcs.Norm.ActionType == DS4ControlSettings.ActionType.Key)
                                                InputMethods.performKeyRelease(ushort.Parse(dcs.Norm.Action.ToString()));
                                            else if (dcs.Norm.ActionType == DS4ControlSettings.ActionType.Macro)
                                            {
                                                int[] keys = (int[])dcs.Norm.Action;
                                                for (int j = 0, keysLen = keys.Length; j < keysLen; j++)
                                                    InputMethods.performKeyRelease((ushort)keys[j]);
                                            }
                                        }
                                    }

                                    string prolog = Properties.Resources.UsingProfile.Replace("*number*", (devIndex + 1).ToString()).Replace("*Profile name*", action.details);
                                    AppLogger.LogToGui(prolog, false);
                                    cfg.LoadTempProfile(action.details, true, ctrl);

                                    if (action.uTrigger.Count == 0 && !action.automaticUntrigger)
                                    {
                                        // If the new profile has any actions with the same action key (controls) than this action (which doesn't have untrigger keys) then set status of those actions to wait for the release of the existing action key. 
                                        var profileActionsNext = cfg.ProfileActions;
                                        for (int actionIndexNext = 0, profileListLenNext = profileActionsNext.Count; actionIndexNext < profileListLenNext; actionIndexNext++)
                                        {
                                            string actionnameNext = profileActionsNext[actionIndexNext];
                                            SpecialAction actionNext = cfg.GetProfileAction(actionnameNext);
                                            int indexNext = cfg.GetProfileActionIndexOf(actionnameNext);

                                            if (actionNext.controls == action.controls)
                                                actionDone[indexNext] = true;
                                        }
                                    }

                                    return;
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.Macro)
                            {
                                actionFound = true;

                                if (!actionDone[index])
                                {
                                    DS4KeyType keyType = action.keyType;
                                    actionDone[index] = true;
                                    foreach (DS4Controls dc in action.trigger)
                                    {
                                        resetToDefaultValue2(dc, MappedState);
                                    }

                                    PlayMacro(macroControl, String.Join("/", action.macro), DS4Controls.None, keyType);
                                }
                                else
                                    EndMacro(macroControl, String.Join("/", action.macro), DS4Controls.None);
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.Key)
                            {
                                actionFound = true;

                                if (uTriggerCount == 0 || (uTriggerCount > 0 && untriggerindex == -1 && !actionDone[index]))
                                {
                                    actionDone[index] = true;
                                    untriggerindex = index;
                                    ushort key;
                                    ushort.TryParse(action.details, out key);
                                    if (uTriggerCount == 0)
                                    {
                                        SyntheticState.KeyPresses kp;
                                        if (!deviceState.keyPresses.TryGetValue(key, out kp))
                                            deviceState.keyPresses[key] = kp = new SyntheticState.KeyPresses();
                                        if (action.keyType.HasFlag(DS4KeyType.ScanCode))
                                            kp.current.scanCodeCount++;
                                        else
                                            kp.current.vkCount++;
                                        kp.current.repeatCount++;
                                    }
                                    else if (action.keyType.HasFlag(DS4KeyType.ScanCode))
                                        InputMethods.performSCKeyPress(key);
                                    else
                                        InputMethods.performKeyPress(key);
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.DisconnectBT)
                            {
                                actionFound = true;

                                DS4Device d = ctrl.DS4Controller;
                                bool synced = /*tempBool =*/ d.isSynced();
                                if (synced && !d.isCharging())
                                {
                                    ConnectionType deviceConn = d.getConnectionType();
                                    bool exclusive = /*tempBool =*/ d.isExclusive();
                                    if (deviceConn == ConnectionType.BT)
                                    {
                                        d.DisconnectBT();
                                    }
                                    else if (deviceConn == ConnectionType.SONYWA && exclusive)
                                    {
                                        d.DisconnectDongle();
                                    }

                                    foreach (DS4Controls dc in action.trigger)
                                    {
                                        DS4ControlSettings dcs = cfg.GetDS4CSetting(dc);
                                        if (dcs.Norm.Action != null)
                                        {
                                            if (dcs.Norm.ActionType == DS4ControlSettings.ActionType.Key)
                                                InputMethods.performKeyRelease((ushort)dcs.Norm.Action);
                                            else if (dcs.Norm.ActionType == DS4ControlSettings.ActionType.Macro)
                                            {
                                                int[] keys = (int[])dcs.Norm.Action;
                                                for (int j = 0, keysLen = keys.Length; j < keysLen; j++)
                                                    InputMethods.performKeyRelease((ushort)keys[j]);
                                            }
                                        }
                                    }
                                    return;
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.BatteryCheck)
                            {
                                actionFound = true;

                                string[] dets = action.details.Split('|');
                                if (dets.Length == 1)
                                    dets = action.details.Split(',');
                                if (bool.Parse(dets[1]) && !actionDone[index])
                                {
                                    AppLogger.LogToTray("Controller " + (devIndex + 1) + ": " +
                                        ctrl.getDS4Battery(), true);
                                }
                                if (bool.Parse(dets[2]))
                                {
                                    DS4Device d = ctrl.DS4Controller;
                                    if (!actionDone[index])
                                    {
                                        lastColor = d.LightBarColor;
                                        lightBar.forcedLight = true;
                                    }
                                    DS4Color empty = new DS4Color(byte.Parse(dets[3]), byte.Parse(dets[4]), byte.Parse(dets[5]));
                                    DS4Color full = new DS4Color(byte.Parse(dets[6]), byte.Parse(dets[7]), byte.Parse(dets[8]));
                                    DS4Color trans = Util.getTransitionedColor(ref empty, ref full, d.Battery);
                                    if (fadetimer < 100)
                                        lightBar.forcedColor = Util.getTransitionedColor(ref lastColor, ref trans, fadetimer += 2);
                                }
                                actionDone[index] = true;
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.SASteeringWheelEmulationCalibrate)
                            {
                                actionFound = true;

                                DS4Device d = ctrl.DS4Controller;
                                // If controller is not already in SASteeringWheelCalibration state then enable it now. If calibration is active then complete it (commit calibration values)
                                if (d.WheelRecalibrateActiveState == 0 && DateTime.UtcNow > (action.firstTap + TimeSpan.FromMilliseconds(3000)))
                                {
                                    action.firstTap = DateTime.UtcNow;
                                    d.WheelRecalibrateActiveState = 1;  // Start calibration process
                                }
                                else if (d.WheelRecalibrateActiveState == 2 && DateTime.UtcNow > (action.firstTap + TimeSpan.FromMilliseconds(3000)))
                                {
                                    action.firstTap = DateTime.UtcNow;
                                    d.WheelRecalibrateActiveState = 3;  // Complete calibration process
                                }

                                actionDone[index] = true;
                            }
                        }
                        else
                        {
                            if (action.typeID == SpecialAction.ActionTypeId.BatteryCheck)
                            {
                                actionFound = true;
                                if (actionDone[index])
                                {
                                    fadetimer = 0;
#if false
                                    if (prevFadetimer == fadetimer)
                                    {
                                        prevFadetimer = 0;
                                        fadetimer = 0;
                                    }
                                    else
                                        prevFadetimer = fadetimer;
#endif
                                    lightBar.forcedLight = false;
                                    actionDone[index] = false;
                                }
                            }
                            else if (action.typeID != SpecialAction.ActionTypeId.Key &&
                                     action.typeID != SpecialAction.ActionTypeId.XboxGameDVR &&
                                     action.typeID != SpecialAction.ActionTypeId.MultiAction)
                            {
                                // Ignore
                                actionFound = true;
                                actionDone[index] = false;
                            }
                        }

                        if (!actionFound)
                        {
                            if (uTriggerCount > 0 && utriggeractivated && action.typeID == SpecialAction.ActionTypeId.Key)
                            {
                                actionFound = true;

                                if (untriggerindex > -1 && !actionDone[index])
                                {
                                    actionDone[index] = true;
                                    untriggerindex = -1;
                                    ushort key;
                                    ushort.TryParse(action.details, out key);
                                    if (action.keyType.HasFlag(DS4KeyType.ScanCode))
                                        InputMethods.performSCKeyRelease(key);
                                    else
                                        InputMethods.performKeyRelease(key);
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.XboxGameDVR || action.typeID == SpecialAction.ActionTypeId.MultiAction)
                            {
                                actionFound = true;

                                bool tappedOnce = action.tappedOnce, firstTouch = action.firstTouch,
                                    secondtouchbegin = action.secondtouchbegin;
                                //DateTime pastTime = action.pastTime, firstTap = action.firstTap,
                                //    TimeofEnd = action.TimeofEnd;

                                /*if (getCustomButton(device, action.trigger[0]) != X360Controls.Unbound)
                                    getCustomButtons(device)[action.trigger[0]] = X360Controls.Unbound;
                                if (getCustomMacro(device, action.trigger[0]) != "0")
                                    getCustomMacros(device).Remove(action.trigger[0]);
                                if (getCustomKey(device, action.trigger[0]) != 0)
                                    getCustomMacros(device).Remove(action.trigger[0]);*/
                                    string[] dets = action.details.Split(',');
                                DS4Device d = ctrl.DS4Controller;
                                //cus

                                DS4State tempPrevState = d.getPreviousStateRef();
                                // Only create one instance of previous DS4StateFieldMapping in case more than one multi-action
                                // button is assigned
                                if (previousFieldMapping == null)
                                {
                                    previousFieldMapping = this.previousFieldMapping;
                                    previousFieldMapping.populateFieldMapping(tempPrevState, eState, tp, true);
                                    //previousFieldMapping = new DS4StateFieldMapping(tempPrevState, eState, tp, true);
                                }

                                bool activeCur = getBoolSpecialActionMapping(action.trigger[0], cState, eState, tp);
                                bool activePrev = getBoolSpecialActionMapping(action.trigger[0], tempPrevState, eState, tp, previousFieldMapping);
                                if (activeCur && !activePrev)
                                {
                                    // pressed down
                                    action.pastTime = DateTime.UtcNow;
                                    if (action.pastTime <= (action.firstTap + TimeSpan.FromMilliseconds(150)))
                                    {
                                        action.tappedOnce = tappedOnce = false;
                                        action.secondtouchbegin = secondtouchbegin = true;
                                        //tappedOnce = false;
                                        //secondtouchbegin = true;
                                    }
                                    else
                                        action.firstTouch = firstTouch = true;
                                        //firstTouch = true;
                                }
                                else if (!activeCur && activePrev)
                                {
                                    // released
                                    if (secondtouchbegin)
                                    {
                                        action.firstTouch = firstTouch = false;
                                        action.secondtouchbegin = secondtouchbegin = false;
                                        //firstTouch = false;
                                        //secondtouchbegin = false;
                                    }
                                    else if (firstTouch)
                                    {
                                        action.firstTouch = firstTouch = false;
                                        //firstTouch = false;
                                        if (DateTime.UtcNow <= (action.pastTime + TimeSpan.FromMilliseconds(150)) && !tappedOnce)
                                        {
                                            action.tappedOnce = tappedOnce = true;
                                            //tappedOnce = true;
                                            action.firstTap = DateTime.UtcNow;
                                            action.TimeofEnd = DateTime.UtcNow;
                                        }
                                    }
                                }

                                int type = 0;
                                string macro = "";
                                if (tappedOnce) //single tap
                                {
                                    if (action.typeID == SpecialAction.ActionTypeId.MultiAction)
                                    {
                                        macro = dets[0];
                                    }
                                    else if (int.TryParse(dets[0], out type))
                                    {
                                        switch (type)
                                        {
                                            case 0: macro = "91/71/71/91"; break;
                                            case 1: macro = "91/164/82/82/164/91"; break;
                                            case 2: macro = "91/164/44/44/164/91"; break;
                                            case 3: macro = dets[3] + "/" + dets[3]; break;
                                            case 4: macro = "91/164/71/71/164/91"; break;
                                        }
                                    }

                                    if ((DateTime.UtcNow - action.TimeofEnd) > TimeSpan.FromMilliseconds(150))
                                    {
                                        if (macro != "")
                                            PlayMacro(macroControl, macro, DS4Controls.None, DS4KeyType.None);

                                        tappedOnce = false;
                                        action.tappedOnce = false;
                                    }
                                    //if it fails the method resets, and tries again with a new tester value (gives tap a delay so tap and hold can work)
                                }
                                else if (firstTouch && (DateTime.UtcNow - action.pastTime) > TimeSpan.FromMilliseconds(500)) //helddown
                                {
                                    if (action.typeID == SpecialAction.ActionTypeId.MultiAction)
                                    {
                                        macro = dets[1];
                                    }
                                    else if (int.TryParse(dets[1], out type))
                                    {
                                        switch (type)
                                        {
                                            case 0: macro = "91/71/71/91"; break;
                                            case 1: macro = "91/164/82/82/164/91"; break;
                                            case 2: macro = "91/164/44/44/164/91"; break;
                                            case 3: macro = dets[3] + "/" + dets[3]; break;
                                            case 4: macro = "91/164/71/71/164/91"; break;
                                        }
                                    }

                                    if (macro != "")
                                        PlayMacro(macroControl, macro, DS4Controls.None, DS4KeyType.None);

                                    firstTouch = false;
                                    action.firstTouch = false;
                                }
                                else if (secondtouchbegin) //if double tap
                                {
                                    if (action.typeID == SpecialAction.ActionTypeId.MultiAction)
                                    {
                                        macro = dets[2];
                                    }
                                    else if (int.TryParse(dets[2], out type))
                                    {
                                        switch (type)
                                        {
                                            case 0: macro = "91/71/71/91"; break;
                                            case 1: macro = "91/164/82/82/164/91"; break;
                                            case 2: macro = "91/164/44/44/164/91"; break;
                                            case 3: macro = dets[3] + "/" + dets[3]; break;
                                            case 4: macro = "91/164/71/71/164/91"; break;
                                        }
                                    }

                                    if (macro != "")
                                        PlayMacro(macroControl, macro, DS4Controls.None, DS4KeyType.None);

                                    secondtouchbegin = false;
                                    action.secondtouchbegin = false;
                                }
                            }
                            else
                            {
                                actionDone[index] = false;
                            }
                        }
                    }
                }
            }
            catch { return; }

            if (untriggeraction != null)
            {
                SpecialAction action = untriggeraction;
                int index = untriggerindex;
                bool utriggeractivated;

                if (!action.automaticUntrigger)
                {
                    // Untrigger keys defined and auto-untrigger (=unload) profile option is NOT set. Unload a temporary profile only when specified untrigger keys have been triggered.
                    utriggeractivated = true;

                    foreach (DS4Controls dc in action.uTrigger)
                    {
                        if (!getBoolSpecialActionMapping(dc, cState, eState, tp))
                        {
                            utriggeractivated = false;
                            break;
                        }
                    }
                }
                else
                {
                    // Untrigger as soon any of the defined regular trigger keys have been released. 
                    utriggeractivated = false;

                    foreach (DS4Controls dc in action.trigger)
                    {
                        if (!getBoolSpecialActionMapping(dc, cState, eState, tp))
                        {
                            utriggeractivated = true;
                            break;
                        }
                    }
                }

                if (utriggeractivated && action.typeID == SpecialAction.ActionTypeId.Profile)
                {
                    if ((action.controls == action.ucontrols && !actionDone[index]) || //if trigger and end trigger are the same
                    action.controls != action.ucontrols)
                    {
                        if (aux.UseTempProfile)
                        {
                            foreach (DS4Controls dc in action.uTrigger)
                            {
                                actionDone[index] = true;
                                DS4ControlSettings dcs = cfg.GetDS4CSetting(dc);
                                if (dcs.Norm.Action != null)
                                {
                                    if (dcs.Norm.ActionType == DS4ControlSettings.ActionType.Key)
                                        InputMethods.performKeyRelease((ushort)dcs.Norm.Action);
                                    else if (dcs.Norm.ActionType == DS4ControlSettings.ActionType.Macro)
                                    {
                                        int[] keys = (int[])dcs.Norm.Action;
                                        for (int j = 0, keysLen = keys.Length; j < keysLen; j++)
                                            InputMethods.performKeyRelease((ushort)keys[j]);
                                    }
                                }
                            }

                            string profileName = untriggeraction.prevProfileName;
                            string prolog = Properties.Resources.UsingProfile.Replace("*number*", (devIndex + 1).ToString()).Replace("*Profile name*", (profileName == string.Empty ? cfg.ProfilePath : profileName));
                            AppLogger.LogToGui(prolog, false);

                            untriggeraction = null;

                            if (profileName == string.Empty)
                                cfg.LoadProfile(false, ctrl); // Previous profile was a regular default profile of a controller
                            else
                                cfg.LoadTempProfile(profileName, true, ctrl); // Previous profile was a temporary profile, so re-load it as a temp profile
                        }
                    }
                }
                else
                {
                    actionDone[index] = false;
                }
            }
        }

        private async void PlayMacro(bool[] macrocontrol, string macro, DS4Controls control, DS4KeyType keyType)
        {
            if (macro.StartsWith("164/9/9/164") || macro.StartsWith("18/9/9/18"))
            {
                string[] skeys;
                int wait = 1000;
                if (!string.IsNullOrEmpty(macro))
                {
                    skeys = macro.Split('/');
                    ushort delay;
                    if (ushort.TryParse(skeys[skeys.Length - 1], out delay) && delay > 300)
                        wait = delay - 300;
                }
                AltTabSwapping(wait);
                if (control != DS4Controls.None)
                    macrodone[DS4ControltoInt(control)] = true;
            }
            else
            {
                string[] skeys;
                int[] keys;
                if (!string.IsNullOrEmpty(macro))
                {
                    skeys = macro.Split('/');
                    keys = new int[skeys.Length];
                }
                else
                {
                    skeys = new string[0];
                    keys = new int[0];
                }
                for (int i = 0; i < keys.Length; i++)
                    keys[i] = int.Parse(skeys[i]);
                bool[] keydown = new bool[286];
                if (control == DS4Controls.None || !macrodone[DS4ControltoInt(control)])
                {
                    if (control != DS4Controls.None)
                        macrodone[DS4ControltoInt(control)] = true;
                    foreach (int i in keys)
                    {
                        if (i >= 1000000000)
                        {
                            string lb = i.ToString().Substring(1);
                            if (i > 1000000000)
                            {
                                byte r = (byte)(int.Parse(lb[0].ToString()) * 100 + int.Parse(lb[1].ToString()) * 10 + int.Parse(lb[2].ToString()));
                                byte g = (byte)(int.Parse(lb[3].ToString()) * 100 + int.Parse(lb[4].ToString()) * 10 + int.Parse(lb[5].ToString()));
                                byte b = (byte)(int.Parse(lb[6].ToString()) * 100 + int.Parse(lb[7].ToString()) * 10 + int.Parse(lb[8].ToString()));
                                lightBar.forcedLight = true;
                                lightBar.forcedFlash = 0;
                                lightBar.forcedColor = new DS4Color(r, g, b);
                            }
                            else
                            {
                                lightBar.forcedFlash = 0;
                                lightBar.forcedLight = false;
                            }
                        }
                        else if (i >= 1000000)
                        {
                            DS4Device d = ctrl.DS4Controller;
                            string r = i.ToString().Substring(1);
                            byte heavy = (byte)(int.Parse(r[0].ToString()) * 100 + int.Parse(r[1].ToString()) * 10 + int.Parse(r[2].ToString()));
                            byte light = (byte)(int.Parse(r[3].ToString()) * 100 + int.Parse(r[4].ToString()) * 10 + int.Parse(r[5].ToString()));
                            d.setRumble(light, heavy);
                        }
                        else if (i >= 300) //ints over 300 used to delay
                            await Task.Delay(i - 300);
                        else if (!keydown[i])
                        {
                            if (i == 256) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTDOWN); //anything above 255 is not a keyvalue
                            else if (i == 257) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTDOWN);
                            else if (i == 258) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEDOWN);
                            else if (i == 259) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 1);
                            else if (i == 260) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 2);
                            else if (i == 261) { macroControl[0] = true; macroCount++; }
                            else if (i == 262) { macroControl[1] = true; macroCount++; }
                            else if (i == 263) { macroControl[2] = true; macroCount++; }
                            else if (i == 264) { macroControl[3] = true; macroCount++; }
                            else if (i == 265) { macroControl[4] = true; macroCount++; }
                            else if (i == 266) { macroControl[5] = true; macroCount++; }
                            else if (i == 267) { macroControl[6] = true; macroCount++; }
                            else if (i == 268) { macroControl[7] = true; macroCount++; }
                            else if (i == 269) { macroControl[8] = true; macroCount++; }
                            else if (i == 270) { macroControl[9] = true; macroCount++; }
                            else if (i == 271) { macroControl[10] = true; macroCount++; }
                            else if (i == 272) { macroControl[11] = true; macroCount++; }
                            else if (i == 273) { macroControl[12] = true; macroCount++; }
                            else if (i == 274) { macroControl[13] = true; macroCount++; }
                            else if (i == 275) { macroControl[14] = true; macroCount++; }
                            else if (i == 276) { macroControl[15] = true; macroCount++; }
                            else if (i == 277) { macroControl[16] = true; macroCount++; }
                            else if (i == 278) { macroControl[17] = true; macroCount++; }
                            else if (i == 279) { macroControl[18] = true; macroCount++; }
                            else if (i == 280) { macroControl[19] = true; macroCount++; }
                            else if (i == 281) { macroControl[20] = true; macroCount++; }
                            else if (i == 282) { macroControl[21] = true; macroCount++; }
                            else if (i == 283) { macroControl[22] = true; macroCount++; }
                            else if (i == 284) { macroControl[23] = true;macroCount++; }
                            else if (i == 285) { macroControl[24] = true; macroCount++; }
                            else if (keyType.HasFlag(DS4KeyType.ScanCode))
                                InputMethods.performSCKeyPress((ushort)i);
                            else
                                InputMethods.performKeyPress((ushort)i);
                            keydown[i] = true;
                        }
                        else
                        {
                            if (i == 256) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTUP); //anything above 255 is not a keyvalue
                            else if (i == 257) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTUP);
                            else if (i == 258) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEUP);
                            else if (i == 259) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 1);
                            else if (i == 260) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 2);
                            else if (i == 261) { macroControl[0] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 262) { macroControl[1] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 263) { macroControl[2] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 264) { macroControl[3] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 265) { macroControl[4] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 266) { macroControl[5] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 267) { macroControl[6] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 268) { macroControl[7] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 269) { macroControl[8] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 270) { macroControl[9] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 271) { macroControl[10] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 272) { macroControl[11] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 273) { macroControl[12] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 274) { macroControl[13] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 275) { macroControl[14] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 276) { macroControl[15] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 277) { macroControl[16] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 278) { macroControl[17] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 279) { macroControl[18] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 280) { macroControl[19] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 281) { macroControl[20] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 282) { macroControl[21] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 283) { macroControl[22] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 284) { macroControl[23] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 285) { macroControl[24] = false; if (macroCount > 0) macroCount--; }
                            else if (keyType.HasFlag(DS4KeyType.ScanCode))
                                InputMethods.performSCKeyRelease((ushort)i);
                            else
                                InputMethods.performKeyRelease((ushort)i);
                            keydown[i] = false;
                        }
                    }
                    for (int i = 0, arlength = keydown.Length; i < arlength; i++)
                    {
                        if (keydown[i])
                        {
                            if (i == 256) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTUP); //anything above 255 is not a keyvalue
                            else if (i == 257) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTUP);
                            else if (i == 258) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEUP);
                            else if (i == 259) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 1);
                            else if (i == 260) InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 2);
                            else if (i == 261) { macroControl[0] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 262) { macroControl[1] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 263) { macroControl[2] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 264) { macroControl[3] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 265) { macroControl[4] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 266) { macroControl[5] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 267) { macroControl[6] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 268) { macroControl[7] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 269) { macroControl[8] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 270) { macroControl[9] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 271) { macroControl[10] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 272) { macroControl[11] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 273) { macroControl[12] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 274) { macroControl[13] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 275) { macroControl[14] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 276) { macroControl[15] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 277) { macroControl[16] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 278) { macroControl[17] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 279) { macroControl[18] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 280) { macroControl[19] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 281) { macroControl[20] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 282) { macroControl[21] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 283) { macroControl[22] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 284) { macroControl[23] = false; if (macroCount > 0) macroCount--; }
                            else if (i == 285) { macroControl[24] = false; if (macroCount > 0) macroCount--; }
                            else if (keyType.HasFlag(DS4KeyType.ScanCode))
                                InputMethods.performSCKeyRelease((ushort)i);
                            else
                                InputMethods.performKeyRelease((ushort)i);
                        }
                    }

                    lightBar.forcedFlash = 0;
                    lightBar.forcedLight = false;
                    ctrl.DS4Controller.setRumble(0, 0);
                    if (keyType.HasFlag(DS4KeyType.HoldMacro))
                    {
                        await Task.Delay(50);
                        if (control != DS4Controls.None)
                            macrodone[DS4ControltoInt(control)] = false;
                    }
                }
            }
        }

        private void EndMacro(bool[] macrocontrol, string macro, DS4Controls control)
        {
            if ((macro.StartsWith("164/9/9/164") || macro.StartsWith("18/9/9/18")) && !altTabDone)
                AltTabSwappingRelease();

            if (control != DS4Controls.None)
                macrodone[DS4ControltoInt(control)] = false;
        }

        private void AltTabSwapping(int wait)
        {
            if (altTabDone)
            {
                altTabDone = false;
                InputMethods.performKeyPress(18);
            }
            else
            {
                altTabNow = DateTime.UtcNow;
                if (altTabNow >= oldAltTabNow + TimeSpan.FromMilliseconds(wait))
                {
                    oldAltTabNow = altTabNow;
                    InputMethods.performKeyPress(9);
                    InputMethods.performKeyRelease(9);
                }
            }
        }

        private static void AltTabSwappingRelease()
        {
            if (altTabNow < DateTime.UtcNow - TimeSpan.FromMilliseconds(10)) //in case multiple controls are mapped to alt+tab
            {
                altTabDone = true;
                InputMethods.performKeyRelease(9);
                InputMethods.performKeyRelease(18);
                altTabNow = DateTime.UtcNow;
                oldAltTabNow = DateTime.UtcNow - TimeSpan.FromDays(1);
            }
        }

        private void getMouseWheelMapping(DS4Controls control, DS4State cState,
            DS4StateExposed eState, Mouse tp, bool down)
        {
            DateTime now = DateTime.UtcNow;
            if (now >= oldnow + TimeSpan.FromMilliseconds(10) && !pressagain)
            {
                oldnow = now;
                InputMethods.MouseWheel((int)(getByteMapping(control, cState, eState, tp) / 8.0f * (down ? -1 : 1)), 0);
            }
        }

        private double getMouseMapping(DS4Controls control, DS4State cState, DS4StateExposed eState, int mnum)
        {
            int controlnum = DS4ControltoInt(control);

            int deadzoneL = 0;
            int deadzoneR = 0;
            if (cfg.LS.DeadZone == 0)
                deadzoneL = 3;
            if (cfg.RS.DeadZone == 0)
                deadzoneR = 3;

            double value = 0.0;
            int speed = cfg.ButtonMouseSensitivity;
            double root = 1.002;
            double divide = 10000d;
            //DateTime now = mousenow[mnum];

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[controlNum];
            //long timeElapsed = ctrl.DS4Controller.getLastTimeElapsed();
            double timeElapsed = ctrl.DS4Controller.lastTimeElapsedDouble;
            //double mouseOffset = 0.025;
            double tempMouseOffsetX = 0.0, tempMouseOffsetY = 0.0;

            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                bool active = fieldMapping.buttons[controlNum];
                value = (active ? Math.Pow(root + speed / divide, 100) - 1 : 0);
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg:
                    {
                        if (cState.LX < 128 - deadzoneL)
                        {
                            double diff = -(cState.LX - 128 - deadzoneL) / (double)(0 - 128 - deadzoneL);
                            //tempMouseOffsetX = Math.Abs(Math.Cos(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                            //tempMouseOffsetX = MOUSESTICKOFFSET;
                            tempMouseOffsetX = cState.LXUnit * MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + (tempMouseOffsetX * -1.0);
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = -(cState.LX - 127 - deadzoneL) / 2550d * speed;
                        }

                        break;
                    }
                    case DS4Controls.LXPos:
                    {
                        if (cState.LX > 128 + deadzoneL)
                        {
                            double diff = (cState.LX - 128 + deadzoneL) / (double)(255 - 128 + deadzoneL);
                            tempMouseOffsetX = cState.LXUnit * MOUSESTICKOFFSET;
                            //tempMouseOffsetX = Math.Abs(Math.Cos(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                            //tempMouseOffsetX = MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + tempMouseOffsetX;
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = (cState.LX - 127 + deadzoneL) / 2550d * speed;
                        }

                        break;
                    }
                    case DS4Controls.RXNeg:
                    {
                        if (cState.RX < 128 - deadzoneR)
                        {
                            double diff = -(cState.RX - 128 - deadzoneR) / (double)(0 - 128 - deadzoneR);
                            tempMouseOffsetX = cState.RXUnit * MOUSESTICKOFFSET;
                            //tempMouseOffsetX = MOUSESTICKOFFSET;
                            //tempMouseOffsetX = Math.Abs(Math.Cos(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + (tempMouseOffsetX * -1.0);
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = -(cState.RX - 127 - deadzoneR) / 2550d * speed;
                        }

                        break;
                    }
                    case DS4Controls.RXPos:
                    {
                        if (cState.RX > 128 + deadzoneR)
                        {
                            double diff = (cState.RX - 128 + deadzoneR) / (double)(255 - 128 + deadzoneR);
                            tempMouseOffsetX = cState.RXUnit * MOUSESTICKOFFSET;
                            //tempMouseOffsetX = MOUSESTICKOFFSET;
                            //tempMouseOffsetX = Math.Abs(Math.Cos(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + tempMouseOffsetX;
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = (cState.RX - 127 + deadzoneR) / 2550d * speed;
                        }

                        break;
                    }
                    case DS4Controls.LYNeg:
                    {
                        if (cState.LY < 128 - deadzoneL)
                        {
                            double diff = -(cState.LY - 128 - deadzoneL) / (double)(0 - 128 - deadzoneL);
                            tempMouseOffsetY = cState.LYUnit * MOUSESTICKOFFSET;
                            //tempMouseOffsetY = MOUSESTICKOFFSET;
                            //tempMouseOffsetY = Math.Abs(Math.Sin(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + (tempMouseOffsetY * -1.0);
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = -(cState.LY - 127 - deadzoneL) / 2550d * speed;
                        }

                        break;
                    }
                    case DS4Controls.LYPos:
                    {
                        if (cState.LY > 128 + deadzoneL)
                        {
                            double diff = (cState.LY - 128 + deadzoneL) / (double)(255 - 128 + deadzoneL);
                            tempMouseOffsetY = cState.LYUnit * MOUSESTICKOFFSET;
                            //tempMouseOffsetY = MOUSESTICKOFFSET;
                            //tempMouseOffsetY = Math.Abs(Math.Sin(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + tempMouseOffsetY;
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = (cState.LY - 127 + deadzoneL) / 2550d * speed;
                        }

                        break;
                    }
                    case DS4Controls.RYNeg:
                    {
                        if (cState.RY < 128 - deadzoneR)
                        {
                            double diff = -(cState.RY - 128 - deadzoneR) / (double)(0 - 128 - deadzoneR);
                            tempMouseOffsetY = cState.RYUnit * MOUSESTICKOFFSET;
                            //tempMouseOffsetY = MOUSESTICKOFFSET;
                            //tempMouseOffsetY = Math.Abs(Math.Sin(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + (tempMouseOffsetY * -1.0);
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = -(cState.RY - 127 - deadzoneR) / 2550d * speed;
                        }

                        break;
                    }
                    case DS4Controls.RYPos:
                    {
                        if (cState.RY > 128 + deadzoneR)
                        {
                            double diff = (cState.RY - 128 + deadzoneR) / (double)(255 - 128 + deadzoneR);
                            tempMouseOffsetY = cState.RYUnit * MOUSESTICKOFFSET;
                            //tempMouseOffsetY = MOUSESTICKOFFSET;
                            //tempMouseOffsetY = Math.Abs(Math.Sin(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                            value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + tempMouseOffsetY;
                            //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                            //value = (cState.RY - 127 + deadzoneR) / 2550d * speed;
                        }

                        break;
                    }

                    default: break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                byte trigger = fieldMapping.triggers[controlNum];
                value = Math.Pow(root + speed / divide, trigger / 2d) - 1;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                //double SXD = getSXDeadzone(device);
                //double SZD = getSZDeadzone(device);

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        int gyroX = fieldMapping.gyrodirs[controlNum];
                        value = (byte)(gyroX > 0 ? Math.Pow(root + speed / divide, gyroX) : 0);
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        int gyroX = fieldMapping.gyrodirs[controlNum];
                        value = (byte)(gyroX < 0 ? Math.Pow(root + speed / divide, -gyroX) : 0);
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        int gyroZ = fieldMapping.gyrodirs[controlNum];
                        value = (byte)(gyroZ > 0 ? Math.Pow(root + speed / divide, gyroZ) : 0);
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        int gyroZ = fieldMapping.gyrodirs[controlNum];
                        value = (byte)(gyroZ < 0 ? Math.Pow(root + speed / divide, -gyroZ) : 0);
                        break;
                    }
                    default: break;
                }
            }

            if (cfg.MouseAccel)
            {
                if (value > 0)
                {
                    mcounter = 34;
                    mouseaccel++;
                }

                if (mouseaccel == prevmouseaccel)
                {
                    mcounter--;
                }

                if (mcounter <= 0)
                {
                    mouseaccel = 0;
                    mcounter = 34;
                }

                value *= 1 + Math.Min(20000, (mouseaccel)) / 10000d;
                prevmouseaccel = mouseaccel;
            }

            return value;
        }

        private static void calculateFinalMouseMovement(ref double rawMouseX, ref double rawMouseY,
            out int mouseX, out int mouseY)
        {
            if ((rawMouseX > 0.0 && horizontalRemainder > 0.0) || (rawMouseX < 0.0 && horizontalRemainder < 0.0))
            {
                rawMouseX += horizontalRemainder;
            }
            else
            {
                horizontalRemainder = 0.0;
            }

            //double mouseXTemp = rawMouseX - (Math.IEEERemainder(rawMouseX * 1000.0, 1.0) / 1000.0);
            double mouseXTemp = rawMouseX - (remainderCutoff(rawMouseX * 1000.0, 1.0) / 1000.0);
            //double mouseXTemp = rawMouseX - (rawMouseX * 1000.0 - (1.0 * (int)(rawMouseX * 1000.0 / 1.0)));
            mouseX = (int)mouseXTemp;
            horizontalRemainder = mouseXTemp - mouseX;
            //mouseX = (int)rawMouseX;
            //horizontalRemainder = rawMouseX - mouseX;

            if ((rawMouseY > 0.0 && verticalRemainder > 0.0) || (rawMouseY < 0.0 && verticalRemainder < 0.0))
            {
                rawMouseY += verticalRemainder;
            }
            else
            {
                verticalRemainder = 0.0;
            }

            //double mouseYTemp = rawMouseY - (Math.IEEERemainder(rawMouseY * 1000.0, 1.0) / 1000.0);
            double mouseYTemp = rawMouseY - (remainderCutoff(rawMouseY * 1000.0, 1.0) / 1000.0);
            mouseY = (int)mouseYTemp;
            verticalRemainder = mouseYTemp - mouseY;
            //mouseY = (int)rawMouseY;
            //verticalRemainder = rawMouseY - mouseY;
        }

        private static double remainderCutoff(double dividend, double divisor)
        {
            return dividend - (divisor * (int)(dividend / divisor));
        }

        public static bool compare(byte b1, byte b2)
        {
            bool result = true;
            if (Math.Abs(b1 - b2) > 10)
            {
                result = false;
            }

            return result;
        }

        private byte getByteMapping2(DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            byte result = 0;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = (byte)(fieldMapping.buttons[controlNum] ? 255 : 0);
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMapping.axisdirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    case DS4Controls.LYNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RXNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RYNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    default: result = (byte)(axisValue - 128.0f < 0 ? 0 : (axisValue - 128.0f) * 2.0078740157480315f); break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMapping.triggers[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = (byte)(tp != null && fieldMapping.buttons[controlNum] ? 255 : 0);
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = (byte)(tp != null ? fieldMapping.swipedirs[controlNum] : 0);
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = cfg.UseSAforMouse;

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        int gyroX = fieldMapping.gyrodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        int gyroX = fieldMapping.gyrodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, -gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        int gyroZ = fieldMapping.gyrodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, gyroZ * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        int gyroZ = fieldMapping.gyrodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, -gyroZ * 2) : 0);
                        break;
                    }
                    default: break;
                }
            }

            return result;
        }

        private byte getByteMapping(DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            byte result = 0;

            if (control >= DS4Controls.Square && control <= DS4Controls.Cross)
            {
                switch (control)
                {
                    case DS4Controls.Cross: result = (byte)(cState.Cross ? 255 : 0); break;
                    case DS4Controls.Square: result = (byte)(cState.Square ? 255 : 0); break;
                    case DS4Controls.Triangle: result = (byte)(cState.Triangle ? 255 : 0); break;
                    case DS4Controls.Circle: result = (byte)(cState.Circle ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.L1 && control <= DS4Controls.R3)
            {
                switch (control)
                {
                    case DS4Controls.L1: result = (byte)(cState.L1 ? 255 : 0); break;
                    case DS4Controls.L2: result = cState.L2; break;
                    case DS4Controls.L3: result = (byte)(cState.L3 ? 255 : 0); break;
                    case DS4Controls.R1: result = (byte)(cState.R1 ? 255 : 0); break;
                    case DS4Controls.R2: result = cState.R2; break;
                    case DS4Controls.R3: result = (byte)(cState.R3 ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.DpadUp && control <= DS4Controls.DpadLeft)
            {
                switch (control)
                {
                    case DS4Controls.DpadUp: result = (byte)(cState.DpadUp ? 255 : 0); break;
                    case DS4Controls.DpadDown: result = (byte)(cState.DpadDown ? 255 : 0); break;
                    case DS4Controls.DpadLeft: result = (byte)(cState.DpadLeft ? 255 : 0); break;
                    case DS4Controls.DpadRight: result = (byte)(cState.DpadRight ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.LXNeg && control <= DS4Controls.RYPos)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg: result = (byte)(cState.LX - 128.0f >= 0 ? 0 : -(cState.LX - 128.0f) * 1.9921875f); break;
                    case DS4Controls.LYNeg: result = (byte)(cState.LY - 128.0f >= 0 ? 0 : -(cState.LY - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RXNeg: result = (byte)(cState.RX - 128.0f >= 0 ? 0 : -(cState.RX - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RYNeg: result = (byte)(cState.RY - 128.0f >= 0 ? 0 : -(cState.RY - 128.0f) * 1.9921875f); break;
                    case DS4Controls.LXPos: result = (byte)(cState.LX - 128.0f < 0 ? 0 : (cState.LX - 128.0f) * 2.0078740157480315f); break;
                    case DS4Controls.LYPos: result = (byte)(cState.LY - 128.0f < 0 ? 0 : (cState.LY - 128.0f) * 2.0078740157480315f); break;
                    case DS4Controls.RXPos: result = (byte)(cState.RX - 128.0f < 0 ? 0 : (cState.RX - 128.0f) * 2.0078740157480315f); break;
                    case DS4Controls.RYPos: result = (byte)(cState.RY - 128.0f < 0 ? 0 : (cState.RY - 128.0f) * 2.0078740157480315f); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.TouchLeft && control <= DS4Controls.TouchRight)
            {
                switch (control)
                {
                    case DS4Controls.TouchLeft: result = (byte)(tp != null && tp.leftDown ? 255 : 0); break;
                    case DS4Controls.TouchRight: result = (byte)(tp != null && tp.rightDown ? 255 : 0); break;
                    case DS4Controls.TouchMulti: result = (byte)(tp != null && tp.multiDown ? 255 : 0); break;
                    case DS4Controls.TouchUpper: result = (byte)(tp != null && tp.upperDown ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.SwipeLeft && control <= DS4Controls.SwipeDown)
            {
                switch (control)
                {
                    case DS4Controls.SwipeUp: result = (byte)(tp != null ? tp.swipeUpB : 0); break;
                    case DS4Controls.SwipeDown: result = (byte)(tp != null ? tp.swipeDownB : 0); break;
                    case DS4Controls.SwipeLeft: result = (byte)(tp != null ? tp.swipeLeftB : 0); break;
                    case DS4Controls.SwipeRight: result = (byte)(tp != null ? tp.swipeRightB : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.GyroXPos && control <= DS4Controls.GyroZNeg)
            {
                double SXD = cfg.SX.DeadZone;
                double SZD = cfg.SZ.DeadZone;
                bool sOff = cfg.UseSAforMouse;
                double sxsens = cfg.SX.Sensitivity;
                double szsens = cfg.SZ.Sensitivity;

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        int gyroX = -eState.AccelX;
                        result = (byte)(!sOff && sxsens * gyroX > SXD * 10 ? Math.Min(255, sxsens * gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        int gyroX = -eState.AccelX;
                        result = (byte)(!sOff && sxsens * gyroX < -SXD * 10 ? Math.Min(255, sxsens * -gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        int gyroZ = eState.AccelZ;
                        result = (byte)(!sOff && szsens * gyroZ > SZD * 10 ? Math.Min(255, szsens * gyroZ * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        int gyroZ = eState.AccelZ;
                        result = (byte)(!sOff && szsens * gyroZ < -SZD * 10 ? Math.Min(255, szsens * -gyroZ * 2) : 0);
                        break;
                    }
                    default: break;
                }
            }
            else
            {
                switch (control)
                {
                    case DS4Controls.Share: result = (byte)(cState.Share ? 255 : 0); break;
                    case DS4Controls.Options: result = (byte)(cState.Options ? 255 : 0); break;
                    case DS4Controls.PS: result = (byte)(cState.PS ? 255 : 0); break;
                    default: break;
                }
            }

            return result;
        }

        /* TODO: Possibly remove usage of this version of the method */
        public bool getBoolMapping(DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            bool result = false;

            if (control >= DS4Controls.Square && control <= DS4Controls.Cross)
            {
                switch (control)
                {
                    case DS4Controls.Cross: result = cState.Cross; break;
                    case DS4Controls.Square: result = cState.Square; break;
                    case DS4Controls.Triangle: result = cState.Triangle; break;
                    case DS4Controls.Circle: result = cState.Circle; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.L1 && control <= DS4Controls.R3)
            {
                switch (control)
                {
                    case DS4Controls.L1: result = cState.L1; break;
                    case DS4Controls.R1: result = cState.R1; break;
                    case DS4Controls.L2: result = cState.L2 > 100; break;
                    case DS4Controls.R2: result = cState.R2 > 100; break;
                    case DS4Controls.L3: result = cState.L3; break;
                    case DS4Controls.R3: result = cState.R3; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.DpadUp && control <= DS4Controls.DpadLeft)
            {
                switch (control)
                {
                    case DS4Controls.DpadUp: result = cState.DpadUp; break;
                    case DS4Controls.DpadDown: result = cState.DpadDown; break;
                    case DS4Controls.DpadLeft: result = cState.DpadLeft; break;
                    case DS4Controls.DpadRight: result = cState.DpadRight; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.LXNeg && control <= DS4Controls.RYPos)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg: result = cState.LX < 128 - 55; break;
                    case DS4Controls.LYNeg: result = cState.LY < 128 - 55; break;
                    case DS4Controls.RXNeg: result = cState.RX < 128 - 55; break;
                    case DS4Controls.RYNeg: result = cState.RY < 128 - 55; break;
                    case DS4Controls.LXPos: result = cState.LX > 128 + 55; break;
                    case DS4Controls.LYPos: result = cState.LY > 128 + 55; break;
                    case DS4Controls.RXPos: result = cState.RX > 128 + 55; break;
                    case DS4Controls.RYPos: result = cState.RY > 128 + 55; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.TouchLeft && control <= DS4Controls.TouchRight)
            {
                switch (control)
                {
                    case DS4Controls.TouchLeft: result = (tp != null ? tp.leftDown : false); break;
                    case DS4Controls.TouchRight: result = (tp != null ? tp.rightDown : false); break;
                    case DS4Controls.TouchMulti: result = (tp != null ? tp.multiDown : false); break;
                    case DS4Controls.TouchUpper: result = (tp != null ? tp.upperDown : false); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.SwipeLeft && control <= DS4Controls.SwipeDown)
            {
                switch (control)
                {
                    case DS4Controls.SwipeUp: result = (tp != null && tp.swipeUp); break;
                    case DS4Controls.SwipeDown: result = (tp != null && tp.swipeDown); break;
                    case DS4Controls.SwipeLeft: result = (tp != null && tp.swipeLeft); break;
                    case DS4Controls.SwipeRight: result = (tp != null && tp.swipeRight); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.GyroXPos && control <= DS4Controls.GyroZNeg)
            {
                bool sOff = cfg.UseSAforMouse;

                switch (control)
                {
                    case DS4Controls.GyroXPos: result = !sOff ? cfg.SX.Sensitivity * -eState.AccelX > 67 : false; break;
                    case DS4Controls.GyroXNeg: result = !sOff ? cfg.SX.Sensitivity * -eState.AccelX < -67 : false; break;
                    case DS4Controls.GyroZPos: result = !sOff ? cfg.SZ.Sensitivity * eState.AccelZ > 67 : false; break;
                    case DS4Controls.GyroZNeg: result = !sOff ? cfg.SZ.Sensitivity * eState.AccelZ < -67 : false; break;
                    default: break;
                }
            }
            else
            {
                switch (control)
                {
                    case DS4Controls.PS: result = cState.PS; break;
                    case DS4Controls.Share: result = cState.Share; break;
                    case DS4Controls.Options: result = cState.Options; break;
                    default: break;
                }
            }

            return result;
        }

        private bool getBoolMapping2(DS4Controls control,
            DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            bool result = false;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMapping.buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMapping.axisdirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: result = cState.LX < 128 - 55; break;
                    case DS4Controls.LYNeg: result = cState.LY < 128 - 55; break;
                    case DS4Controls.RXNeg: result = cState.RX < 128 - 55; break;
                    case DS4Controls.RYNeg: result = cState.RY < 128 - 55; break;
                    default: result = axisValue > 128 + 55; break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMapping.triggers[controlNum] > 100;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMapping.buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = fieldMapping.swipedirbools[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = cfg.UseSAforMouse;
                bool safeTest = false;

                switch (control)
                {
                    case DS4Controls.GyroXPos: safeTest = fieldMapping.gyrodirs[controlNum] > 0; break;
                    case DS4Controls.GyroXNeg: safeTest = fieldMapping.gyrodirs[controlNum] < -0; break;
                    case DS4Controls.GyroZPos: safeTest = fieldMapping.gyrodirs[controlNum] > 0; break;
                    case DS4Controls.GyroZNeg: safeTest = fieldMapping.gyrodirs[controlNum] < -0; break;
                    default: break;
                }

                result = sOff == false ? safeTest : false;
            }

            return result;
        }

        private bool getBoolSpecialActionMapping(DS4Controls control,
            DS4State cState, DS4StateExposed eState, Mouse tp, DS4StateFieldMapping fieldMapping = null)
        {
            if (fieldMapping == null) fieldMapping = this.fieldMapping;
            bool result = false;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMapping.buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMapping.axisdirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: result = cState.LX < 128 - 55; break;
                    case DS4Controls.LYNeg: result = cState.LY < 128 - 55; break;
                    case DS4Controls.RXNeg: result = cState.RX < 128 - 55; break;
                    case DS4Controls.RYNeg: result = cState.RY < 128 - 55; break;
                    default: result = axisValue > 128 + 55; break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMapping.triggers[controlNum] > 100;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMapping.buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = fieldMapping.swipedirbools[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = cfg.UseSAforMouse;
                bool safeTest = false;

                switch (control)
                {
                    case DS4Controls.GyroXPos: safeTest = fieldMapping.gyrodirs[controlNum] > 67; break;
                    case DS4Controls.GyroXNeg: safeTest = fieldMapping.gyrodirs[controlNum] < -67; break;
                    case DS4Controls.GyroZPos: safeTest = fieldMapping.gyrodirs[controlNum] > 67; break;
                    case DS4Controls.GyroZNeg: safeTest = fieldMapping.gyrodirs[controlNum] < -67; break;
                    default: break;
                }

                result = sOff == false ? safeTest : false;
            }

            return result;
        }

        private bool getBoolActionMapping2(DS4Controls control,
            DS4State cState, DS4StateExposed eState, Mouse tp, bool analog = false)
        {
            bool result = false;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMapping.buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LX < 128 && (angle >= 112.5 && angle <= 247.5);
                        break;
                    }
                    case DS4Controls.LYNeg:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LY < 128 && (angle >= 22.5 && angle <= 157.5);
                        break;
                    }
                    case DS4Controls.RXNeg:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RX < 128 && (angle >= 112.5 && angle <= 247.5);
                        break;
                    }
                    case DS4Controls.RYNeg:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RY < 128 && (angle >= 22.5 && angle <= 157.5);
                        break;
                    }
                    case DS4Controls.LXPos:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LX > 128 && (angle <= 67.5 || angle >= 292.5);
                        break;
                    }
                    case DS4Controls.LYPos:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LY > 128 && (angle >= 202.5 && angle <= 337.5);
                        break;
                    }
                    case DS4Controls.RXPos:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RX > 128 && (angle <= 67.5 || angle >= 292.5);
                        break;
                    }
                    case DS4Controls.RYPos:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RY > 128 && (angle >= 202.5 && angle <= 337.5);
                        break;
                    }
                    default: break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMapping.triggers[controlNum] > 0;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMapping.buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = fieldMapping.swipedirbools[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = cfg.UseSAforMouse;
                bool safeTest = false;

                switch (control)
                {
                    case DS4Controls.GyroXPos: safeTest = fieldMapping.gyrodirs[controlNum] > 0; break;
                    case DS4Controls.GyroXNeg: safeTest = fieldMapping.gyrodirs[controlNum] < 0; break;
                    case DS4Controls.GyroZPos: safeTest = fieldMapping.gyrodirs[controlNum] > 0; break;
                    case DS4Controls.GyroZNeg: safeTest = fieldMapping.gyrodirs[controlNum] < 0; break;
                    default: break;
                }

                result = sOff == false ? safeTest : false;
            }

            return result;
        }

        public static bool getBoolButtonMapping(bool stateButton)
        {
            return stateButton;
        }

        public static bool getBoolAxisDirMapping(byte stateAxis, bool positive)
        {
            return positive ? stateAxis > 128 + 55 : stateAxis < 128 - 55;
        }

        public static bool getBoolTriggerMapping(byte stateAxis)
        {
            return stateAxis > 100;
        }

        public static bool getBoolTouchMapping(bool touchButton)
        {
            return touchButton;
        }

        private byte getXYAxisMapping2(DS4Controls control, DS4State cState,
            DS4StateExposed eState, Mouse tp, bool alt = false)
        {
            const byte falseVal = 128;
            byte result = 0;
            byte trueVal = 0;

            if (alt)
                trueVal = 255;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[controlNum];

            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMapping.buttons[controlNum] ? trueVal : falseVal;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMapping.axisdirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    case DS4Controls.LYNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    case DS4Controls.RXNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    case DS4Controls.RYNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    default: if (!alt) result = axisValue > falseVal ? (byte)(255 - axisValue) : falseVal; else result = axisValue > falseVal ? axisValue : falseVal; break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                if (alt)
                {
                    result = (byte)(128.0f + fieldMapping.triggers[controlNum] / 2.0078740157480315f);
                }
                else
                {
                    result = (byte)(128.0f - fieldMapping.triggers[controlNum] / 2.0078740157480315f);
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMapping.buttons[controlNum] ? trueVal : falseVal;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                if (alt)
                {
                    result = (byte)(tp != null ? 127.5f + fieldMapping.swipedirs[controlNum] / 2f : 0);
                }
                else
                {
                    result = (byte)(tp != null ? 127.5f - fieldMapping.swipedirs[controlNum] / 2f : 0);
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir) {
                bool sOff = cfg.UseSAforMouse;

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        if (sOff == false && fieldMapping.gyrodirs[controlNum] > 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + fieldMapping.gyrodirs[controlNum]); else result = (byte)Math.Max(0, 127 - fieldMapping.gyrodirs[controlNum]);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        if (sOff == false && fieldMapping.gyrodirs[controlNum] < 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + -fieldMapping.gyrodirs[controlNum]); else result = (byte)Math.Max(0, 127 - -fieldMapping.gyrodirs[controlNum]);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        if (sOff == false && fieldMapping.gyrodirs[controlNum] > 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + fieldMapping.gyrodirs[controlNum]); else result = (byte)Math.Max(0, 127 - fieldMapping.gyrodirs[controlNum]);
                        }
                        else return falseVal;
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        if (sOff == false && fieldMapping.gyrodirs[controlNum] < 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + -fieldMapping.gyrodirs[controlNum]); else result = (byte)Math.Max(0, 127 - -fieldMapping.gyrodirs[controlNum]);
                        }
                        else result = falseVal;
                        break;
                    }
                    default: break;
                }
            }

            return result;
        }

        /* TODO: Possibly remove usage of this version of the method */
        public byte getXYAxisMapping(DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp, bool alt = false)
        {
            byte result = 0;
            byte trueVal = 0;
            byte falseVal = 127;

            if (alt)
                trueVal = 255;

            if (control >= DS4Controls.Square && control <= DS4Controls.Cross)
            {
                switch (control)
                {
                    case DS4Controls.Cross: result = (byte)(cState.Cross ? trueVal : falseVal); break;
                    case DS4Controls.Square: result = (byte)(cState.Square ? trueVal : falseVal); break;
                    case DS4Controls.Triangle: result = (byte)(cState.Triangle ? trueVal : falseVal); break;
                    case DS4Controls.Circle: result = (byte)(cState.Circle ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.L1 && control <= DS4Controls.R3)
            {
                switch (control)
                {
                    case DS4Controls.L1: result = (byte)(cState.L1 ? trueVal : falseVal); break;
                    case DS4Controls.L2: if (alt) result = (byte)(128.0f + cState.L2 / 2.0078740157480315f); else result = (byte)(128.0f - cState.L2 / 2.0078740157480315f); break;
                    case DS4Controls.L3: result = (byte)(cState.L3 ? trueVal : falseVal); break;
                    case DS4Controls.R1: result = (byte)(cState.R1 ? trueVal : falseVal); break;
                    case DS4Controls.R2: if (alt) result = (byte)(128.0f + cState.R2 / 2.0078740157480315f); else result = (byte)(128.0f - cState.R2 / 2.0078740157480315f); break;
                    case DS4Controls.R3: result = (byte)(cState.R3 ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.DpadUp && control <= DS4Controls.DpadLeft)
            {
                switch (control)
                {
                    case DS4Controls.DpadUp: result = (byte)(cState.DpadUp ? trueVal : falseVal); break;
                    case DS4Controls.DpadDown: result = (byte)(cState.DpadDown ? trueVal : falseVal); break;
                    case DS4Controls.DpadLeft: result = (byte)(cState.DpadLeft ? trueVal : falseVal); break;
                    case DS4Controls.DpadRight: result = (byte)(cState.DpadRight ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.LXNeg && control <= DS4Controls.RYPos)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg: if (!alt) result = cState.LX; else result = (byte)(255 - cState.LX); break;
                    case DS4Controls.LYNeg: if (!alt) result = cState.LY; else result = (byte)(255 - cState.LY); break;
                    case DS4Controls.RXNeg: if (!alt) result = cState.RX; else result = (byte)(255 - cState.RX); break;
                    case DS4Controls.RYNeg: if (!alt) result = cState.RY; else result = (byte)(255 - cState.RY); break;
                    case DS4Controls.LXPos: if (!alt) result = (byte)(255 - cState.LX); else result = cState.LX; break;
                    case DS4Controls.LYPos: if (!alt) result = (byte)(255 - cState.LY); else result = cState.LY; break;
                    case DS4Controls.RXPos: if (!alt) result = (byte)(255 - cState.RX); else result = cState.RX; break;
                    case DS4Controls.RYPos: if (!alt) result = (byte)(255 - cState.RY); else result = cState.RY; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.TouchLeft && control <= DS4Controls.TouchRight)
            {
                switch (control)
                {
                    case DS4Controls.TouchLeft: result = (byte)(tp != null && tp.leftDown ? trueVal : falseVal); break;
                    case DS4Controls.TouchRight: result = (byte)(tp != null && tp.rightDown ? trueVal : falseVal); break;
                    case DS4Controls.TouchMulti: result = (byte)(tp != null && tp.multiDown ? trueVal : falseVal); break;
                    case DS4Controls.TouchUpper: result = (byte)(tp != null && tp.upperDown ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.SwipeLeft && control <= DS4Controls.SwipeDown)
            {
                switch (control)
                {
                    case DS4Controls.SwipeUp: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeUpB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeUpB / 2f : 0); break;
                    case DS4Controls.SwipeDown: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeDownB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeDownB / 2f : 0); break;
                    case DS4Controls.SwipeLeft: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeLeftB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeLeftB / 2f : 0); break;
                    case DS4Controls.SwipeRight: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeRightB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeRightB / 2f : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.GyroXPos && control <= DS4Controls.GyroZNeg)
            {
                double SXSens = cfg.SX.Sensitivity;
                double SZSens = cfg.SZ.Sensitivity;
                double SXD = cfg.SX.DeadZone;
                double SZD = cfg.SZ.DeadZone;
                bool sOff = cfg.UseSAforMouse;

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        if (!sOff && -eState.AccelX > SXD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SXSens * -eState.AccelX); else result = (byte)Math.Max(0, 127 - SXSens * -eState.AccelX);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        if (!sOff && -eState.AccelX < -SXD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SXSens * eState.AccelX); else result = (byte)Math.Max(0, 127 - SXSens * eState.AccelX);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        if (!sOff && eState.AccelZ > SZD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SZSens * eState.AccelZ); else result = (byte)Math.Max(0, 127 - SZSens * eState.AccelZ);
                        }
                        else return falseVal;
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        if (!sOff && eState.AccelZ < -SZD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SZSens * -eState.AccelZ); else result = (byte)Math.Max(0, 127 - SZSens * -eState.AccelZ);
                        }
                        else result = falseVal;
                        break;
                    }
                    default: break;
                }
            }
            else
            {
                switch (control)
                {
                    case DS4Controls.Share: result = (byte)(cState.Share ? trueVal : falseVal); break;
                    case DS4Controls.Options: result = (byte)(cState.Options ? trueVal : falseVal); break;
                    case DS4Controls.PS: result = (byte)(cState.PS ? trueVal : falseVal); break;
                    default: break;
                }
            }

            return result;
        }

        private void resetToDefaultValue2(DS4Controls control, DS4State cState)
        {
            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.mappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                fieldMapping.buttons[controlNum] = false;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                fieldMapping.axisdirs[controlNum] = 128;
                int controlRelation = (controlNum % 2 == 0 ? controlNum - 1 : controlNum + 1);
                fieldMapping.axisdirs[controlRelation] = 128;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                fieldMapping.triggers[controlNum] = 0;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                fieldMapping.buttons[controlNum] = false;
            }
        }


        // SA steering wheel emulation mapping

        private const int C_WHEEL_ANGLE_PRECISION = 10; // Precision of SA angle in 1/10 of degrees
        
        private static readonly DS4Color calibrationColor_0 = new DS4Color { red = 0xA0, green = 0x00, blue = 0x00 };
        private static readonly DS4Color calibrationColor_1 = new DS4Color { red = 0xFF, green = 0xFF, blue = 0x00 };
        private static readonly DS4Color calibrationColor_2 = new DS4Color { red = 0x00, green = 0x50, blue = 0x50 };
        private static readonly DS4Color calibrationColor_3 = new DS4Color { red = 0x00, green = 0xC0, blue = 0x00 };

        private static DateTime latestDebugMsgTime;
        private static string latestDebugData;
        private static void LogToGuiSACalibrationDebugMsg(string data, bool forceOutput = false)
        {
            // Print debug calibration log messages only once per 2 secs to avoid flooding the log receiver
            DateTime curTime = DateTime.Now;
            if (forceOutput || ((TimeSpan)(curTime - latestDebugMsgTime)).TotalSeconds > 2)
            {
                latestDebugMsgTime = curTime;
                if (data != latestDebugData)
                {
                    AppLogger.LogToGui(data, false);
                    latestDebugData = data;
                }
            }
        }

        // Return number of bits set in a value
        protected static int CountNumOfSetBits(int bitValue)
        {
            int count = 0;
            while (bitValue != 0)
            {
                count++;
                bitValue &= (bitValue - 1);
            }
            return count;
        }

        // Calculate and return the angle of the controller as -180...0...+180 value.
        private static Int32 CalculateControllerAngle(int gyroAccelX, int gyroAccelZ, DS4Device controller)
        {
            Int32 result;

            if (gyroAccelX == controller.wheelCenterPoint.X && Math.Abs(gyroAccelZ - controller.wheelCenterPoint.Y) <= 1)
            {
                // When the current gyro position is "close enough" the wheel center point then no need to go through the hassle of calculating an angle
                result = 0;
            }
            else
            {
                // Calculate two vectors based on "circle center" (ie. circle represents the 360 degree wheel turn and wheelCenterPoint and currentPosition vectors both start from circle center).
                // To improve accuracy both left and right turns use a decicated calibration "circle" because DS4 gyro and DoItYourselfWheelRig may return slightly different SA sensor values depending on the tilt direction (well, only one or two degree difference so nothing major).
                Point vectorAB;
                Point vectorCD;

                if (gyroAccelX >= controller.wheelCenterPoint.X)
                {
                    // "DS4 gyro wheel" tilted to right
                    vectorAB = new Point(controller.wheelCenterPoint.X - controller.wheelCircleCenterPointRight.X, controller.wheelCenterPoint.Y - controller.wheelCircleCenterPointRight.Y);
                    vectorCD = new Point(gyroAccelX - controller.wheelCircleCenterPointRight.X, gyroAccelZ - controller.wheelCircleCenterPointRight.Y);
                }
                else
                {
                    // "DS4 gyro wheel" tilted to left
                    vectorAB = new Point(controller.wheelCenterPoint.X - controller.wheelCircleCenterPointLeft.X, controller.wheelCenterPoint.Y - controller.wheelCircleCenterPointLeft.Y);
                    vectorCD = new Point(gyroAccelX - controller.wheelCircleCenterPointLeft.X, gyroAccelZ - controller.wheelCircleCenterPointLeft.Y);
                }

                // Calculate dot product and magnitude of vectors (center vector and the current tilt vector)
                double dotProduct = vectorAB.X * vectorCD.X + vectorAB.Y * vectorCD.Y;
                double magAB = Math.Sqrt(vectorAB.X * vectorAB.X + vectorAB.Y * vectorAB.Y);
                double magCD = Math.Sqrt(vectorCD.X * vectorCD.X + vectorCD.Y * vectorCD.Y);

                // Calculate angle between vectors and convert radian to degrees
                if (magAB == 0 || magCD == 0)
                {
                    result = 0;
                }
                else
                {
                    double angle = Math.Acos(dotProduct / (magAB * magCD));
                    result = Convert.ToInt32(Util.Clamp(
                            -180.0 * C_WHEEL_ANGLE_PRECISION,
                            Math.Round((angle * (180.0 / Math.PI)), 1) * C_WHEEL_ANGLE_PRECISION,
                            180.0 * C_WHEEL_ANGLE_PRECISION)
                         );
                }

                // Left turn is -180..0 and right turn 0..180 degrees
                if (gyroAccelX < controller.wheelCenterPoint.X) result = -result;
            }

            return result;
        }

        // Calibrate sixaxis steering wheel emulation. Use DS4Windows configuration screen to start a calibration or press a special action key (if defined)
        private void SAWheelEmulationCalibration(DS4StateExposed exposedState, DS4State currentDeviceState, DS4Device controller)
        {
            int gyroAccelX, gyroAccelZ;
            int result;

            gyroAccelX = exposedState.getAccelX();
            gyroAccelZ = exposedState.getAccelZ();

            // State 0=Normal mode (ie. calibration process is not running), 1=Activating calibration, 2=Calibration process running, 3=Completing calibration, 4=Cancelling calibration
            if (controller.WheelRecalibrateActiveState == 1)
            {
                AppLogger.LogToGui($"Controller {1 + devIndex} activated re-calibration of SA steering wheel emulation", false);

                controller.WheelRecalibrateActiveState = 2;

                controller.wheelPrevPhysicalAngle = 0;
                controller.wheelPrevFullAngle = 0;
                controller.wheelFullTurnCount = 0;

                // Clear existing calibration value and use current position as "center" point.
                // This initial center value may be off-center because of shaking the controller while button was pressed. The value will be overriden with correct value once controller is stabilized and hold still few secs at the center point
                controller.wheelCenterPoint.X = gyroAccelX;
                controller.wheelCenterPoint.Y = gyroAccelZ;
                controller.wheel90DegPointRight.X = gyroAccelX + 20;
                controller.wheel90DegPointLeft.X = gyroAccelX - 20;

                // Clear bitmask for calibration points. All three calibration points need to be set before re-calibration process is valid
                controller.wheelCalibratedAxisBitmask = DS4Device.WheelCalibrationPoint.None;

                controller.wheelPrevRecalibrateTime = new DateTime(2500, 1, 1);
            }
            else if (controller.WheelRecalibrateActiveState == 3)
            {
                AppLogger.LogToGui($"Controller {1 + devIndex} completed the calibration of SA steering wheel emulation. center=({controller.wheelCenterPoint.X}, {controller.wheelCenterPoint.Y})  90L=({controller.wheel90DegPointLeft.X}, {controller.wheel90DegPointLeft.Y})  90R=({controller.wheel90DegPointRight.X}, {controller.wheel90DegPointRight.Y})", false);

                // If any of the calibration points (center, left 90deg, right 90deg) are missing then reset back to default calibration values
                if (((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.All) == DS4Device.WheelCalibrationPoint.All))
                    API.Config.SaveControllerConfigs(controller);
                else
                    controller.wheelCenterPoint.X = controller.wheelCenterPoint.Y = 0;

                controller.WheelRecalibrateActiveState = 0;
                controller.wheelPrevRecalibrateTime = DateTime.Now;
            }
            else if (controller.WheelRecalibrateActiveState == 4)
            {
                AppLogger.LogToGui($"Controller {1 + devIndex} cancelled the calibration of SA steering wheel emulation.", false);

                controller.WheelRecalibrateActiveState = 0;
                controller.wheelPrevRecalibrateTime = DateTime.Now;
            }

            if (controller.WheelRecalibrateActiveState > 0)
            {
                // Cross "X" key pressed. Set calibration point when the key is released and controller hold steady for a few seconds
                if (currentDeviceState.Cross == true) controller.wheelPrevRecalibrateTime = DateTime.Now;

                // Make sure controller is hold steady (velocity of gyro axis) to avoid misaligments and set calibration few secs after the "X" key was released
                if (Math.Abs(currentDeviceState.Motion.angVelPitch) < 0.5 && Math.Abs(currentDeviceState.Motion.angVelYaw) < 0.5 && Math.Abs(currentDeviceState.Motion.angVelRoll) < 0.5
                    && ((TimeSpan)(DateTime.Now - controller.wheelPrevRecalibrateTime)).TotalSeconds > 1)
                {
                    controller.wheelPrevRecalibrateTime = new DateTime(2500, 1, 1);

                    if (controller.wheelCalibratedAxisBitmask == DS4Device.WheelCalibrationPoint.None)
                    {
                        controller.wheelCenterPoint.X = gyroAccelX;
                        controller.wheelCenterPoint.Y = gyroAccelZ;

                        controller.wheelCalibratedAxisBitmask |= DS4Device.WheelCalibrationPoint.Center;
                    }
                    else if (controller.wheel90DegPointRight.X < gyroAccelX)
                    {
                        controller.wheel90DegPointRight.X = gyroAccelX;
                        controller.wheel90DegPointRight.Y = gyroAccelZ;
                        controller.wheelCircleCenterPointRight.X = controller.wheelCenterPoint.X;
                        controller.wheelCircleCenterPointRight.Y = controller.wheel90DegPointRight.Y;

                        controller.wheelCalibratedAxisBitmask |= DS4Device.WheelCalibrationPoint.Right90;
                    }
                    else if (controller.wheel90DegPointLeft.X > gyroAccelX)
                    {
                        controller.wheel90DegPointLeft.X = gyroAccelX;
                        controller.wheel90DegPointLeft.Y = gyroAccelZ;
                        controller.wheelCircleCenterPointLeft.X = controller.wheelCenterPoint.X;
                        controller.wheelCircleCenterPointLeft.Y = controller.wheel90DegPointLeft.Y;

                        controller.wheelCalibratedAxisBitmask |= DS4Device.WheelCalibrationPoint.Left90;
                    }
                }

                // Show lightbar color feedback how the calibration process is proceeding.
                //  red / yellow / blue / green = No calibration anchors/one anchor/two anchors/all three anchors calibrated when color turns to green (center, 90DegLeft, 90DegRight).
                int bitsSet = CountNumOfSetBits((int)controller.wheelCalibratedAxisBitmask);
                if (bitsSet >= 3) lightBar.forcedColor = calibrationColor_3;
                else if (bitsSet == 2) lightBar.forcedColor = calibrationColor_2;
                else if (bitsSet == 1) lightBar.forcedColor = calibrationColor_1;
                else lightBar.forcedColor = calibrationColor_0;

                result = CalculateControllerAngle(gyroAccelX, gyroAccelZ, controller);

                // Force lightbar flashing when controller is currently at calibration point (user can verify the calibration before accepting it by looking at flashing lightbar)
                if (((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Center) != 0 && Math.Abs(result) <= 1 * C_WHEEL_ANGLE_PRECISION)
                 || ((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Left90) != 0 && result <= -89 * C_WHEEL_ANGLE_PRECISION && result >= -91 * C_WHEEL_ANGLE_PRECISION)
                 || ((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Right90) != 0 && result >= 89 * C_WHEEL_ANGLE_PRECISION && result <= 91 * C_WHEEL_ANGLE_PRECISION)
                 || ((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Left90) != 0 && Math.Abs(result) >= 179 * C_WHEEL_ANGLE_PRECISION))
                    lightBar.forcedFlash = 2;
                else
                    lightBar.forcedFlash = 0;

                lightBar.forcedLight = true;

                LogToGuiSACalibrationDebugMsg($"Calibration values ({gyroAccelX}, {gyroAccelZ})  angle={result / (1.0 * C_WHEEL_ANGLE_PRECISION)}\n");
            }
            else
            {
                // Re-calibration completed or cancelled. Set lightbar color back to normal color
                lightBar.forcedFlash = 0;
                lightBar.forcedColor = cfg.MainColor;
                lightBar.forcedLight = false;
                lightBar.updateLightBar(controller);
            }
        }

        private Int32 Scale360degreeGyroAxis(DS4StateExposed exposedState)
        {
            unchecked
            {
                DS4Device controller;
                DS4State currentDeviceState;

                int gyroAccelX, gyroAccelZ;
                int result;

                controller = ctrl.DS4Controller;
                if (controller == null) return 0;

                currentDeviceState = controller.getCurrentStateRef();

                // If calibration is active then do the calibration process instead of the normal "angle calculation"
                if (controller.WheelRecalibrateActiveState > 0)
                {
                    SAWheelEmulationCalibration(exposedState, currentDeviceState, controller);

                    // Return center wheel position while SA wheel emuation is being calibrated
                    return 0;
                }

                // Do nothing if connection is active but the actual DS4 controller is still missing or not yet synchronized
                if (!controller.Synced)
                    return 0;

                gyroAccelX = exposedState.getAccelX();
                gyroAccelZ = exposedState.getAccelZ();

                // If calibration values are missing then use "educated guesses" about good starting values
                if (controller.wheelCenterPoint.IsEmpty)
                {
                    if (!API.Config.LoadControllerConfigs(controller))
                    {
                        AppLogger.LogToGui($"Controller {1 + devIndex} sixaxis steering wheel calibration data missing. It is recommended to run steering wheel calibration process by pressing SASteeringWheelEmulationCalibration special action key. Using estimated values until the controller is calibrated at least once.", false);

                        // Use current controller position as "center point". Assume DS4Windows was started while controller was hold in center position (yes, dangerous assumption but can't do much until controller is calibrated)
                        controller.wheelCenterPoint.X = gyroAccelX;
                        controller.wheelCenterPoint.Y = gyroAccelZ;

                        controller.wheel90DegPointRight.X = controller.wheelCenterPoint.X + 113;
                        controller.wheel90DegPointRight.Y = controller.wheelCenterPoint.Y + 110;

                        controller.wheel90DegPointLeft.X = controller.wheelCenterPoint.X - 127;
                        controller.wheel90DegPointLeft.Y = controller.wheel90DegPointRight.Y;
                    }

                    controller.wheelCircleCenterPointRight.X = controller.wheelCenterPoint.X;
                    controller.wheelCircleCenterPointRight.Y = controller.wheel90DegPointRight.Y;
                    controller.wheelCircleCenterPointLeft.X = controller.wheelCenterPoint.X;
                    controller.wheelCircleCenterPointLeft.Y = controller.wheel90DegPointLeft.Y;

                    AppLogger.LogToGui($"Controller {1 + devIndex} steering wheel emulation calibration values. Center=({controller.wheelCenterPoint.X}, {controller.wheelCenterPoint.Y})  90L=({controller.wheel90DegPointLeft.X}, {controller.wheel90DegPointLeft.Y})  90R=({controller.wheel90DegPointRight.X}, {controller.wheel90DegPointRight.Y})  Range={cfg.SASteeringWheelEmulationRange}", false);
                    controller.wheelPrevRecalibrateTime = DateTime.Now;
                }


                int maxRangeRight = cfg.SASteeringWheelEmulationRange / 2 * C_WHEEL_ANGLE_PRECISION;
                int maxRangeLeft = -maxRangeRight;

                result = CalculateControllerAngle(gyroAccelX, gyroAccelZ, controller);

                // Apply deadzone (SA X-deadzone value). This code assumes that 20deg is the max deadzone anyone ever might wanna use (in practice effective deadzone 
                // is probably just few degrees by using SXDeadZone values 0.01...0.05)
                double sxDead = cfg.SZ.DeadZone;
                if (sxDead > 0)
                {
                    int sxDeadInt = Convert.ToInt32(20.0 * C_WHEEL_ANGLE_PRECISION * sxDead);
                    if (Math.Abs(result) <= sxDeadInt)
                    {
                        result = 0;
                    }
                    else
                    {
                        // Smooth steering angle based on deadzone range instead of just clipping the deadzone gap
                        result -= (result < 0 ? -sxDeadInt : sxDeadInt);
                    }
                }

                // If wrapped around from +180 to -180 side (or vice versa) then SA steering wheel keeps on turning beyond 360 degrees (if range is >360).
                // Keep track of how many times the steering wheel has been turned beyond the full 360 circle and clip the result to max range.
                int wheelFullTurnCount = controller.wheelFullTurnCount;
                if (controller.wheelPrevPhysicalAngle < 0 && result > 0)
                {
                    if ((result - controller.wheelPrevPhysicalAngle) > 180 * C_WHEEL_ANGLE_PRECISION)
                    {
                        if (maxRangeRight > 360/2 * C_WHEEL_ANGLE_PRECISION)
                            wheelFullTurnCount--;
                        else
                            result = maxRangeLeft;
                    }
                }
                else if (controller.wheelPrevPhysicalAngle > 0 && result < 0)
                {
                    if ((controller.wheelPrevPhysicalAngle - result) > 180 * C_WHEEL_ANGLE_PRECISION)
                    {
                        if (maxRangeRight > 360/2 * C_WHEEL_ANGLE_PRECISION)
                            wheelFullTurnCount++;
                        else
                            result = maxRangeRight;
                    }
                }
                controller.wheelPrevPhysicalAngle = result;

                if (wheelFullTurnCount != 0)
                {
                    // Adjust value of result (steering wheel angle) based on num of full 360 turn counts
                    result += (wheelFullTurnCount * 180 * C_WHEEL_ANGLE_PRECISION * 2);
                }

                // If the new angle is more than 180 degrees further away then this is probably bogus value (controller shaking too much and gyro and velocity sensors went crazy).
                // Accept the new angle only when the new angle is within a "stability threshold", otherwise use the previous full angle value and wait for controller to be stabilized.
                if (Math.Abs(result - controller.wheelPrevFullAngle) <= 180 * C_WHEEL_ANGLE_PRECISION)
                {
                    controller.wheelPrevFullAngle = result;
                    controller.wheelFullTurnCount = wheelFullTurnCount;
                }
                else
                {
                    result = controller.wheelPrevFullAngle;
                }

                result = Util.Clamp(maxRangeLeft, result, maxRangeRight);

                // Debug log output of SA sensor values
                //LogToGuiSACalibrationDebugMsg($"DBG gyro=({gyroAccelX}, {gyroAccelZ})  output=({exposedState.OutputAccelX}, {exposedState.OutputAccelZ})  PitRolYaw=({currentDeviceState.Motion.gyroPitch}, {currentDeviceState.Motion.gyroRoll}, {currentDeviceState.Motion.gyroYaw})  VelPitRolYaw=({currentDeviceState.Motion.angVelPitch}, {currentDeviceState.Motion.angVelRoll}, {currentDeviceState.Motion.angVelYaw})  angle={result / (1.0 * C_WHEEL_ANGLE_PRECISION)}  fullTurns={controller.wheelFullTurnCount}", false);

                // Apply anti-deadzone (SA X-antideadzone value)
                double sxAntiDead = cfg.SX.AntiDeadZone;

                int outputAxisMax, outputAxisMin, outputAxisZero;
                if ( cfg.OutputDevType == OutContType.DS4 )
                {
                    // DS4 analog stick axis supports only 0...255 output value range (not the best one for steering wheel usage)
                    outputAxisMax = 255;
                    outputAxisMin = 0;
                    outputAxisZero = 128;
                }
                else
                {
                    // x360 (xinput) analog stick axis supports -32768...32767 output value range (more than enough for steering wheel usage)
                    outputAxisMax = 32767;
                    outputAxisMin = -32768;
                    outputAxisZero = 0;
                }

                switch (cfg.SASteeringWheelEmulationAxis)
                {
                    case SASteeringWheelEmulationAxisType.LX:
                    case SASteeringWheelEmulationAxisType.LY:
                    case SASteeringWheelEmulationAxisType.RX:
                    case SASteeringWheelEmulationAxisType.RY:
                        // DS4 thumbstick axis output (-32768..32767 raw value range)
                        //return (((result - maxRangeLeft) * (32767 - (-32768))) / (maxRangeRight - maxRangeLeft)) + (-32768);
                        if (result == 0) return outputAxisZero;

                        if (sxAntiDead > 0)
                        {
                            sxAntiDead *= (outputAxisMax - outputAxisZero);
                            if (result < 0) return (((result - maxRangeLeft) * (outputAxisZero - Convert.ToInt32(sxAntiDead) - (outputAxisMin))) / (0 - maxRangeLeft)) + (outputAxisMin);
                            else return (((result - 0) * (outputAxisMax - (outputAxisZero + Convert.ToInt32(sxAntiDead)))) / (maxRangeRight - 0)) + (outputAxisZero + Convert.ToInt32(sxAntiDead));
                        }
                        else
                        {
                            return (((result - maxRangeLeft) * (outputAxisMax - (outputAxisMin))) / (maxRangeRight - maxRangeLeft)) + (outputAxisMin);
                        }
                        
                    case SASteeringWheelEmulationAxisType.L2R2:
                        // DS4 Trigger axis output. L2+R2 triggers share the same axis in x360 xInput/DInput controller, 
                        // so L2+R2 steering output supports only 360 turn range (-255..255 raw value range in the shared trigger axis)
                        if (result == 0) return 0;

                        result = Convert.ToInt32(Math.Round(result / (1.0 * C_WHEEL_ANGLE_PRECISION)));
                        if (result < 0) result = -181 - result;

                        if (sxAntiDead > 0)
                        {
                            sxAntiDead *= 255;
                            if (result < 0) return (((result - (-180)) * (-Convert.ToInt32(sxAntiDead) - (-255))) / (0 - (-180))) + (-255);
                            else return (((result - (0)) * (255 - (Convert.ToInt32(sxAntiDead)))) / (180 - (0))) + (Convert.ToInt32(sxAntiDead));
                        }
                        else
                        {
                            return (((result - (-180)) * (255 - (-255))) / (180 - (-180))) + (-255);
                        }

                    case SASteeringWheelEmulationAxisType.VJoy1X:
                    case SASteeringWheelEmulationAxisType.VJoy1Y:
                    case SASteeringWheelEmulationAxisType.VJoy1Z:
                    case SASteeringWheelEmulationAxisType.VJoy2X:
                    case SASteeringWheelEmulationAxisType.VJoy2Y:
                    case SASteeringWheelEmulationAxisType.VJoy2Z:
                        // SASteeringWheelEmulationAxisType.VJoy1X/VJoy1Y/VJoy1Z/VJoy2X/VJoy2Y/VJoy2Z VJoy axis output (0..32767 raw value range by default)
                        if (result == 0) return 16384;

                        if (sxAntiDead > 0)
                        {
                            sxAntiDead *= 16384;
                            if (result < 0) return (((result - maxRangeLeft) * (16384 - Convert.ToInt32(sxAntiDead) - (-0))) / (0 - maxRangeLeft)) + (-0);
                            else return (((result - 0) * (32767 - (16384 + Convert.ToInt32(sxAntiDead)))) / (maxRangeRight - 0)) + (16384 + Convert.ToInt32(sxAntiDead));
                        }
                        else
                        {
                            return (((result - maxRangeLeft) * (32767 - (-0))) / (maxRangeRight - maxRangeLeft)) + (-0);
                        }

                    default:
                        // Should never come here, but C# case statement syntax requires DEFAULT handler
                        return 0;
                }
            }
        }

    }
}
