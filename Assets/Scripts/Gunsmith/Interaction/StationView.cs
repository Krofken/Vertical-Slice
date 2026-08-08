using UnityEngine;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// Where the gunsmith puts his eye when he leans in over a station.
    ///
    /// THIS IS WHAT LETS THE BENCH BE TRUE SIZE. The shop used to inflate its
    /// cartridges forty times and its powder grains nine hundred, because a real 9 mm
    /// round on a waist-high bench seen from 1.7 m away is four pixels across. The
    /// result was a 23 cm tank shell lying next to baseball-sized grains of powder, on
    /// a bench, in a gunsmith's shop.
    ///
    /// Inflating the object is the wrong fix and it cannot be made right — the moment
    /// you scale a cartridge to be visible from standing height it stops being a
    /// cartridge. A gunsmith does not enlarge the round. He picks it up and brings it
    /// to his eye. So the object stays the size it really is and the CAMERA moves,
    /// which is both correct and free: nothing a solver reads is touched, because
    /// nothing about the object changes at all.
    ///
    /// A narrow field of view does the rest. Dropping from the usual 60 degrees to
    /// about 22 magnifies roughly three times on top of the closer position, which is
    /// what makes a 13 mm bullet fill a third of the screen at true scale.
    /// </summary>
    [AddComponentMenu("Gunsmith/Station View")]
    public sealed class StationView : MonoBehaviour
    {
        [Tooltip("Where the eye sits while working here, relative to this object. Ignored " +
                 "unless FrameTheWork is off.")]
        public Vector3 EyeOffset = new Vector3(0f, 0.14f, -0.16f);

        [Tooltip("The point being looked at, relative to this object. Usually the work " +
                 "itself rather than the middle of the machine. Ignored unless " +
                 "FrameTheWork is off.")]
        public Vector3 LookOffset = Vector3.zero;

        [Header("Framing")]
        [Tooltip("Work out the pose from what is actually ON the station instead of using " +
                 "the offsets above. Leave this on.")]
        public bool FrameTheWork = true;

        [Tooltip("THE WORK, if only part of the station is it. Optional. Measuring the whole " +
                 "machine frames the machine — the powder balance has a 26 cm beam and a 44 mm " +
                 "pan, so fitting the beam put the eye 86 cm away and a granule of powder came " +
                 "out ONE PIXEL across. You lean in to look at the pan, not at the beam.")]
        public Transform Work;

        [Tooltip("How much of the frame's height the work should fill, 0..1.")]
        [Range(0.2f, 0.95f)]
        public float Fill = 0.6f;

        [Tooltip("How far above the work the eye sits, as a fraction of the distance to " +
                 "it. You lean over a bench, you do not look at it side-on.")]
        [Range(0f, 1.5f)]
        public float Loft = 0.55f;

        [Tooltip("Field of view while leaning in, degrees. Lower is more magnified — " +
                 "this is the loupe, and it is what makes true-scale work readable.")]
        [Range(5f, 60f)]
        public float FieldOfView = 22f;

        /// <summary>World position the eye should occupy.</summary>
        public Vector3 EyePosition
        {
            get
            {
                if (!FrameTheWork) return transform.TransformPoint(EyeOffset);

                if (!TryMeasureWork(out Bounds work)) return transform.TransformPoint(EyeOffset);

                // How far back the eye must sit for the work to fill `Fill` of the frame.
                //
                //     visible height at distance d = 2 * d * tan(fov / 2)
                //     want 2 * extent = Fill * visible height
                //  => d = extent / (Fill * tan(fov / 2))
                float extent = Mathf.Max(work.extents.x, work.extents.y, work.extents.z);
                float halfAngle = Mathf.Max(1f, FieldOfView * 0.5f) * Mathf.Deg2Rad;
                float distance = extent / Mathf.Max(0.05f, Fill) / Mathf.Tan(halfAngle);

                // Never closer than the near plane can survive, never further than arm's
                // reach — beyond that you are not leaning in, you are standing back.
                distance = Mathf.Clamp(distance, 0.06f, 0.75f);

                // On the player's side of the bench and lofted above it. -Z is the room:
                // the back wall is at +Z, so the gunsmith always stands on -Z.
                Vector3 back = -transform.forward;
                Vector3 up = Vector3.up;

                return work.center + back * distance + up * (distance * Loft);
            }
        }

        /// <summary>World point being looked at.</summary>
        public Vector3 LookTarget
        {
            get
            {
                if (!FrameTheWork) return transform.TransformPoint(LookOffset);
                return TryMeasureWork(out Bounds work) ? work.center : transform.TransformPoint(LookOffset);
            }
        }

        /// <summary>
        /// Where the work actually is, in world space.
        ///
        /// THE POSES WERE WRONG BECAUSE THEY WERE ARITHMETIC. Every EyeOffset in the shop was
        /// a hand-picked triple that nobody had looked through, so the powder balance framed
        /// the beam edge-on with the pan off the side of the screen, and the others were no
        /// better. A constant cannot be right anyway: the moment a station gains a part or a
        /// tool is repositioned, the number that framed it yesterday frames air today.
        ///
        /// So the pose is measured from the station's own contents instead. Same reasoning as
        /// `TextFit` measuring rendered text rather than trusting a character size — fit the
        /// frame to the thing, do not guess a number and hope.
        ///
        /// TextMeshes are excluded deliberately. A station's readouts hang off to the side and
        /// are often larger than the work itself, so including them would pull the shot away
        /// from the very thing you leaned in to see and zoom out to fit a label.
        /// </summary>
        private Bounds _measured;
        private bool _measuredKnown;

        /// <summary>
        /// Re-measures the station on the next request. Call after deliberately changing what
        /// is on it, if the framing genuinely should move.
        /// </summary>
        public void Remeasure() => _measuredKnown = false;

        public bool TryMeasureWork(out Bounds work)
        {
            // MEASURED ONCE, THEN HELD. Measuring live meant the camera crept every time the
            // station's contents changed — grinding the powder coarser produces bigger
            // granules, the bounds grew, the eye backed off to fit them, and the whole view
            // drifted while the player was mid-drag. The framing is a decision about where to
            // stand, and it should not wobble because the work changed shape under your hands.
            if (_measuredKnown)
            {
                work = _measured;
                return true;
            }

            work = default;
            bool found = false;

            // Only the nominated work, when there is one.
            var scope = Work != null ? Work : transform;

            foreach (var renderer in scope.GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                // Readouts are not the work.
                if (renderer.GetComponent<TextMesh>() != null) continue;

                if (!found) { work = renderer.bounds; found = true; }
                else work.Encapsulate(renderer.bounds);
            }

            if (found && Application.isPlaying)
            {
                _measured = work;
                _measuredKnown = true;
            }

            return found;
        }

        /// <summary>Rotation that looks from the eye at the work.</summary>
        public Quaternion EyeRotation
        {
            get
            {
                Vector3 forward = LookTarget - EyePosition;
                return forward.sqrMagnitude > 1e-8f
                    ? Quaternion.LookRotation(forward, Vector3.up)
                    : transform.rotation;
            }
        }

        /// <summary>
        /// Lifts the station's readouts clear of the interaction prompt.
        ///
        /// THE BUG THIS FIXES, seen on screen at the mill: the recipe's last line ("coated")
        /// overprinted "[E] mill the powder". The prompt is carried on the player's head and
        /// always sits low and centred, while several stations hang their readout BELOW the
        /// work — so the two land in the same part of the frame and neither can be read.
        ///
        /// Moving the labels above the work rather than moving the prompt is the right way
        /// round: the prompt's position is a deliberate HUD decision that applies everywhere in
        /// the shop, and only these four labels are in the wrong place. Framing already ignores
        /// TextMeshes when it measures the work, so lifting a label cannot pull the shot off
        /// the thing you leaned in to see.
        ///
        /// RUNTIME ONLY. Moving a transform in edit mode dirties the scene, and this has to
        /// work on the frozen prefab, where the old positions are already serialised.
        /// </summary>
        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (!TryMeasureWork(out Bounds work)) return;

            foreach (var label in GetComponentsInChildren<TextMesh>(includeInactive: true))
            {
                Vector3 world = label.transform.position;

                // BEHIND the work, not merely above it. Lifting a label in Y alone was my
                // first attempt and it made things worse: the lean-in eye is lofted and looking
                // down, so "above the work" projects onto the middle of the frame and the text
                // landed straight across the pan it was describing.
                //
                // Pushing it away from the player as well as up puts it past the far edge of
                // the work, where it reads as a card standing behind the tool. transform.forward
                // is +Z, which is the back wall — the gunsmith always stands on -Z.
                world.y = work.max.y + LabelClearance;
                world += transform.forward * (work.extents.z + LabelClearance * 2f);

                label.transform.position = world;
            }
        }

        [Tooltip("How far above and behind the work a readout is placed, metres. Generous " +
                 "on purpose: a readout overlapping the work is worse than one further away, " +
                 "because the work is the thing you leaned in to look at.")]
        public float LabelClearance = 0.035f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(EyePosition, 0.02f);
            Gizmos.DrawLine(EyePosition, LookTarget);

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(LookTarget, 0.01f);

            // The box the framing is solving for, so a bad shot can be seen to be a bad
            // measurement rather than guessed at.
            if (FrameTheWork && TryMeasureWork(out Bounds work))
            {
                Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.7f);
                Gizmos.DrawWireCube(work.center, work.size);
            }
        }
    }
}
