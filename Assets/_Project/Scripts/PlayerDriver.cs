using UnityEngine;
using UnityEngine.InputSystem;

namespace Racing
{
    // Keyboard (WASD / arrows / Space) and gamepad (triggers, left stick, South) input.
    [RequireComponent(typeof(CarController))]
    public class PlayerDriver : MonoBehaviour
    {
        CarController car;
        Nitro nitro;
        float keyboardSteer;

        void Awake()
        {
            car = GetComponent<CarController>();
            nitro = GetComponent<Nitro>();
        }

        void OnDisable()
        {
            keyboardSteer = 0f;
            if (nitro) nitro.Request = false;
        }

        void Update()
        {
            float throttle = 0f, steer = 0f;
            bool handbrake = false, boost = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                float kt = 0f, ks = 0f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) kt += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) kt -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) ks -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) ks += 1f;
                float rate = ks == 0f || Mathf.Sign(ks) != Mathf.Sign(keyboardSteer) ? 9f : 5f;
                keyboardSteer = Mathf.MoveTowards(keyboardSteer, ks, rate * Time.deltaTime);
                throttle += kt;
                steer += keyboardSteer;
                handbrake |= kb.spaceKey.isPressed;
                boost |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }

            var gp = Gamepad.current;
            if (gp != null)
            {
                throttle += gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
                float stick = gp.leftStick.x.ReadValue();
                steer += Mathf.Abs(stick) > 0.12f ? stick : 0f;
                handbrake |= gp.buttonSouth.isPressed;
                boost |= gp.buttonWest.isPressed;
            }

            car.Throttle = Mathf.Clamp(throttle, -1f, 1f);
            car.Steer = Mathf.Clamp(steer, -1f, 1f);
            car.Handbrake = handbrake;
            if (nitro) nitro.Request = boost;
        }
    }
}
