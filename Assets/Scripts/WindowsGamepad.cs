using System;
using System.Runtime.InteropServices;

namespace FruitFlyJoust
{
    // Windows' built-in API: no downloadable native plug-in or Unity package needed.
    public static class WindowsGamepad
    {
        public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, RightStick = 0x0080,
            LeftBumper = 0x0100, RightBumper = 0x0200, Start = 0x0010, DPadDown = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct PadState
        {
            public ushort buttons;
            public byte leftTrigger, rightTrigger;
            public short leftX, leftY, rightX, rightY;
            public bool Held(ushort mask) { return (buttons & mask) != 0; }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeState { public uint packet; public PadState pad; }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState(uint index, out NativeState state);
        private static bool unavailable;
        private static int selected = -1;

        public static bool Read(out PadState state)
        {
            state = default(PadState);
            if (unavailable || Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            try
            {
                if (selected >= 0 && GetState((uint)selected, out var active) == 0)
                { state = active.pad; return true; }
                selected = -1;
                for (uint i = 0; i < 4; i++)
                    if (GetState(i, out var candidate) == 0)
                    { selected = (int)i; state = candidate.pad; return true; }
            }
            catch (DllNotFoundException) { unavailable = true; }
            catch (EntryPointNotFoundException) { unavailable = true; }
            return false;
        }

        public static void Stick(float x, float y, float deadZone, out float horizontal, out float vertical)
        {
            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= deadZone) { horizontal = vertical = 0; return; }
            double amplitude = Math.Min(1, (magnitude - deadZone) / (1 - deadZone));
            horizontal = (float)(x / magnitude * amplitude);
            vertical = (float)(y / magnitude * amplitude);
        }

        public static float Axis(short value) { return value < 0 ? value / 32768f : value / 32767f; }
        public static float Trigger(byte value) { return Math.Max(0, (value - 30) / 225f); }
        public static bool Pressed(ushort now, ushort before, ushort mask)
        { return (now & mask) != 0 && (before & mask) == 0; }
    }
}
