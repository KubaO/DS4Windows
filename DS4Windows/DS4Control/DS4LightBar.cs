using System;
using System.Drawing;
using static System.Math;
using static DS4Windows.Global;
using System.Diagnostics;

namespace DS4Windows
{
    public class DS4LightBar
    {
        private readonly IDeviceConfig cfg;
        private readonly IDeviceAuxiliaryConfig aux;

        internal DS4LightBar(int devIndex)
        {
            cfg = API.Cfg(devIndex);
            aux = API.Aux(devIndex);
        }

        private readonly static byte[/* Light On duration */, /* Light Off duration */] BatteryIndicatorDurations =
        {
            { 28, 252 }, // on 10% of the time at 0
            { 28, 252 },
            { 56, 224 },
            { 84, 196 },
            { 112, 168 },
            { 140, 140 },
            { 168, 112 },
            { 196, 84 },
            { 224, 56 }, // on 80% of the time at 80, etc.
            { 252, 28 }, // on 90% of the time at 90
            { 0, 0 }     // use on 100%. 0 is for "charging" OR anything sufficiently-"charged"
        };

        double counter = 0.0;
        public Stopwatch fadewatch = new Stopwatch();

        private bool fadedirection = false;
        DateTime oldnow = DateTime.UtcNow;

        public bool forcedLight = false;
        public DS4Color forcedColor;
        public byte forcedFlash;
        internal const int PULSE_FLASH_DURATION = 2000;
        internal const double PULSE_FLASH_SEGMENTS = PULSE_FLASH_DURATION / 40;
        internal const int PULSE_CHARGING_DURATION = 4000;
        internal const double PULSE_CHARGING_SEGMENTS = (PULSE_CHARGING_DURATION / 40) - 2;

        public void updateLightBar(DS4Device device)
        {
            DS4Color color;
            if (!defaultLight && !forcedLight)
            {
                if (cfg.UseCustomColor)
                {
                    if (cfg.LedAsBatteryIndicator)
                    {
                        DS4Color fullColor = cfg.CustomColor;
                        DS4Color lowColor = cfg.LowColor;
                        color = Util.getTransitionedColor(ref lowColor, ref fullColor, device.getBattery());
                    }
                    else
                        color = cfg.CustomColor;
                }
                else
                {
                    double rainbow = cfg.Rainbow;
                    if (rainbow > 0)
                    {
                        // Display rainbow
                        DateTime now = DateTime.UtcNow;
                        if (now >= oldnow + TimeSpan.FromMilliseconds(10)) //update by the millisecond that way it's a smooth transtion
                        {
                            oldnow = now;
                            if (device.isCharging())
                                counter -= 1.5 * 3 / rainbow;
                            else
                                counter += 1.5 * 3 / rainbow;
                        }

                        if (counter < 0)
                            counter = 180000;
                        else if (counter > 180000)
                            counter = 0;

                        if (cfg.LedAsBatteryIndicator)
                            color = HuetoRGB((float)counter % 360, (byte)(device.getBattery() * 2.55));
                        else
                            color = HuetoRGB((float)counter % 360, 255);

                    }
                    else if (cfg.LedAsBatteryIndicator)
                    {
                        DS4Color fullColor = cfg.MainColor;
                        DS4Color lowColor = cfg.LowColor;
                        color = Util.getTransitionedColor(ref lowColor, ref fullColor, device.getBattery());
                    }
                    else
                    {
                        color = cfg.MainColor;
                    }
                }

                if (device.getBattery() <= cfg.FlashBatteryAt && !defaultLight && !device.isCharging())
                {
                    DS4Color flashColor = cfg.FlashColor;
                    if (!(flashColor.red == 0 &&
                        flashColor.green == 0 &&
                        flashColor.blue == 0))
                        color = flashColor;

                    if (cfg.FlashType == 1)
                    {
                        double ratio = 0.0;

                        if (!fadewatch.IsRunning)
                        {
                            bool temp = fadedirection;
                            fadedirection = !temp;
                            fadewatch.Restart();
                            ratio = temp ? 100.0 : 0.0;
                        }
                        else
                        {
                            long elapsed = fadewatch.ElapsedMilliseconds;

                            if (fadedirection)
                            {
                                if (elapsed < PULSE_FLASH_DURATION)
                                {
                                    elapsed = elapsed / 40;
                                    ratio = 100.0 * (elapsed / PULSE_FLASH_SEGMENTS);
                                }
                                else
                                {
                                    ratio = 100.0;
                                    fadewatch.Stop();
                                }
                            }
                            else
                            {
                                if (elapsed < PULSE_FLASH_DURATION)
                                {
                                    elapsed = elapsed / 40;
                                    ratio = (0 - 100.0) * (elapsed / PULSE_FLASH_SEGMENTS) + 100.0;
                                }
                                else
                                {
                                    ratio = 0.0;
                                    fadewatch.Stop();
                                }
                            }
                        }

                        DS4Color tempCol = new DS4Color(0, 0, 0);
                        color = Util.getTransitionedColor(ref color, ref tempCol, ratio);
                    }
                }

                int idleDisconnectTimeout = cfg.IdleDisconnectTimeout;
                if (idleDisconnectTimeout > 0 && cfg.LedAsBatteryIndicator &&
                    (!device.isCharging() || device.getBattery() >= 100))
                {
                    //Fade lightbar by idle time
                    TimeSpan timeratio = new TimeSpan(DateTime.UtcNow.Ticks - device.lastActive.Ticks);
                    double botratio = timeratio.TotalMilliseconds;
                    double topratio = TimeSpan.FromSeconds(idleDisconnectTimeout).TotalMilliseconds;
                    double ratio = 100.0 * (botratio / topratio), elapsed = ratio;
                    if (ratio >= 50.0 && ratio < 100.0)
                    {
                        DS4Color emptyCol = new DS4Color(0, 0, 0);
                        color = Util.getTransitionedColor(ref color, ref emptyCol,
                            (uint)(-100.0 * (elapsed = 0.02 * (ratio - 50.0)) * (elapsed - 2.0)));
                    }
                    else if (ratio >= 100.0)
                    {
                        DS4Color emptyCol = new DS4Color(0, 0, 0);
                        color = Util.getTransitionedColor(ref color, ref emptyCol, 100.0);
                    }
                        
                }

                if (device.isCharging() && device.getBattery() < 100)
                {
                    switch (cfg.ChargingType)
                    {
                        case 1:
                        {
                            double ratio = 0.0;

                            if (!fadewatch.IsRunning)
                            {
                                bool temp = fadedirection;
                                fadedirection = !temp;
                                fadewatch.Restart();
                                ratio = temp ? 100.0 : 0.0;
                            }
                            else
                            {
                                long elapsed = fadewatch.ElapsedMilliseconds;

                                if (fadedirection)
                                {
                                    if (elapsed < PULSE_CHARGING_DURATION)
                                    {
                                        elapsed = elapsed / 40;
                                        if (elapsed > PULSE_CHARGING_SEGMENTS)
                                            elapsed = (long)PULSE_CHARGING_SEGMENTS;
                                        ratio = 100.0 * (elapsed / PULSE_CHARGING_SEGMENTS);
                                    }
                                    else
                                    {
                                        ratio = 100.0;
                                        fadewatch.Stop();
                                    }
                                }
                                else
                                {
                                    if (elapsed < PULSE_CHARGING_DURATION)
                                    {
                                        elapsed = elapsed / 40;
                                        if (elapsed > PULSE_CHARGING_SEGMENTS)
                                            elapsed = (long)PULSE_CHARGING_SEGMENTS;
                                        ratio = (0 - 100.0) * (elapsed / PULSE_CHARGING_SEGMENTS) + 100.0;
                                    }
                                    else
                                    {
                                        ratio = 0.0;
                                        fadewatch.Stop();
                                    }
                                }
                            }

                            DS4Color emptyCol = new DS4Color(0, 0, 0);
                            color = Util.getTransitionedColor(ref color, ref emptyCol, ratio);
                            break;
                        }
                        case 2:
                        {
                            counter += 0.167;
                            color = HuetoRGB((float)counter % 360, 255);
                            break;
                        }
                        case 3:
                        {
                            color = cfg.ChargingColor;
                            break;
                        }
                        default: break;
                    }
                }
            }
            else if (forcedLight)
            {
                color = forcedColor;
            }
            else if (shuttingdown)
                color = new DS4Color(0, 0, 0);
            else
            {
                if (device.getConnectionType() == ConnectionType.BT)
                    color = new DS4Color(32, 64, 64);
                else
                    color = new DS4Color(0, 0, 0);
            }

            bool distanceprofile = cfg.DistanceProfiles || aux.TempProfileDistance;
            //distanceprofile = (ProfileExePath[deviceNum].ToLower().Contains("distance") || TempProfileName[deviceNum].ToLower().Contains("distance"));
            if (distanceprofile && !defaultLight)
            {
                // Thing I did for Distance
                float rumble = device.getLeftHeavySlowRumble() / 2.55f;
                byte max = Max(color.red, Max(color.green, color.blue));
                if (device.getLeftHeavySlowRumble() > 100)
                {
                    DS4Color maxCol = new DS4Color(max, max, 0);
                    DS4Color redCol = new DS4Color(255, 0, 0);
                    color = Util.getTransitionedColor(ref maxCol, ref redCol, rumble);
                }
                    
                else
                {
                    DS4Color maxCol = new DS4Color(max, max, 0);
                    DS4Color redCol = new DS4Color(255, 0, 0);
                    DS4Color tempCol = Util.getTransitionedColor(ref maxCol,
                        ref redCol, 39.6078f);
                    color = Util.getTransitionedColor(ref color, ref tempCol,
                        device.getLeftHeavySlowRumble());
                }
                    
            }

            DS4HapticState haptics = new DS4HapticState
            {
                LightBarColor = color
            };

            if (haptics.IsLightBarSet())
            {
                if (forcedLight && forcedFlash > 0)
                {
                    haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = (byte)(25 - forcedFlash);
                    haptics.LightBarExplicitlyOff = true;
                }
                else if (device.getBattery() <= cfg.FlashBatteryAt && cfg.FlashType == 0 && !defaultLight && !device.isCharging())
                {
                    int level = device.getBattery() / 10;
                    if (level >= 10)
                        level = 10; // all values of >~100% are rendered the same

                    haptics.LightBarFlashDurationOn = BatteryIndicatorDurations[level, 0];
                    haptics.LightBarFlashDurationOff = BatteryIndicatorDurations[level, 1];
                }
                else if (distanceprofile && device.getLeftHeavySlowRumble() > 155) //also part of Distance
                {
                    haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = (byte)((-device.getLeftHeavySlowRumble() + 265));
                    haptics.LightBarExplicitlyOff = true;
                }
                else
                {
                    //haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = 1;
                    haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = 0;
                    haptics.LightBarExplicitlyOff = true;
                }
            }
            else
            {
                haptics.LightBarExplicitlyOff = true;
            }

            byte tempLightBarOnDuration = device.getLightBarOnDuration();
            if (tempLightBarOnDuration != haptics.LightBarFlashDurationOn && tempLightBarOnDuration != 1 && haptics.LightBarFlashDurationOn == 0)
                haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = 1;

            device.SetHapticState(ref haptics);
            //device.pushHapticState(ref haptics);
        }

        public static bool defaultLight = false, shuttingdown = false;
      
        public static DS4Color HuetoRGB(float hue, byte sat)
        {
            byte C = sat;
            int X = (int)((C * (float)(1 - Math.Abs((hue / 60) % 2 - 1))));
            if (0 <= hue && hue < 60)
                return new DS4Color(C, (byte)X, 0);
            else if (60 <= hue && hue < 120)
                return new DS4Color((byte)X, C, 0);
            else if (120 <= hue && hue < 180)
                return new DS4Color(0, C, (byte)X);
            else if (180 <= hue && hue < 240)
                return new DS4Color(0, (byte)X, C);
            else if (240 <= hue && hue < 300)
                return new DS4Color((byte)X, 0, C);
            else if (300 <= hue && hue < 360)
                return new DS4Color(C, 0, (byte)X);
            else
                return new DS4Color(Color.Red);
        }
    }
}
