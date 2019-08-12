
namespace DS4Windows
{
    public class DS4StateExposed
    {
        private DS4State _state;

        public DS4StateExposed()
        {
            _state = new DS4State();
        }

        public DS4StateExposed(DS4State state)
        {
            _state = state;
        }

        bool Square { get => _state.Square; }
        bool Triangle { get => _state.Triangle; }
        bool Circle { get => _state.Circle; }
        bool Cross { get => _state.Cross; }
        bool DpadUp { get => _state.DpadUp; }
        bool DpadDown { get => _state.DpadDown; }
        bool DpadLeft { get => _state.DpadLeft; }
        bool DpadRight { get => _state.DpadRight; }
        bool L1 { get => _state.L1; }
        bool L3 { get => _state.L3; }
        bool R1 { get => _state.R1; }
        bool R3 { get => _state.R3; }
        bool Share { get => _state.Share; }
        bool Options { get => _state.Options; }
        bool PS { get => _state.PS; }
        bool Touch1 { get => _state.Touch1; }
        bool Touch2 { get => _state.Touch2; }
        bool TouchButton { get => _state.TouchButton; }
        bool Touch1Finger { get => _state.Touch1Finger; }
        bool Touch2Fingers { get => _state.Touch2Fingers; }
        byte LX { get => _state.LX; }
        byte RX { get => _state.RX; }
        byte LY { get => _state.LY; }
        byte RY { get => _state.RY; }
        byte L2 { get => _state.L2; }
        byte R2 { get => _state.R2; }
        int Battery { get => _state.Battery; }

        public int GyroYaw   { get => _state.Motion.gyro.Yaw; }
        public int GyroPitch { get => _state.Motion.gyro.Pitch; }
        public int GyroRoll  { get => _state.Motion.gyro.Roll; }

        public int AccelX { get => _state.Motion.accel.X; }
        public int AccelY { get => _state.Motion.accel.Y; }
        public int AccelZ { get => _state.Motion.accel.Z; }

        public int OutputAccelX { get => _state.Motion.outputAccel.X; }
        public int OutputAccelY { get => _state.Motion.outputAccel.Y; }
        public int OutputAccelZ { get => _state.Motion.outputAccel.Z; }
    }
}
