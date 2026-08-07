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

        [Header("Leaning in")]
        [Tooltip("Seconds to move between standing and working at a station.")]
        public float FocusSeconds = 0.28f;

        private CharacterController _controller;
        private float _pitch;
        private float _fallSpeed;

        // ---- Focus ---------------------------------------------------------
        private StationView _focus;
        private float _blend;
        private Vector3 _restLocalPosition;
        private float _restFieldOfView = 60f;
        private Camera _camera;

        // Held so the blend BACK to standing still has somewhere to come from after
        // the station reference is cleared.
        private Vector3 _focusPosition;
        private Quaternion _focusRotation;
        private float _focusFieldOfView = 22f;

        /// <summary>The station being leaned over, or null when standing.</summary>
        public StationView Focused => _focus;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (Head == null) Head = transform;
            _pitch = Head.localEulerAngles.x;

            _restLocalPosition = Head.localPosition;
            _camera = Head.GetComponentInChildren<Camera>();
            if (_camera == null) _camera = Camera.main;
            if (_camera != null) _restFieldOfView = _camera.fieldOfView;
        }

        /// <summary>
        /// Leans in over a station: the eye moves to its working position and the field
        /// of view narrows to a loupe. The cursor is released, because at the bench the
        /// mouse belongs to the work rather than to looking around.
        /// </summary>
        public void Focus(StationView station)
        {
            if (station == null) return;

            _focus = station;
            _focusFieldOfView = station.FieldOfView;
            SetCursorLocked(false);
        }

        /// <summary>Stands back up.</summary>
        public void Unfocus()
        {
            _focus = null;
            SetCursorLocked(true);
        }

        /// <summary>Leans in, or stands up if already leaning over this one.</summary>
        public void ToggleFocus(StationView station)
        {
            if (_focus == station) Unfocus();
            else Focus(station);
        }

        private void Start()
        {
            if (LockCursor) SetCursorLocked(true);
        }

        private void OnDisable() => SetCursorLocked(false);

        private void Update()
        {
            HandleCursor();

            // Standing still while leaning over a station. Walking away from the bench
            // with your eye still on it would be nonsense, and the mouse is busy.
            if (_focus != null) return;

            // Only steer while the cursor is captured, so releasing it hands the mouse
            // back to the bench without spinning the room.
            if (Cursor.lockState == CursorLockMode.Locked) Look();

            Move();
        }

        private void HandleCursor()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                // Escape stands you up first. Only once you are standing does it hand
                // the cursor back — otherwise one key would do two things at once and
                // you could never leave a station without also losing mouse look.
                if (_focus != null) Unfocus();
                else SetCursorLocked(false);
            }

            var mouse = Mouse.current;
            if (mouse != null && _focus == null && Cursor.lockState != CursorLockMode.Locked
                && mouse.rightButton.wasPressedThisFrame)
                SetCursorLocked(true);
        }

        /// <summary>
        /// Drives the eye between standing and leaning in.
        ///
        /// LateUpdate, and it writes Head's WORLD pose directly. The head is a child of
        /// the body, so anything written in Update would be overwritten by the parent's
        /// own movement in the same frame.
        /// </summary>
        private void LateUpdate()
        {
            float target = _focus != null ? 1f : 0f;

            if (FocusSeconds > 0.001f)
                _blend = Mathf.MoveTowards(_blend, target, Time.deltaTime / FocusSeconds);
            else
                _blend = target;

            // Standing, and settled: leave the head exactly where the look code put it.
            if (_blend <= 0.0001f)
            {
                Head.localPosition = _restLocalPosition;
                if (_camera != null) _camera.fieldOfView = _restFieldOfView;
                return;
            }

            if (_focus != null)
            {
                _focusPosition = _focus.EyePosition;
                _focusRotation = _focus.EyeRotation;
            }

            // Where the eye would be if it were not leaning in at all.
            Vector3 standingPosition = transform.TransformPoint(_restLocalPosition);
            Quaternion standingRotation = transform.rotation * Quaternion.Euler(_pitch, 0f, 0f);

            Head.SetPositionAndRotation(
                Vector3.Lerp(standingPosition, _focusPosition, _blend),
                Quaternion.Slerp(standingRotation, _focusRotation, _blend));

            if (_camera != null)
                _camera.fieldOfView = Mathf.Lerp(_restFieldOfView, _focusFieldOfView, _blend);
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
