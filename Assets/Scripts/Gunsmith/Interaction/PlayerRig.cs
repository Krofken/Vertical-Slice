using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// The gunsmith: a body that stands in the shop and walks between the stations.
    ///
    /// You are a person in a room, not a cursor over a dashboard. Everything the game
    /// asks you to do — read the board, turn a bullet, weigh a charge, walk out to the
    /// yard and look at a block — is something you do by GOING THERE. That is the whole
    /// reason the evidence rack works: you walk down the row.
    ///
    /// INPUT SYSTEM, not the legacy Input class. The project has active input handling
    /// set to the Input System package, so every UnityEngine.Input call throws at
    /// runtime — which is exactly what the first version of this did. Read devices
    /// directly off Keyboard.current and Mouse.current; both are null when no device is
    /// attached, so every access is guarded.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Gunsmith/Player Rig")]
    public sealed class PlayerRig : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Walking speed, metres per second. A shop is small; do not sprint.")]
        public float WalkSpeed = 2.6f;

        [Tooltip("Held-shift speed, for crossing the yard.")]
        public float FastSpeed = 4.5f;

        [Tooltip("Downward acceleration, m/s^2. Keeps the controller on the floor.")]
        public float Gravity = 18f;

        [Header("Looking")]
        [Tooltip("Degrees per pixel of mouse movement. Input System reports raw pixel " +
                 "deltas, not the smoothed axis the old input class gave, so this is " +
                 "much smaller than a legacy sensitivity would be.")]
        public float LookSensitivity = 0.09f;

        [Tooltip("How far up and down you can look, degrees.")]
        public float PitchLimit = 85f;

        [Tooltip("The head. The camera lives here, at eye height.")]
        public Transform Head;

        [Header("Cursor")]
        [Tooltip("Lock and hide the cursor on start. Escape releases it, which you want " +
                 "whenever you are working the bench rather than walking.")]
        public bool LockCursor = true;

        private CharacterController _controller;
        private float _pitch;
        private float _fallSpeed;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (Head == null) Head = transform;
            _pitch = Head.localEulerAngles.x;
        }

        private void Start()
        {
            if (LockCursor) SetCursorLocked(true);
        }

        private void OnDisable() => SetCursorLocked(false);

        private void Update()
        {
            HandleCursor();

            // Only steer while the cursor is captured, so releasing it hands the mouse
            // back to the bench without spinning the room.
            if (Cursor.lockState == CursorLockMode.Locked) Look();

            Move();
        }

        private void HandleCursor()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);

            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState != CursorLockMode.Locked
                && mouse.rightButton.wasPressedThisFrame)
                SetCursorLocked(true);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Look()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 delta = mouse.delta.ReadValue() * LookSensitivity;

            // Yaw turns the body, pitch tilts only the head — otherwise looking down
            // would tip the whole character over.
            transform.Rotate(0f, delta.x, 0f, Space.Self);

            _pitch = Mathf.Clamp(_pitch - delta.y, -PitchLimit, PitchLimit);
            Head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            var keyboard = Keyboard.current;

            float forward = 0f, strafe = 0f;
            bool hurry = false;

            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) forward += 1f;
                if (keyboard.sKey.isPressed) forward -= 1f;
                if (keyboard.dKey.isPressed) strafe += 1f;
                if (keyboard.aKey.isPressed) strafe -= 1f;
                hurry = keyboard.leftShiftKey.isPressed;
            }

            Vector3 wish = transform.forward * forward + transform.right * strafe;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            // Stay pinned to the floor. There is no jumping in a workshop.
            if (_controller.isGrounded && _fallSpeed < 0f) _fallSpeed = -2f;
            _fallSpeed -= Gravity * Time.deltaTime;

            Vector3 motion = wish * (hurry ? FastSpeed : WalkSpeed);
            motion.y = _fallSpeed;

            _controller.Move(motion * Time.deltaTime);
        }
    }
}
