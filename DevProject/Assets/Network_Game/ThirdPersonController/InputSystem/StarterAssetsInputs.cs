using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Network_Game.ThirdPersonController
{
    /// <summary>
    /// Reads input from the Unity Input System and exposes it as simple public fields
    /// that the ThirdPersonController and other scripts can poll each frame.
    /// Supports: Move, Look, Jump, Sprint, Crouch.
    /// </summary>
    public class StarterAssetsInputs : MonoBehaviour
    {
        [Header("Character Input Values")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool sprint;
        public bool crouch;
        public bool interact;
        public bool pause;

        [Header("Movement Settings")]
        public bool analogMovement;

        [Header("Mouse Cursor Settings")]
        public bool cursorLocked = true;
        public bool cursorInputForLook = true;

        /// <summary>
        /// When true, movement and jump inputs are ignored (e.g. while typing in dialogue UI).
        /// </summary>
        [HideInInspector]
        public bool inputBlocked;

        // PlayerInput drives this component via SendMessage (On* callbacks above).
        // No manual Awake wiring needed.

#if ENABLE_INPUT_SYSTEM
        // PlayerInput uses SendMessage to call these On* methods — keep exactly these names.
        public void OnMove(InputValue value) => MoveInput(value.Get<Vector2>());

        public void OnLook(InputValue value)
        {
            if (cursorInputForLook)
                LookInput(value.Get<Vector2>());
        }

        public void OnJump(InputValue value) => JumpInput(value.isPressed);

        public void OnSprint(InputValue value) => SprintInput(value.isPressed);

        public void OnCrouch(InputValue value) => CrouchInput(!crouch);

        public void OnInteract(InputValue value) => InteractInput(value.isPressed);

        public void OnPause(InputValue value) => PauseInput(value.isPressed);
#endif

        public void MoveInput(Vector2 newMoveDirection)
        {
            move = inputBlocked ? Vector2.zero : newMoveDirection;
        }

        public void LookInput(Vector2 newLookDirection)
        {
            look = newLookDirection;
        }

        public void JumpInput(bool newJumpState)
        {
            if (!inputBlocked)
                jump = newJumpState;
        }

        public void SprintInput(bool newSprintState)
        {
            if (!inputBlocked)
                sprint = newSprintState;
        }

        public void CrouchInput(bool newCrouchState)
        {
            if (!inputBlocked)
                crouch = newCrouchState;
        }

        public void InteractInput(bool newInteractState)
        {
            interact = newInteractState;
        }

        public void PauseInput(bool newPauseState)
        {
            pause = newPauseState;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            SetCursorState(cursorLocked);
        }

        public void SetCursorState(bool newState)
        {
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
        }
    }
}
