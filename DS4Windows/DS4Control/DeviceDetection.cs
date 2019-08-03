using System;

namespace DS4Windows
{
    public class DeviceDetection
    {
        public static bool CheckForDevice(string guid)
        {
            bool result = false;
            Guid deviceGuid = Guid.Parse(guid);
            NativeMethods.SP_DEVINFO_DATA deviceInfoData =
                new NativeMethods.SP_DEVINFO_DATA();
            deviceInfoData.cbSize =
                System.Runtime.InteropServices.Marshal.SizeOf(deviceInfoData);

            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref deviceGuid, null, 0,
                NativeMethods.DIGCF_DEVICEINTERFACE);
            result = NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 0, ref deviceInfoData);

            if (deviceInfoSet.ToInt64() != NativeMethods.INVALID_HANDLE_VALUE) {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return result;
        }

        public static bool IsHidGuardianInstalled()
        {
            return CheckForSysDevice(@"Root\HidGuardian");
        }

        const string VIGEMBUS_GUID = "{96E42B22-F5E9-42F8-B043-ED0F932F014F}";

        public static bool IsViGEmBusInstalled()
        {
            return CheckForDevice(VIGEMBUS_GUID);
        }

        public static string ViGEmBusVersion()
        {
            return GetViGEmDriverProperty(NativeMethods.DEVPKEY_Device_DriverVersion);
        }

        internal static string GetDeviceProperty(string deviceInstanceId,
            NativeMethods.DEVPROPKEY prop)
        {
            string result = string.Empty;
            NativeMethods.SP_DEVINFO_DATA deviceInfoData = new NativeMethods.SP_DEVINFO_DATA();
            deviceInfoData.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(deviceInfoData);
            var dataBuffer = new byte[4096];
            ulong propertyType = 0;
            var requiredSize = 0;

            Guid hidGuid = new Guid();
            NativeMethods.HidD_GetHidGuid(ref hidGuid);
            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, deviceInstanceId, 0, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
            NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 0, ref deviceInfoData);
            if (NativeMethods.SetupDiGetDeviceProperty(deviceInfoSet, ref deviceInfoData, ref prop, ref propertyType,
                dataBuffer, dataBuffer.Length, ref requiredSize, 0)) {
                result = dataBuffer.ToUTF16String();
            }

            if (deviceInfoSet.ToInt64() != NativeMethods.INVALID_HANDLE_VALUE) {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return result;
        }

        private static bool CheckForSysDevice(string searchHardwareId)
        {
            bool result = false;
            Guid sysGuid = Guid.Parse("{4d36e97d-e325-11ce-bfc1-08002be10318}");
            NativeMethods.SP_DEVINFO_DATA deviceInfoData =
                new NativeMethods.SP_DEVINFO_DATA();
            deviceInfoData.cbSize =
                System.Runtime.InteropServices.Marshal.SizeOf(deviceInfoData);
            var dataBuffer = new byte[4096];
            ulong propertyType = 0;
            var requiredSize = 0;
            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref sysGuid, null, 0, 0);
            for (int i = 0; !result && NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++) {
                if (NativeMethods.SetupDiGetDeviceProperty(deviceInfoSet, ref deviceInfoData,
                    ref NativeMethods.DEVPKEY_Device_HardwareIds, ref propertyType,
                    dataBuffer, dataBuffer.Length, ref requiredSize, 0)) {
                    string hardwareId = dataBuffer.ToUTF16String();
                    //if (hardwareIds.Contains("Virtual Gamepad Emulation Bus"))
                    //    result = true;
                    if (hardwareId.Equals(searchHardwareId))
                        result = true;
                }
            }

            if (deviceInfoSet.ToInt64() != NativeMethods.INVALID_HANDLE_VALUE) {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return result;
        }

        private static string GetViGEmDriverProperty(NativeMethods.DEVPROPKEY prop)
        {
            string result = string.Empty;
            Guid deviceGuid = Guid.Parse(VIGEMBUS_GUID);
            NativeMethods.SP_DEVINFO_DATA deviceInfoData =
                new NativeMethods.SP_DEVINFO_DATA();
            deviceInfoData.cbSize =
                System.Runtime.InteropServices.Marshal.SizeOf(deviceInfoData);

            var dataBuffer = new byte[4096];
            ulong propertyType = 0;
            var requiredSize = 0;

            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref deviceGuid, null, 0,
                NativeMethods.DIGCF_DEVICEINTERFACE);
            NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 0, ref deviceInfoData);
            if (NativeMethods.SetupDiGetDeviceProperty(deviceInfoSet, ref deviceInfoData, ref prop, ref propertyType,
                dataBuffer, dataBuffer.Length, ref requiredSize, 0)) {
                result = dataBuffer.ToUTF16String();
            }

            if (deviceInfoSet.ToInt64() != NativeMethods.INVALID_HANDLE_VALUE) {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return result;
        }
    }
}