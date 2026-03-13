//This script lets you change time scale during training. It is not a required script for this demo to function

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MLAgentsExamples
{
    public class AdjustTrainingTimescale : MonoBehaviour
    {
        // Update is called once per frame
        void Update()
        {
            int selectedScale = GetSelectedTimescale();
            if (selectedScale > 0)
            {
                Time.timeScale = selectedScale;
                return;
            }

            if (IsDoubleTimescalePressed())
            {
                Time.timeScale *= 2f;
            }
        }

        private static int GetSelectedTimescale()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) return 1;
                if (keyboard.digit2Key.wasPressedThisFrame) return 2;
                if (keyboard.digit3Key.wasPressedThisFrame) return 3;
                if (keyboard.digit4Key.wasPressedThisFrame) return 4;
                if (keyboard.digit5Key.wasPressedThisFrame) return 5;
                if (keyboard.digit6Key.wasPressedThisFrame) return 6;
                if (keyboard.digit7Key.wasPressedThisFrame) return 7;
                if (keyboard.digit8Key.wasPressedThisFrame) return 8;
                if (keyboard.digit9Key.wasPressedThisFrame) return 9;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha2)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha3)) return 3;
            if (Input.GetKeyDown(KeyCode.Alpha4)) return 4;
            if (Input.GetKeyDown(KeyCode.Alpha5)) return 5;
            if (Input.GetKeyDown(KeyCode.Alpha6)) return 6;
            if (Input.GetKeyDown(KeyCode.Alpha7)) return 7;
            if (Input.GetKeyDown(KeyCode.Alpha8)) return 8;
            if (Input.GetKeyDown(KeyCode.Alpha9)) return 9;
#endif
            return 0;
        }

        private static bool IsDoubleTimescalePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.digit0Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Alpha0);
#else
            return false;
#endif
        }
    }
}
