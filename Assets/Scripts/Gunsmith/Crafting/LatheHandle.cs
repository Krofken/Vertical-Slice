using Gunsmith.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// One dimension you can take hold of.
    ///
    /// Each handle moves along exactly ONE axis and cuts exactly ONE dimension. That
    /// restriction is deliberate and is the most valuable thing about the bench: if
    /// changing one variable is one drag and changing three is three drags, a player
    /// naturally runs controlled experiments and learns causality fast. If everything
    /// moved at once they would change everything and learn nothing.
    ///
    /// AIMED, NOT CLICKED. This used to hang off OnMouseDrag, which does not fire at all
    /// once the project switches to the Input System package — and it read the cursor's
    /// screen position, which is meaningless while the cursor is locked for mouse-look.
    /// So the handle is grabbed the same way everything else in the shop is used: you
    /// look at it and hold the left button, and then steering the mouse works the
    /// dimension. It behaves identically whether the cursor is locked or free.
    ///
    /// Dragging finds the point on the handle's axis nearest the aim ray, rather than
    /// projecting onto a plane. A plane fails badly when you are looking down the axis;
    /// the nearest-point solution simply stops responding, which is the correct
    /// behaviour for a slide viewed end-on.
    ///
    /// AND IT STILL DID NOT DRAG, for a third reason that had nothing to do with either
    /// of those: the station's own lean-in trigger box occluded every handle inside it,
    /// so the grab ray never reached one. That trap and the measurement that found it are
    /// documented on <see cref="Aim"/>, which is now the only thing deciding what is being
    /// pointed at — shared with the seating die, so the same bug cannot be fixed here and
    /// left standing there.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Gunsmith/Lathe Handle")]
    public sealed class LatheHandle : MonoBehaviour
    {
        [Tooltip("Which dimension this handle cuts.")]
        public LatheOperation Operation;

        [Tooltip("The lathe this belongs to.")]
        public LatheStation Station;

        [Tooltip("Camera used for aiming. Falls back to the main camera, which is the " +
                 "player's head once the shop is running.")]
        public Camera Rig;

        [Tooltip("How far you can be and still grab a handle, metres.")]
        public float Reach = 2.4f;

        private bool _dragging;

        private Camera Aiming => Rig != null ? Rig : Camera.main;

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame) TryGrab(mouse);
            else if (!mouse.leftButton.isPressed) _dragging = false;

            if (_dragging) Drag(mouse);
        }

        /// <summary>Grabs this handle if the player is looking at it.</summary>
        private void TryGrab(Mouse mouse)
        {
            var camera = Aiming;
            if (camera == null || Station == null || Station.Rig == null) return;

            _dragging = Aim.IsUnderAim(camera, gameObject, Reach, mouse);
        }

        private void Drag(Mouse mouse)
        {
            var camera = Aiming;
            if (camera == null || Station == null || Station.Rig == null) return;

            // The axis this handle slides on, in world space.
            Vector3 axisLocal = LatheStation.AxisOf(Operation);
            Vector3 axisWorld = Station.Rig.TransformDirection(axisLocal).normalized;
            Vector3 origin = Station.Rig.position;

            if (!Aim.ClosestPointOnAxis(origin, axisWorld, Aim.Ray(camera, mouse), out Vector3 point)) return;

            // Distance along the axis, in rig-local metres. lossyScale undoes the display
            // scaling the rig applies so the projectile can be seen at all.
            float scale = Station.Rig.lossyScale.z;
            if (Mathf.Abs(scale) < 1e-6f) return;

            double along = Vector3.Dot(point - origin, axisWorld) / scale;

            Station.Apply(Operation, along);
            Station.Rebuild();
        }
    }
}
