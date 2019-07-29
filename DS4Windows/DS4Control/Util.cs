using System;
using System.ComponentModel;
using System.Diagnostics;
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
    }
}
