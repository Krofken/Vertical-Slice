using Gunsmith.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The stop you screw up and down. Take hold of it and the bullet seats to wherever
    /// you leave it.
    ///
    /// THIS DID NOT EXIST, and that is the whole of the bug. <see cref="SeatingStop"/>
    /// carried a <c>SetStop</c> method whose own summary said it was "bound to a draggable
    /// handle" — and nothing outside the test assembly had ever called it.
    /// <c>WorkshopBuilder.BuildDie</c> built the stop, the case and the bullet, gave the
    /// stop a collider, and then never made it grabbable. So "set the seating depth" leaned
    /// you in over a die you could not operate, with no error to say why.
    ///
    /// Worth recording that it read as the SAME failure as the lathe handles and was not
    /// one: the lathe's handles existed and were occluded, the die's handle was simply
    /// missing. Two causes, one symptom, and treating them as one bug would have fixed
    /// half of it and left the other half looking fixed.
    ///
    /// WHY IT IS WORTH A TOOL AT ALL: seating depth is the sharpest pressure lever on the
    /// bench. The powder burns in the space left behind the bullet and pressure goes roughly
    /// as the inverse of that volume, so a couple of millimetres deeper in a 9 mm case is
    /// the single change most likely to turn a working load into a flattened primer and then
    /// a split case. <c>Seating_Deeper_Raises_Peak_Pressure</c> guards the physics; this is
    /// what lets a player reach it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Gunsmith/Seating Handle")]
    public sealed class SeatingHandle : MonoBehaviour
    {
        [Tooltip("The die this screws into.")]
        public SeatingStop Die;

        [Tooltip("Frame the stop slides in. The die's rig, whose local +Z runs from the " +
                 "case mouth out towards the bullet's tip.")]
        public Transform Rig;

        [Tooltip("Camera used for aiming. Falls back to the main camera, which is the " +
                 "player's head once the shop is running.")]
        public Camera AimingCamera;

        [Tooltip("How far you can be and still take hold of the stop, metres.")]
        public float Reach = 2.4f;

        private bool _dragging;

        private Camera Aiming => AimingCamera != null ? AimingCamera : Camera.main;

        /// <summary>
        /// Finds its own parts rather than trusting whoever built it.
        ///
        /// The canon's recurring bug shape, and it has bitten four times already. Resolving
        /// lazily means a die placed by hand, restored from a prefab or duplicated across
        /// the bench still works, whatever order anything was wired in.
        /// </summary>
        private bool Resolve()
        {
            if (Die == null) Die = GetComponentInParent<SeatingStop>();
            if (Die == null) return false;

            if (Rig == null) Rig = Die.Stop != null ? Die.Stop.parent : transform.parent;
            return Rig != null;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame) TryGrab(mouse);
            else if (!mouse.leftButton.isPressed) _dragging = false;

            if (_dragging) Drag(mouse);
        }

        private void TryGrab(Mouse mouse)
        {
            if (!Resolve() || Aiming == null) return;

            _dragging = Aim.IsUnderAim(Aiming, gameObject, Reach, mouse);
        }

        /// <summary>
        /// Screws the stop to wherever it is being held.
        ///
        /// The stop is what the bullet's NOSE runs up against, so its station along the
        /// die's axis is the tip position:
        ///
        ///     z_tip = bullet length - seating depth
        ///
        /// which inverts to the depth the die is set to. Dragging the stop towards the case
        /// therefore seats deeper, exactly as screwing a real die body down does.
        /// <see cref="SeatingStop.Depth"/> clamps to the die's own travel, so the tool
        /// cannot be driven past what it could physically produce.
        /// </summary>
        private void Drag(Mouse mouse)
        {
            if (!Resolve() || Aiming == null) return;

            Vector3 axisWorld = Rig.TransformDirection(Vector3.forward).normalized;
            Vector3 origin = Rig.position;

            if (!Aim.ClosestPointOnAxis(origin, axisWorld, Aim.Ray(Aiming, mouse), out Vector3 point))
                return;

            // Rig-local metres. lossyScale undoes any display scaling on the rig so what
            // the solver reads is a true seating depth and not a scaled one.
            float scale = Rig.lossyScale.z;
            if (Mathf.Abs(scale) < 1e-6f) return;

            double tip = Vector3.Dot(point - origin, axisWorld) / scale;

            Die.SetStop(Die.Projectile.OverallLength - tip);
        }
    }
}
