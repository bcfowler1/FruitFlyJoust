using UnityEngine;

namespace FruitFlyJoust
{
    [DefaultExecutionOrder(-100)]
    public sealed class RiderInput : MonoBehaviour
    {
        public Vector2 reins, look;
        public float lift, roll;
        public bool spur, land, recenter, resetRide;
        public bool gamepad;
        public float primaryAction, secondaryAction;
        public string controllerStatus = "Keyboard + mouse";
        [Range(.05f, .45f)] public float reinDeadZone = .18f;
        [Range(.05f, .45f)] public float lookDeadZone = .2f;
        [Range(1, 2.5f)] public float lookResponse = 1.5f;
        public bool invertLookY;
        private ushort previousButtons;
        [Min(.1f)] public float landingHoldSeconds = .65f;
        private bool spurHeldBefore, brakeHeldBefore;
        private bool pendingSpur, pendingBrake;
        private float brakeHoldTime;
        public bool braking;

        // Latch press cues until the physics loop consumes them, even for short taps.
        public bool ConsumeSpur() { bool cue = pendingSpur; pendingSpur = false; return cue; }
        public bool ConsumeBrake() { bool cue = pendingBrake; pendingBrake = false; return cue; }
        public void RequestSpur() { pendingSpur = true; }
        public void RequestBrake() { pendingBrake = true; }
        public void ResetCues()
        {
            pendingSpur = pendingBrake = false;
            brakeHoldTime = 0;
            land = false;
        }

        Vector2 Stick(float x, float y, float deadZone)
        {
            WindowsGamepad.Stick(x, y, deadZone, out float horizontal, out float vertical);
            return new Vector2(horizontal, vertical);
        }

        void Update()
        {
            reins = look = Vector2.zero;
            lift = roll = primaryAction = secondaryAction = 0;
            spur = land = recenter = resetRide = false;
            if (!Application.isFocused)
            {
                previousButtons = 0; spurHeldBefore = brakeHeldBefore = braking = false;
                ResetCues(); return;
            }
            bool native = WindowsGamepad.Read(out var pad);
            gamepad = native || System.Array.Exists(Input.GetJoystickNames(), n => !string.IsNullOrEmpty(n));
            controllerStatus = native ? "Xbox-compatible gamepad" : gamepad ? "Legacy gamepad" : "Keyboard + mouse";
            if (native)
            {
                reins = Stick(WindowsGamepad.Axis(pad.leftX), WindowsGamepad.Axis(pad.leftY), reinDeadZone);
                look = Stick(WindowsGamepad.Axis(pad.rightX), WindowsGamepad.Axis(pad.rightY), lookDeadZone);
                roll = (pad.Held(WindowsGamepad.LeftBumper) ? 1 : 0) - (pad.Held(WindowsGamepad.RightBumper) ? 1 : 0);
                spur = pad.Held(WindowsGamepad.A);
                land = pad.Held(WindowsGamepad.B);
                recenter = WindowsGamepad.Pressed(pad.buttons, previousButtons, WindowsGamepad.RightStick);
                resetRide = WindowsGamepad.Pressed(pad.buttons, previousButtons, WindowsGamepad.Start);
                primaryAction = WindowsGamepad.Trigger(pad.rightTrigger);
                secondaryAction = WindowsGamepad.Trigger(pad.leftTrigger);
                previousButtons = pad.buttons;
            }
            else
            {
                previousButtons = 0;
                // Windows legacy joystick axes use negative Y for stick-forward.
                reins = Stick(Input.GetAxisRaw("ReinHorizontal"), -Input.GetAxisRaw("ReinVertical"), reinDeadZone);
                look = Stick(Input.GetAxisRaw("LookHorizontal"), -Input.GetAxisRaw("LookVertical"), lookDeadZone);
                roll = (Input.GetKey(KeyCode.JoystickButton4) ? 1 : 0) - (Input.GetKey(KeyCode.JoystickButton5) ? 1 : 0);
                spur = Input.GetKey(KeyCode.JoystickButton0);
                land = Input.GetKey(KeyCode.JoystickButton1);
                recenter = Input.GetKeyDown(KeyCode.JoystickButton9);
                resetRide = Input.GetKeyDown(KeyCode.JoystickButton7);
            }
            look *= Mathf.Pow(look.magnitude, lookResponse - 1);
            if (invertLookY) look.y = -look.y;
            reins += new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            reins = Vector2.ClampMagnitude(reins, 1);
            lift = Mathf.Clamp(lift + (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0), -1, 1);
            roll = Mathf.Clamp(roll + (Input.GetKey(KeyCode.Z) ? 1 : 0) - (Input.GetKey(KeyCode.V) ? 1 : 0), -1, 1);
            spur |= Input.GetKey(KeyCode.Space);
            land |= Input.GetKey(KeyCode.L);
            braking = land;
            if (spur && !spurHeldBefore) pendingSpur = true;
            if (braking && !brakeHeldBefore) pendingBrake = true;
            brakeHoldTime = braking ? brakeHoldTime + Time.deltaTime : 0;
            land = braking && brakeHoldTime >= landingHoldSeconds;
            spurHeldBefore = spur;
            brakeHeldBefore = braking;
            recenter |= Input.GetKeyDown(KeyCode.R);
            resetRide |= Input.GetKeyDown(KeyCode.Backspace);
            if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;
            if (Input.GetMouseButtonDown(0)) Cursor.lockState = CursorLockMode.Locked;
        }
        void OnDisable()
        {
            reins = look = Vector2.zero; lift = roll = primaryAction = secondaryAction = 0;
            spur = land = recenter = resetRide = false; previousButtons = 0;
            spurHeldBefore = brakeHeldBefore = braking = false; ResetCues();
            Cursor.lockState = CursorLockMode.None;
        }
    }
}
