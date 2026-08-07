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
        [Tooltip("Where the eye sits while working here, relative to this object.")]
        public Vector3 EyeOffset = new Vector3(0f, 0.14f, -0.16f);

        [Tooltip("The point being looked at, relative to this object. Usually the work " +
                 "itself rather than the middle of the machine.")]
        public Vector3 LookOffset = Vector3.zero;

        [Tooltip("Field of view while leaning in, degrees. Lower is more magnified — " +
                 "this is the loupe, and it is what makes true-scale work readable.")]
        [Range(5f, 60f)]
        public float FieldOfView = 22f;

        /// <summary>World position the eye should occupy.</summary>
        public Vector3 EyePosition => transform.TransformPoint(EyeOffset);

        /// <summary>World point being looked at.</summary>
        public Vector3 LookTarget => transform.TransformPoint(LookOffset);

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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(EyePosition, 0.02f);
            Gizmos.DrawLine(EyePosition, LookTarget);

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(LookTarget, 0.01f);
        }
    }
}
