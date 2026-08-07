using UnityEngine;

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
    /// Deliberately minimal for the slice. No model, no animation, no footsteps: a
    /// capsule with a CharacterController and a head. What matters now is that the
    /// player occupies space and has to cross it.
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
        public float MouseSensitivity = 2.2f;

        [Tooltip("How far up and down you can look, degrees.")]
        public float PitchLimit = 85f;

        [Tooltip("The head. The camera lives here, at eye height.")]
        public Transform Head;

        [Header("Cursor")]
        [Tooltip("Lock and hide the cursor on start. Escape releases it, which you will " +
                 "want constantly while the bench handles are still dragged with the mouse.")]
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

        private void Update()
        {
            HandleCursor();

            // Only steer when the cursor is captured, so releasing it hands the mouse
            // back to the bench handles rather than spinning the room.
            if (Cursor.lockState == CursorLockMode.Locked) Look();

            Move();
        }

        private void HandleCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);

            // Clicking back into the view recaptures it, but not while the player is
            // dragging a lathe handle — that click belongs to the bench.
            if (Cursor.lockState != CursorLockMode.Locked
                && Input.GetMouseButtonDown(1))
                SetCursorLocked(true);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Look()
        {
            float yaw = Input.GetAxisRaw("Mouse X") * MouseSensitivity;
            float pitch = Input.GetAxisRaw("Mouse Y") * MouseSensitivity;

            // Yaw turns the body, pitch tilts only the head — otherwise looking down
            // would tip the whole character over.
            transform.Rotate(0f, yaw, 0f, Space.Self);

            _pitch = Mathf.Clamp(_pitch - pitch, -PitchLimit, PitchLimit);
            Head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            float forward = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float strafe = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);

            Vector3 wish = transform.forward * forward + transform.right * strafe;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = Input.GetKey(KeyCode.LeftShift) ? FastSpeed : WalkSpeed;

            // Stay pinned to the floor. There is no jumping in a workshop.
            if (_controller.isGrounded && _fallSpeed < 0f) _fallSpeed = -2f;
            _fallSpeed -= Gravity * Time.deltaTime;

            Vector3 motion = wish * speed;
            motion.y = _fallSpeed;

            _controller.Move(motion * Time.deltaTime);
        }
    }
}
