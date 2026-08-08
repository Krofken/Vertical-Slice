using Gunsmith.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Crafting
{
    /// <summary>Which of the mill's three jobs a control does.</summary>
    public enum MillAdjustment
    {
        /// <summary>The grinding wheel. Sets the web — how big the granules are, and so
        /// how fast the charge burns.</summary>
        Grind = 0,

        /// <summary>The coating drum. More passes, more surface deterrent.</summary>
        Drum = 1,

        /// <summary>The extrusion die. Discrete: there is no halfway between a sphere and
        /// a flake, so this is a swap rather than a slide.</summary>
        Die = 2
    }

    /// <summary>
    /// A control on the propellant mill you can actually work.
    ///
    /// THE MILL HAD NO HAND ATTACHED TO IT. `SetWeb`, `SetDeterrent` and `NextShape` were
    /// called from the builders — once, at construction — and from the test assembly, and
    /// from nowhere else. So the station rendered a recipe, and the player could look at it
    /// and do nothing whatsoever. Asked what he was supposed to do there, the answer was
    /// genuinely nothing, which is why it read as purposeless rather than merely unlabelled.
    /// Fourth station found in that state, after the balance, the seating die and the press.
    ///
    /// WHY IT DESERVES CONTROLS AT ALL: the web is the sharpest pressure lever on the bench.
    /// On one 5.5 grain charge, changing only the web —
    ///
    ///     15 µm -> 366 m/s and 499 MPa, which splits the case
    ///     30 µm -> 337 m/s and 262 MPa
    ///     60 µm -> 258 m/s and 128 MPa, and only 71% of the charge burnt
    ///
    /// — so finer is emphatically not better. Grinding finer bursts the case; leaving it
    /// coarse throws unburnt powder out of the muzzle as flash. Neither of those is stated
    /// anywhere at the station and neither should be: the player learns it from the fired case
    /// and the muzzle flash, which is the whole design. What the station owes them is the
    /// ability to CHANGE it, and a dimension they can read while they do.
    ///
    /// Grind and drum are drags, on the shared <see cref="Aim"/> path so the station's own
    /// lean-in trigger cannot shield them. The die is a left-click rather than the E key,
    /// deliberately: E is how you lean in and out of a station, and a fixture inside a
    /// station's hierarchy that also answers to E would swap the die and stand the player up
    /// at the same time.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Gunsmith/Mill Control")]
    public sealed class MillControl : MonoBehaviour
    {
        [Tooltip("Which job this control does.")]
        public MillAdjustment Adjustment = MillAdjustment.Grind;

        [Tooltip("The mill this belongs to.")]
        public PropellantMill Mill;

        [Tooltip("Frame the control slides in. Defaults to the parent.")]
        public Transform Rig;

        [Tooltip("How far this control travels end to end, metres.")]
        public float Travel = 0.06f;

        [Tooltip("Where the control sits at the fine/zero end of its travel, rig-local. " +
                 "Set by the builder; the control places itself along the track from here.")]
        public Vector3 TrackStart = Vector3.zero;

        [Tooltip("How far you can be and still work it, metres.")]
        public float Reach = 2.4f;

        [Tooltip("Camera used for aiming. Falls back to the main camera, which is the " +
                 "player's head once the shop is running.")]
        public Camera AimingCamera;

        private bool _dragging;

        private Camera Aiming => AimingCamera != null ? AimingCamera : Camera.main;

        /// <summary>The axis this control slides on, rig-local. The die does not slide.</summary>
        public static Vector3 AxisOf(MillAdjustment adjustment)
            => adjustment == MillAdjustment.Drum ? Vector3.forward : Vector3.right;

        private bool Resolve()
        {
            if (Mill == null) Mill = GetComponentInParent<PropellantMill>();
            if (Rig == null) Rig = transform.parent;
            return Mill != null && Rig != null;
        }

        private void OnEnable() => Resolve();

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !Resolve()) return;

            if (Adjustment == MillAdjustment.Die)
            {
                // A swap, not a slide.
                if (mouse.leftButton.wasPressedThisFrame
                    && Aim.IsUnderAim(Aiming, gameObject, Reach, mouse))
                    Mill.NextShape();

                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
                _dragging = Aim.IsUnderAim(Aiming, gameObject, Reach, mouse);
            else if (!mouse.leftButton.isPressed)
                _dragging = false;

            // PLACE EVERY FRAME, dragging or not. Placing only when idle is what made the
            // controls appear to move after the mouse stopped rather than under it: the value
            // was changing live but the sphere stayed put until the drag ended. Drag writes the
            // value, Place reads it back, both every frame — so the control tracks the hand.
            if (_dragging) Drag(mouse);
            Place();
        }

        /// <summary>
        /// Puts the control where its current value says it belongs.
        ///
        /// THE CONTROL'S POSITION HAS TO MEAN ITS VALUE, and this is what was missing. The
        /// builder parked the wheel at x = -0.055 while the drag mapped 0..Travel from the rig
        /// ORIGIN, so the handle's resting place worked out as a negative fraction, clamped to
        /// zero, and slammed the web to its minimum the instant it was touched. The web read
        /// 0.020 mm — the floor of the range — and would not come back up, which looks exactly
        /// like a control that does not work.
        ///
        /// Same arrangement as the lathe: <c>PlaceHandles</c> positions each bead from the
        /// geometry and the handle writes the geometry back when dragged. A control that is
        /// placed from its value cannot disagree with it.
        /// </summary>
        private void Place()
        {
            if (Adjustment == MillAdjustment.Die) return;

            transform.localPosition = TrackStart + AxisOf(Adjustment) * (Travel * (float)Fraction);
        }

        /// <summary>Where along its travel the control's current value sits, 0..1.</summary>
        private double Fraction
        {
            get
            {
                if (Mill == null) return 0.0;

                if (Adjustment == MillAdjustment.Drum) return Mill.DeterrentCoating;

                // Inverse of the log map used when dragging.
                double min = System.Math.Log(Mill.MinimumWeb);
                double max = System.Math.Log(Mill.MaximumWeb);
                if (max - min < 1e-12) return 0.0;

                return (System.Math.Log(Mill.WebThickness) - min) / (max - min);
            }
        }

        /// <summary>
        /// Slides the control and sets what it controls.
        ///
        /// The web is mapped LOGARITHMICALLY across the travel. Its range is 20 µm to 500 µm —
        /// a factor of twenty-five — and a linear slide would spend most of its length in
        /// coarse powder nobody wants while cramming the entire useful pistol range into the
        /// first few millimetres. On a log scale every millimetre of travel is the same
        /// PROPORTIONAL change in burn time, which is what the physics actually responds to.
        /// </summary>
        private void Drag(Mouse mouse)
        {
            Vector3 axisWorld = Rig.TransformDirection(AxisOf(Adjustment)).normalized;

            // Measured from the START OF THE TRACK, not from the rig's origin. Measuring from
            // the origin is what made the resting position map to a negative fraction.
            Vector3 origin = Rig.TransformPoint(TrackStart);

            if (!Aim.ClosestPointOnAxis(origin, axisWorld, Aim.Ray(Aiming, mouse), out Vector3 point))
                return;

            float scale = Rig.lossyScale.x;
            if (Mathf.Abs(scale) < 1e-6f) return;

            double along = Vector3.Dot(point - origin, axisWorld) / scale;
            double fraction = Travel > 1e-6f ? along / Travel : 0.0;

            if (fraction < 0.0) fraction = 0.0;
            else if (fraction > 1.0) fraction = 1.0;

            if (Adjustment == MillAdjustment.Drum)
            {
                Mill.SetDeterrent(fraction);
                return;
            }

            // Log interpolation between the mill's own limits.
            double min = System.Math.Log(Mill.MinimumWeb);
            double max = System.Math.Log(Mill.MaximumWeb);

            Mill.SetWeb(System.Math.Exp(min + (max - min) * fraction));
        }
    }
}
