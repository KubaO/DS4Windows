﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DS4Windows
{
    public struct VidPidInfo
    {
        public readonly int vid;
        public readonly int pid;
        internal VidPidInfo(int vid, int pid)
        {
            this.vid = vid;
            this.pid = pid;
        }
    }

    public class DS4Devices
    {
        // (HID device path, DS4Device)
        private static Dictionary<string, DS4Device> Devices = new Dictionary<string, DS4Device>();
        private static HashSet<string> deviceSerials = new HashSet<string>();
        private static HashSet<string> DevicePaths = new HashSet<string>();
        // Keep instance of opened exclusive mode devices not in use (Charging while using BT connection)
        private static List<HidDevice> DisabledDevices = new List<HidDevice>();
        private static Stopwatch sw = new Stopwatch();
        public static bool isExclusiveMode = false;
        internal const int SONY_VID = 0x054C;
        internal const int RAZER_VID = 0x1532;
        internal const int NACON_VID = 0x146B;
        internal const int HORI_VID = 0x0F0D;

        private static VidPidInfo[] knownDevices =
        {
            new VidPidInfo(SONY_VID, 0xBA0),
            new VidPidInfo(SONY_VID, 0x5C4),
            new VidPidInfo(SONY_VID, 0x09CC),
            new VidPidInfo(RAZER_VID, 0x1000),
            new VidPidInfo(NACON_VID, 0x0D01),
            new VidPidInfo(NACON_VID, 0x0D02),
            new VidPidInfo(HORI_VID, 0x00EE),    // Hori PS4 Mini Wired Gamepad
            new VidPidInfo(0x7545, 0x0104),
            new VidPidInfo(0x2E95, 0x7725), // Scuf Vantage gamepad
            new VidPidInfo(0x11C0, 0x4001), // PS4 Fun Controller
            new VidPidInfo(RAZER_VID, 0x1007), // Razer Raiju Tournament Edition
            new VidPidInfo(RAZER_VID, 0x1004), // Razer Raiju Ultimate Edition (wired)
            new VidPidInfo(RAZER_VID, 0x1009), // Razer Raiju Ultimate Edition (BT). Doesn't work yet for some reason even when non-steam Razer driver lists the BT Razer Ultimate with this ID.
            new VidPidInfo(SONY_VID, 0x05C5), // CronusMax (PS4 Output Mode)
            new VidPidInfo(0x0C12, 0x57AB), // Warrior Joypad JS083 (wired). Custom lightbar color doesn't work, but everything else works OK (except touchpad and gyro because the gamepad doesnt have those).
        };

        private static string devicePathToInstanceId(string devicePath)
        {
            string deviceInstanceId = devicePath;
            deviceInstanceId = deviceInstanceId.Remove(0, deviceInstanceId.LastIndexOf('\\') + 1);
            deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.LastIndexOf('{'));
            deviceInstanceId = deviceInstanceId.Replace('#', '\\');
            if (deviceInstanceId.EndsWith("\\"))
            {
                deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.Length - 1);
            }

            return deviceInstanceId;
        }

        private static bool IsRealDS4(HidDevice hDevice)
        {
            string deviceInstanceId = devicePathToInstanceId(hDevice.DevicePath);
            string temp = API.GetDeviceProperty(deviceInstanceId,
                NativeMethods.DEVPKEY_Device_UINumber);
            return string.IsNullOrEmpty(temp);
        }

        // Enumerates ds4 controllers in the system
        public static void findControllers()
        {
            lock (Devices)
            {
                IEnumerable<HidDevice> hDevices = HidDevices.EnumerateDS4(knownDevices);
                hDevices = hDevices.Where(dev => IsRealDS4(dev)).Select(dev => dev);
                //hDevices = from dev in hDevices where IsRealDS4(dev) select dev;
                // Sort Bluetooth first in case USB is also connected on the same controller.
                hDevices = hDevices.OrderBy<HidDevice, ConnectionType>(
                    (HidDevice d) => { return DS4Device.HidConnectionType(d); });

                List<HidDevice> tempList = hDevices.ToList();
                purgeHiddenExclusiveDevices();
                tempList.AddRange(DisabledDevices);
                int devCount = tempList.Count();
                string devicePlural = "device" + (devCount == 0 || devCount > 1 ? "s" : "");
                //Log.LogToGui("Found " + devCount + " possible " + devicePlural + ". Examining " + devicePlural + ".", false);

                foreach (HidDevice hDevice in tempList)
                {
                    if (hDevice.Description == "HID-compliant vendor-defined device")
                        continue; // ignore the Nacon Revolution Pro programming interface
                    else if (DevicePaths.Contains(hDevice.DevicePath))
                        continue; // BT/USB endpoint already open once

                    if (!hDevice.IsOpen)
                    {
                        hDevice.OpenDevice(isExclusiveMode);
                        if (!hDevice.IsOpen && isExclusiveMode)
                        {
                            try
                            {
                                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                                WindowsPrincipal principal = new WindowsPrincipal(identity);
                                bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

                                if (!elevated)
                                {
                                    // Launches an elevated child process to re-enable device
                                    string exeName = Process.GetCurrentProcess().MainModule.FileName;
                                    ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
                                    startInfo.Verb = "runas";
                                    startInfo.Arguments = "re-enabledevice " + devicePathToInstanceId(hDevice.DevicePath);
                                    Process child = Process.Start(startInfo);

                                    if (!child.WaitForExit(30000))
                                    {
                                        child.Kill();
                                    }
                                    else if (child.ExitCode == 0)
                                    {
                                        hDevice.OpenDevice(isExclusiveMode);
                                    }
                                }
                                else
                                {
                                    reEnableDevice(devicePathToInstanceId(hDevice.DevicePath));
                                    hDevice.OpenDevice(isExclusiveMode);
                                }
                            }
                            catch (Exception) { }
                        }
                        
                        // TODO in exclusive mode, try to hold both open when both are connected
                        if (isExclusiveMode && !hDevice.IsOpen)
                            hDevice.OpenDevice(false);
                    }

                    if (hDevice.IsOpen)
                    {
                        string serial = hDevice.readSerial();
                        bool validSerial = !serial.Equals(DS4Device.blankSerial);
                        if (validSerial && deviceSerials.Contains(serial))
                        {
                            // happens when the BT endpoint already is open and the USB is plugged into the same host
                            if (isExclusiveMode && hDevice.IsExclusive &&
                                !DisabledDevices.Contains(hDevice))
                            {
                                // Grab reference to exclusively opened HidDevice so device
                                // stays hidden to other processes
                                DisabledDevices.Add(hDevice);
                                //DevicePaths.Add(hDevice.DevicePath);
                            }

                            continue;
                        }
                        else
                        {
                            DS4Device ds4Device = new DS4Device(hDevice);
                            //ds4Device.Removal += On_Removal;
                            if (!ds4Device.ExitOutputThread)
                            {
                                Devices.Add(hDevice.DevicePath, ds4Device);
                                DevicePaths.Add(hDevice.DevicePath);
                                deviceSerials.Add(serial);
                            }
                        }
                    }
                }
            }
        }
        
        // Returns DS4 controllers that were found and are running
        public static IEnumerable<DS4Device> getDS4Controllers()
        {
            lock (Devices)
            {
                return Devices.Values.ToArray();
            }
        }

        public static void stopControllers()
        {
            lock (Devices)
            {
                foreach (DS4Device device in getDS4Controllers())
                {
                    device.StopUpdate();
                    //device.runRemoval();
                    device.HidDevice.CloseDevice();
                }

                Devices.Clear();
                DevicePaths.Clear();
                deviceSerials.Clear();
                DisabledDevices.Clear();
            }
        }

        // Called when devices is disconnected, timed out or has input reading failure
        public static void On_Removal(object sender, EventArgs e)
        {
            RemoveDevice((DS4Device)sender);
        }

        public static void RemoveDevice(DS4Device device)
        {
            if (device == null) return;
            lock (Devices)
            {
                device.HidDevice.CloseDevice();
                Devices.Remove(device.HidDevice.DevicePath);
                DevicePaths.Remove(device.HidDevice.DevicePath);
                deviceSerials.Remove(device.MacAddress);
                //purgeHiddenExclusiveDevices();
            }
        }

        public static void UpdateSerial(object sender, EventArgs e)
        {
            if (sender == null) return;
            lock (Devices)
            {
                var device = (DS4Device)sender;
                string devPath = device.HidDevice.DevicePath;
                string serial = device.MacAddress;
                if (Devices.ContainsKey(devPath))
                {
                    deviceSerials.Remove(serial);
                    device.updateSerial();
                    serial = device.MacAddress;
                    if (DS4Device.isValidSerial(serial))
                    {
                        deviceSerials.Add(serial);
                    }

                    if (device.ShouldRunCalib)
                        device.RefreshCalibration();
                }
            }
        }

        private static void purgeHiddenExclusiveDevices()
        {
            List<HidDevice> disabledDevList = new List<HidDevice>();
            foreach (HidDevice tempDev in DisabledDevices)
            {
                if (tempDev == null || !tempDev.IsOpen) continue;
                if (tempDev.IsConnected)
                {
                    disabledDevList.Add(tempDev);
                }
                else
                {
                    try
                    {
                        tempDev.CloseDevice();
                    }
                    catch { }

                    if (DevicePaths.Contains(tempDev.DevicePath))
                    {
                        DevicePaths.Remove(tempDev.DevicePath);
                    }
                }
            }
            DisabledDevices.Clear();
            DisabledDevices.AddRange(disabledDevList);
        }

        public static void reEnableDevice(string deviceInstanceId)
        {
            bool success;
            Guid hidGuid = new Guid();
            NativeMethods.HidD_GetHidGuid(ref hidGuid);
            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, deviceInstanceId, 0, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
            NativeMethods.SP_DEVINFO_DATA deviceInfoData = new NativeMethods.SP_DEVINFO_DATA();
            deviceInfoData.cbSize = Marshal.SizeOf(deviceInfoData);
            success = NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 0, ref deviceInfoData);
            if (!success)
            {
                throw new Exception("Error getting device info data, error code = " + Marshal.GetLastWin32Error());
            }
            success = NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 1, ref deviceInfoData); // Checks that we have a unique device
            if (success)
            {
                throw new Exception("Can't find unique device");
            }

            NativeMethods.SP_PROPCHANGE_PARAMS propChangeParams = new NativeMethods.SP_PROPCHANGE_PARAMS();
            propChangeParams.classInstallHeader.cbSize = Marshal.SizeOf(propChangeParams.classInstallHeader);
            propChangeParams.classInstallHeader.installFunction = NativeMethods.DIF_PROPERTYCHANGE;
            propChangeParams.stateChange = NativeMethods.DICS_DISABLE;
            propChangeParams.scope = NativeMethods.DICS_FLAG_GLOBAL;
            propChangeParams.hwProfile = 0;
            success = NativeMethods.SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref propChangeParams, Marshal.SizeOf(propChangeParams));
            if (!success)
            {
                throw new Exception("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
            }
            success = NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
            // TEST: If previous SetupDiCallClassInstaller fails, just continue
            // otherwise device will likely get permanently disabled.
            /*if (!success)
            {
                throw new Exception("Error disabling device, error code = " + Marshal.GetLastWin32Error());
            }
            */

            //System.Threading.Thread.Sleep(50);
            sw.Restart();
            while (sw.ElapsedMilliseconds < 50)
            {
                // Use SpinWait to keep control of current thread. Using Sleep could potentially
                // cause other events to get run out of order
                System.Threading.Thread.SpinWait(100);
            }
            sw.Stop();

            propChangeParams.stateChange = NativeMethods.DICS_ENABLE;
            success = NativeMethods.SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref propChangeParams, Marshal.SizeOf(propChangeParams));
            if (!success)
            {
                throw new Exception("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
            }
            success = NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
            if (!success)
            {
                throw new Exception("Error enabling device, error code = " + Marshal.GetLastWin32Error());
            }

            //System.Threading.Thread.Sleep(50);
            sw.Restart();
            while (sw.ElapsedMilliseconds < 50)
            {
                // Use SpinWait to keep control of current thread. Using Sleep could potentially
                // cause other events to get run out of order
                System.Threading.Thread.SpinWait(100);
            }
            sw.Stop();

            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }
}
