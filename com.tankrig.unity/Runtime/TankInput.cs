using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TankRig
{
    /// <summary>One frame of player input (keyboard + mouse).</summary>
    [System.Serializable]
    public struct TankInputState
    {
        public float throttle;      // W / S
        public float steer;         // A / D
        public Vector2 look;        // mouse delta (pixels)
        public float scroll;        // mouse wheel (notches)
        public bool fireMain;       // left click (this frame)
        public bool fireMachineGun; // Space (held)
        public bool aim;            // right mouse (held): zoom
        public bool toggleLights;   // L (this frame)
        public bool unlockCursor;   // Esc (this frame)
        public bool click;          // left click (this frame), used to lock the cursor again
    }

    public static class TankInput
    {
        public static TankInputState Read()
        {
            var s = new TankInputState();
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var m = Mouse.current;
            if (k != null)
            {
                s.throttle = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                s.steer = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                s.fireMachineGun = k.spaceKey.isPressed;
                s.toggleLights = k.lKey.wasPressedThisFrame;
                s.unlockCursor = k.escapeKey.wasPressedThisFrame;
            }
            if (m != null)
            {
                s.look = m.delta.ReadValue();
                // Windows reports 120 per wheel notch, other platforms / settings about 1
                float sy = m.scroll.ReadValue().y;
                s.scroll = Mathf.Abs(sy) >= 10f ? sy / 120f : sy;
                s.fireMain = m.leftButton.wasPressedThisFrame;
                s.click = s.fireMain;
                s.aim = m.rightButton.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            s.throttle = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            s.steer = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            s.fireMachineGun = Input.GetKey(KeyCode.Space);
            s.toggleLights = Input.GetKeyDown(KeyCode.L);
            s.unlockCursor = Input.GetKeyDown(KeyCode.Escape);
            s.look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
            s.scroll = Input.mouseScrollDelta.y;
            s.fireMain = Input.GetMouseButtonDown(0);
            s.click = s.fireMain;
            s.aim = Input.GetMouseButton(1);
#endif
            return s;
        }
    }
}
