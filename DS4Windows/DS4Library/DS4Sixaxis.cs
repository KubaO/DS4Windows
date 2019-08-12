using System;
using System.Linq;

namespace DS4Windows
{
    public delegate void SixAxisHandler<TEventArgs>(DS4SixAxis sender, TEventArgs args);

    public class SixAxisEventArgs : EventArgs
    {
        public readonly SixAxis sixAxis;
        public readonly DateTime timeStamp;
        public SixAxisEventArgs(DateTime utcTimestamp, SixAxis sa)
        {
            sixAxis = sa;
            timeStamp = utcTimestamp;
        }
    }

    public class SixAxis
    {
        public const int ACC_RES_PER_G = 8192;
        private const float F_ACC_RES_PER_G = ACC_RES_PER_G;
        public const int GYRO_RES_IN_DEG_SEC = 16;
        private const float F_GYRO_RES_IN_DEG_SEC = GYRO_RES_IN_DEG_SEC;

        public YawPitchRollInt gyroFull;
        public Vector3Int accelFull;
        public YawPitchRollInt gyro;
        public Vector3Int accel, outputAccel;
        public YawPitchRollDouble angVel;
        public Vector3Double accelG;
        public double elapsed;
        public SixAxis previousAxis = null;

        public SixAxis() { } // default values are OK

        public SixAxis(int X, int Y, int Z,
            int aX, int aY, int aZ,
            double elapsedDelta, SixAxis prevAxis = null)
        {
            populate(new YawPitchRollInt { Yaw = X, Pitch = Y, Roll = Z },
                new Vector3Int { X = aX, Y = aY, Z = aZ }, elapsedDelta, prevAxis);
        }

        public void populate(YawPitchRollInt gyroIn, Vector3Int accelIn,
            double elapsedDelta, SixAxis prevAxis = null)
        {
            gyroFull.Yaw   = -gyroIn.Yaw;
            gyroFull.Pitch =  gyroIn.Pitch;
            gyroFull.Roll  = -gyroIn.Roll;
            gyro = gyroFull / 256;

            // Put accel ranges between 0 - 128 abs
            accelFull.X = -accelIn.X;
            accelFull.Y = -accelIn.Y;
            accelFull.Z =  accelIn.Z;
            accel = accelFull / 64;
            outputAccel = accel;

            angVel = gyroFull / F_GYRO_RES_IN_DEG_SEC;
            accelG = accelFull / F_ACC_RES_PER_G;

            elapsed = elapsedDelta;
            previousAxis = prevAxis;
        }
    }

    internal struct CalibData
    {
        public int bias;
        public Int64 sensitivity; // 32.32 format
        public const int GyroPitchIdx = 0, GyroYawIdx = 1, GyroRollIdx = 2,
        AccelXIdx = 3, AccelYIdx = 4, AccelZIdx = 5;
    }

    public class DS4SixAxis
    {
        public event SixAxisHandler<SixAxisEventArgs> SixAccelMoved = null;
        private SixAxis sPrev = new SixAxis(), now = new SixAxis();
        private CalibData[] calibrationData = new CalibData[6];
        private bool calibrationDone;

        public DS4SixAxis() { }

        private struct PlusMinus
        {
            public int Plus, Minus;
            public int Range { get => Plus - Minus;  }
        }

        private struct Frac
        {
            // 32.32 / 32.0
            public Int64 Numer { get => Numer; set => Numer = ((Int64)value) << 32; }
            public int Denom;
        }

        public void setCalibrationData(ref byte[] calibData, bool fromUSB)
        {
            PlusMinus pitch, yaw, roll, accelX, accelY, accelZ, gyroSpeed;

            short getShort(ref byte[] data, int index)
                => (short)((ushort)(data[index * 2 + 2] << 8) | data[index * 2 + 1]);

            calibrationData[0].bias = getShort(ref calibData, 0);
            calibrationData[1].bias = getShort(ref calibData, 1);
            calibrationData[2].bias = getShort(ref calibData, 2);

            if (!fromUSB)
            {
                pitch.Plus = getShort(ref calibData, 3);
                yaw.Plus = getShort(ref calibData, 4);
                roll.Plus = getShort(ref calibData, 5);
                pitch.Minus = getShort(ref calibData, 6);
                yaw.Minus = getShort(ref calibData, 7);
                roll.Minus = getShort(ref calibData, 8);
            }
            else
            {
                pitch.Plus = getShort(ref calibData, 3);
                pitch.Minus = getShort(ref calibData, 4);
                yaw.Plus = getShort(ref calibData, 5);
                yaw.Minus = getShort(ref calibData, 6);
                roll.Plus = getShort(ref calibData, 7);
                roll.Minus = getShort(ref calibData, 8);
            }

            gyroSpeed.Plus = getShort(ref calibData, 9);
            gyroSpeed.Minus = getShort(ref calibData, 10);

            accelX.Plus = getShort(ref calibData, 11);
            accelX.Minus = getShort(ref calibData, 12);
            accelY.Plus = getShort(ref calibData, 13);
            accelY.Minus = getShort(ref calibData, 14);
            accelZ.Plus = getShort(ref calibData, 15);
            accelZ.Minus = getShort(ref calibData, 16);

            Frac[] fractions = new Frac[6];
            int gyroSpeed2x = gyroSpeed.Range;

            fractions[0].Numer = gyroSpeed2x * SixAxis.GYRO_RES_IN_DEG_SEC;
            fractions[0].Denom = pitch.Range;

            fractions[1].Numer = gyroSpeed2x * SixAxis.GYRO_RES_IN_DEG_SEC;
            fractions[1].Denom = yaw.Range;

            fractions[2].Numer = gyroSpeed2x * SixAxis.GYRO_RES_IN_DEG_SEC;
            fractions[2].Denom = roll.Range;

            calibrationData[3].bias = accelX.Plus - accelX.Range / 2;
            fractions[3].Numer = 2 * SixAxis.ACC_RES_PER_G;
            fractions[3].Denom = accelX.Range;

            calibrationData[4].bias = accelY.Plus - accelY.Range / 2;
            fractions[4].Numer = 2 * SixAxis.ACC_RES_PER_G;
            fractions[4].Denom = accelY.Range;

            calibrationData[5].bias = accelZ.Plus - accelZ.Range / 2;
            fractions[5].Numer = 2 * SixAxis.ACC_RES_PER_G;
            fractions[5].Denom = accelZ.Range;

            calibrationDone = fractions.All(frac => frac.Denom != 0);
            if (calibrationDone) {
                // Pre-divide the sensitivity values into 32.32 format:
                // 32.32 / 32.0 = 32.32
                for (int i = 0; i < calibrationData.Length; i++)
                {
                    calibrationData[i].sensitivity = fractions[i].Numer / fractions[i].Denom;
                }
            }
        }

        private void applyCalibs(ref YawPitchRollInt gyro, ref Vector3Int accel)
        {
            ref var cal = ref calibrationData;

            gyro.Pitch = (int)(((gyro.Pitch - cal[0].bias) * cal[0].sensitivity) >> 32);
            gyro.Yaw   = (int)(((gyro.Yaw   - cal[1].bias) * cal[1].sensitivity) >> 32);
            gyro.Roll  = (int)(((gyro.Roll  - cal[2].bias) * cal[2].sensitivity) >> 32);

            accel.X = (int)(((accel.X - cal[3].bias) * cal[3].sensitivity) >> 32);
            accel.Y = (int)(((accel.Y - cal[4].bias) * cal[4].sensitivity) >> 32);
            accel.Z = (int)(((accel.Z - cal[5].bias) * cal[5].sensitivity) >> 32);
        }

        public unsafe void handleSixaxis(byte* gyroRaw, byte* accelRaw, DS4State state,
            double elapsedDelta)
        {
            short getShort(byte* data, int index)
                => (short)((ushort)(data[index * 2 + 1] << 8) | data[index * 2 + 0]);

            YawPitchRollInt gyro;
            gyro.Yaw = getShort(gyroRaw, 1);
            gyro.Pitch = getShort(gyroRaw, 0);
            gyro.Roll = getShort(gyroRaw, 2);
            Vector3Int accel;
            accel.X = getShort(accelRaw, 0);
            accel.Y = getShort(accelRaw, 1);
            accel.Z = getShort(accelRaw, 2);

            if (calibrationDone)
                applyCalibs(ref gyro, ref accel);

            if (accel.isNonZero && SixAccelMoved != null)
            {
                swap(ref sPrev, ref now);

                now.populate(gyro, accel, elapsedDelta, sPrev);

                var args = new SixAxisEventArgs(state.ReportTimeStamp, now);
                state.Motion = now;
                SixAccelMoved(this, args);
            }
        }

        private static void swap<T>(ref T l, ref T r) where T : class
        {
            ref T temp = ref l;
            l = r;
            r = temp;
        }
    }
}
