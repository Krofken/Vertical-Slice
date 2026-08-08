using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// What the gunsmith is pointing at, and whether a given object is the thing under
    /// the point.
    ///
    /// THIS EXISTS BECAUSE EVERY DRAGGABLE THING IN THE SHOP WAS SHIELDED BY ITS OWN
    /// STATION, and the failure was completely silent. <see cref="StationView"/> is
    /// installed by <c>WorkshopBuilder.LeanIn</c>, which fits a 17 cm BoxCollider marked
    /// <c>isTrigger</c> so the station can be walked up to and used. The work you then
    /// lean in to grab — a 2.5 mm lathe handle, the seating stop — sits INSIDE that box.
    ///
    /// <see cref="Physics.queriesHitTriggers"/> defaults to true, so a plain
    /// <see cref="Physics.Raycast"/> from the leaned-in eye hits the station's own trigger
    /// box first and stops there. Measured in the running shop, at the lathe:
    ///
    ///     [0] d=0.062  Core bench    trigger=True     &lt;- the ray stopped here
    ///     [1] d=0.169  Cavity mouth  trigger=False
    ///     [2] d=0.170  Meplat        trigger=False
    ///
    /// The handle checks "did the ray hit ME", the answer was always no, and nothing on
    /// the bench could ever be dragged. No error, no warning — it just leaned in and sat
    /// there, which is exactly how it was reported.
    ///
    /// So the grab ray IGNORES TRIGGERS. A trigger in this shop means "you may walk up to
    /// this", never "this is solid", and it must never occlude the work it surrounds.
    /// Ignoring them also gets the nearest SOLID hit in one raycast with no allocation and
    /// no sorting, which is why this is a single <see cref="Physics.Raycast"/> rather than
    /// a RaycastAll-and-filter like <see cref="PlayerInteractor"/> needs.
    /// </summary>
    public static class Aim
    {
        /// <summary>
        /// Where the player is aiming.
        ///
        /// With the cursor locked for mouse-look there IS no cursor position, so the aim
        /// is the centre of the screen — you point with your head. Unlocked, which is what
        /// leaning in over a station does, it is the pointer, so the bench works with a
        /// free mouse and from the editor.
        /// </summary>
        public static Ray Ray(Camera camera, Mouse mouse)
        {
            if (camera == null) return new Ray(Vector3.zero, Vector3.forward);

            if (mouse == null || Cursor.lockState == CursorLockMode.Locked)
                return camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            return camera.ScreenPointToRay(mouse.position.ReadValue());
        }

        /// <summary>
        /// True when <paramref name="self"/> is the nearest solid thing under the aim,
        /// within reach.
        ///
        /// "Nearest" rather than "hit at all" is what keeps one drag to one handle. The
        /// lathe's meplat and cavity-mouth beads sit about a millimetre apart on a 9 mm
        /// nose, so a test that accepted any hit would grab both at once and move two
        /// dimensions from one drag — which is the one property the bench must not lose.
        /// </summary>
        public static bool IsUnderAim(Camera camera, GameObject self, float reach, Mouse mouse)
        {
            if (camera == null || self == null) return false;

            // SKIPS WHATEVER THE PLAYER IS CARRYING, and that is not a refinement — without it,
            // raising the magnifying glass killed every control in the shop until the game was
            // restarted. The loupe is held twenty centimetres in front of the eye and a Unity
            // quad primitive comes with a MeshCollider, so the glass became the nearest solid
            // thing under the aim and answered every grab. The lever stopped moving and nothing
            // said why.
            //
            // This is the THIRD time this exact shape has bitten: the character controller
            // capsule hid the bench, the station's lean-in trigger hid its own handles, and now
            // a carried tool hid everything. The lesson each time is the same — a single
            // Raycast finds whatever is closest, and what is closest is often you. Anything
            // parented under the player is not scenery and must never intercept an aim.
            Transform player = camera.transform.root;

            var hits = Physics.RaycastAll(Ray(camera, mouse), reach, ~0,
                QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            GameObject found = null;

            for (int i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;

                if (player != null && collider.transform.IsChildOf(player)) continue;
                if (hits[i].distance >= nearest) continue;

                nearest = hits[i].distance;
                found = collider.gameObject;
            }

            return found == self;
        }

        /// <summary>
        /// Point on the line (origin, direction) closest to the given ray.
        ///
        /// Standard closest-approach of two skew lines. Returns false when the ray and the
        /// axis are near parallel, where the solution is unbounded — which happens exactly
        /// when you are looking straight down the slide. Refusing there is the correct
        /// behaviour for a slide viewed end-on: it simply stops responding rather than
        /// leaping to infinity, which is what projecting onto a plane would do.
        ///
        /// Shared by every tool that slides along one axis — the lathe's nine handles and
        /// the seating die — so there is one implementation of the one piece of geometry
        /// they all depend on.
        /// </summary>
        public static bool ClosestPointOnAxis(
            Vector3 origin, Vector3 direction, Ray ray, out Vector3 point)
        {
            Vector3 r = ray.direction.normalized;

            float dr = Vector3.Dot(direction, r);
            float denominator = 1f - dr * dr;

            if (Mathf.Abs(denominator) < 1e-5f)
            {
                point = origin;
                return false;
            }

            Vector3 between = ray.origin - origin;
            float t = (Vector3.Dot(between, direction) - dr * Vector3.Dot(between, r)) / denominator;

            point = origin + direction * t;
            return true;
        }
    }
}
