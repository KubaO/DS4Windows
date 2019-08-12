using System;

namespace DS4Windows
{

    public struct YawPitchRollInt
    {
        public int Yaw, Pitch, Roll;

        public static YawPitchRollDouble operator /(YawPitchRollInt l, double r)
            => (YawPitchRollDouble)l / r;

        public static YawPitchRollInt operator /(YawPitchRollInt l, int r)
        {
            l.Yaw /= r;
            l.Pitch /= r;
            l.Roll /= r;
            return l;
        }
    }

    public struct YawPitchRollDouble
    {
        public double Yaw, Pitch, Roll;

        public static implicit operator YawPitchRollDouble(YawPitchRollInt o)
            => new YawPitchRollDouble { Yaw = o.Yaw, Pitch = o.Pitch, Roll = o.Roll };

        public static YawPitchRollDouble operator /(YawPitchRollDouble l, double r)
        {
            l.Yaw /= r;
            l.Pitch /= r;
            l.Roll /= r;
            return l;
        }
    }

    public struct Vector3Int
    {
        public int X, Y, Z;

        public bool isNonZero => X != 0 || Y != 0 || Z != 0;

        public static Vector3Double operator /(Vector3Int l, double r)
            => (Vector3Double) l / r;

        public static Vector3Int operator /(Vector3Int l, int r)
        {
            l.X /= r;
            l.Y /= r;
            l.Z /= r;
            return l;
        }
    }

    public struct Vector3Double
    {
        public double X, Y, Z;

        public static implicit operator Vector3Double(Vector3Int o)
            => new Vector3Double {X = o.X, Y = o.Y, Z = o.Z};

        public static Vector3Double operator /(Vector3Double l, double r)
        {
            l.X /= r;
            l.Y /= r;
            l.Z /= r;
            return l;
        }
    }
}
