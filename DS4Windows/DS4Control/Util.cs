using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using DS4Windows.Forms;

namespace DS4Windows
{
    [SuppressUnmanagedCodeSecurity]
    static class Util
    {
        public enum PROCESS_INFORMATION_CLASS : int
        {
            ProcessBasicInformation = 0,
            ProcessQuotaLimits,
            ProcessIoCounters,
            ProcessVmCounters,
            ProcessTimes,
            ProcessBasePriority,
            ProcessRaisePriority,
            ProcessDebugPort,
            ProcessExceptionPort,
            ProcessAccessToken,
            ProcessLdtInformation,
            ProcessLdtSize,
            ProcessDefaultHardErrorMode,
            ProcessIoPortHandlers,
            ProcessPooledUsageAndLimits,
            ProcessWorkingSetWatch,
            ProcessUserModeIOPL,
            ProcessEnableAlignmentFaultFixup,
            ProcessPriorityClass,
            ProcessWx86Information,
            ProcessHandleCount,
            ProcessAffinityMask,
            ProcessPriorityBoost,
            ProcessDeviceMap,
            ProcessSessionInformation,
            ProcessForegroundInformation,
            ProcessWow64Information,
            ProcessImageFileName,
            ProcessLUIDDeviceMapsEnabled,
            ProcessBreakOnTermination,
            ProcessDebugObjectHandle,
            ProcessDebugFlags,
            ProcessHandleTracing,
            ProcessIoPriority,
            ProcessExecuteFlags,
            ProcessResourceManagement,
            ProcessCookie,
            ProcessImageInformation,
            ProcessCycleTime,
            ProcessPagePriority,
            ProcessInstrumentationCallback,
            ProcessThreadStackAllocation,
            ProcessWorkingSetWatchEx,
            ProcessImageFileNameWin32,
            ProcessImageFileMapping,
            ProcessAffinityUpdateMode,
            ProcessMemoryAllocationMode,
            MaxProcessInfoClass
        }

        [StructLayout(LayoutKind.Sequential)]
        public class DEV_BROADCAST_DEVICEINTERFACE
        {
            internal Int32 dbcc_size;
            internal Int32 dbcc_devicetype;
            internal Int32 dbcc_reserved;
            internal Guid dbcc_classguid;
            internal Int16 dbcc_name;
        }

        public const Int32 DBT_DEVTYP_DEVICEINTERFACE = 0x0005;

        public const Int32 DEVICE_NOTIFY_WINDOW_HANDLE = 0x0000;
        public const Int32 DEVICE_NOTIFY_SERVICE_HANDLE = 0x0001;
        public const Int32 DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x0004;

        public const Int32 WM_CREATE = 0x0001;
        public const Int32 WM_DEVICECHANGE = 0x0219;

        public const Int32 DIGCF_PRESENT = 0x0002;
        public const Int32 DIGCF_DEVICEINTERFACE = 0x0010;

        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtSetInformationProcess(IntPtr processHandle,
           PROCESS_INFORMATION_CLASS processInformationClass, ref IntPtr processInformation, uint processInformationLength);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, Int32 Flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean UnregisterDeviceNotification(IntPtr Handle);

        public static Boolean RegisterNotify(IntPtr Form, Guid Class, ref IntPtr Handle, Boolean Window = true)
        {
            IntPtr devBroadcastDeviceInterfaceBuffer = IntPtr.Zero;

            try
            {
                DEV_BROADCAST_DEVICEINTERFACE devBroadcastDeviceInterface = new DEV_BROADCAST_DEVICEINTERFACE();
                Int32 Size = Marshal.SizeOf(devBroadcastDeviceInterface);

                devBroadcastDeviceInterface.dbcc_size = Size;
                devBroadcastDeviceInterface.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
                devBroadcastDeviceInterface.dbcc_reserved = 0;
                devBroadcastDeviceInterface.dbcc_classguid = Class;

                devBroadcastDeviceInterfaceBuffer = Marshal.AllocHGlobal(Size);
                Marshal.StructureToPtr(devBroadcastDeviceInterface, devBroadcastDeviceInterfaceBuffer, true);

                Handle = RegisterDeviceNotification(Form, devBroadcastDeviceInterfaceBuffer, Window ? DEVICE_NOTIFY_WINDOW_HANDLE : DEVICE_NOTIFY_SERVICE_HANDLE);

                Marshal.PtrToStructure(devBroadcastDeviceInterfaceBuffer, devBroadcastDeviceInterface);

                return Handle != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0} {1}", ex.HelpLink, ex.Message);
                throw;
            }
            finally
            {
                if (devBroadcastDeviceInterfaceBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(devBroadcastDeviceInterfaceBuffer);
                }
            }
        }

        public static Boolean UnregisterNotify(IntPtr Handle)
        {
            try
            {
                return UnregisterDeviceNotification(Handle);
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0} {1}", ex.HelpLink, ex.Message);
                throw;
            }
        }

        public static T? TryParse<T>(string s) where T : struct
        {
            // This function is like TryParse on basic types, except that
            // it returns a nullable type. It's useful in LINQ queries.
            // TODO: A variant returning a collection for use with MultiSelect
            // would be useful as well.
            var converter = TypeDescriptor.GetConverter(typeof(T));
            if (converter != null && converter.CanConvertFrom(typeof(string)))
            {
                return (T)converter.ConvertFromString(s);
            }
            return default(T);
        }

        public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
        {
            // API like .Net Core's Math.Clamp
            if (val.CompareTo(min) < 0) return min;
            else if (val.CompareTo(max) > 0) return max;
            else return val;
        }

        public struct Clamped<T> : IComparable, IComparable<T>, IEquatable<T>
            where T : struct, IComparable<T>, IEquatable<T>
        {
            private T _value;
            private readonly  T _min, _max;

            Clamped(T o, T min, T max)
            {
                if (min.CompareTo(max) > 0)
                    throw new ArgumentException($"{min} must be less or equal to {max}", "min");
                _value = Util.Clamp(o, min, max);
                _min = min;
                _max = max;
            }

            void set(T o)
            {
                _value = Util.Clamp(o, _min, _max);
            }

            T get() => _value;
            public static implicit operator T(Clamped<T> o) => o._value;

            public int CompareTo(T o) => _value.CompareTo(o);

            public int CompareTo(object o)
            {
                if (o != null && !(o is T)) throw
                    new ArgumentException(
                        String.Format("Object must be of type {0}", typeof(T).ToString()),
                        "o");
                return _value.CompareTo((T)o);
            }

            public static bool operator >(Clamped<T> l, Clamped<T> r) => l.CompareTo(r) == 1;
            public static bool operator <(Clamped<T> l, Clamped<T> r) => l.CompareTo(r) == -1;
            public static bool operator >=(Clamped<T> l, Clamped<T> r) => l.CompareTo(r) >= 0;
            public static bool operator <=(Clamped<T> l, Clamped<T> r) => l.CompareTo(r) <= 0;

            public bool Equals(T o) => _value.Equals(o);

            public override bool Equals(object o) => (o is T other) ? Equals(other) : false;

            public override int GetHashCode() => _value.GetHashCode();

            private T MinValue { get => _min; }
            private T MaxValue { get => _max; }
        }

        struct Lazy<T> where T : struct
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

        // TODO: Unused. Do we need it?
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
                csR = HuetoRGB(hue2, sat2, bri2 - br * ratio);
            else
                csR = HuetoRGB(hue2 - hr * ratio, sat2 - sr * ratio, bri2 - br * ratio);

            return csR;
        }

        public static Color HuetoRGB(float hue, float sat, float bri)
        {
            float C = (1 - Math.Abs(2 * bri) - 1) * sat;
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
    }

    namespace ExtensionMethods
    {
        public static class Extensions
        {
            public static Value GetOr<Key,Value>(this IDictionary<Key, Value> dict, Key key, Value defaultResult)
            {
                dict.TryGetValue(key, out defaultResult);
                return defaultResult;
            }
        }
    }
}
