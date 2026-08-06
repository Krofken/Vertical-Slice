using UnityEngine;

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
    /// Dragging works by finding the point on the handle's axis nearest the mouse ray,
    /// rather than by projecting onto a plane. A plane fails badly when you happen to be
    /// looking down the axis; the nearest-point solution just stops responding, which is
    /// the correct behaviour for a slide you are viewing end-on.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Gunsmith/Lathe Handle")]
    public sealed class LatheHandle : MonoBehaviour
    {
        [Tooltip("Which dimension this handle cuts.")]
        public LatheOperation Operation;

        [Tooltip("The lathe this belongs to.")]
        public LatheStation Station;

        [Tooltip("Camera used for picking. Falls back to the main camera.")]
        public Camera Rig;

        private Camera Picking => Rig != null ? Rig : Camera.main;

        private void OnMouseDrag()
        {
            if (Station == null || Station.Rig == null) return;

            var camera = Picking;
            if (camera == null) return;

            // The axis this handle slides on, in world space.
            Vector3 axisLocal = LatheStation.AxisOf(Operation);
            Vector3 axisWorld = Station.Rig.TransformDirection(axisLocal).normalized;
            Vector3 origin = Station.Rig.position;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);

            if (!ClosestPointOnLine(origin, axisWorld, ray, out Vector3 point)) return;

            // Distance along the axis, in rig-local metres. lossyScale undoes the
            // display scaling the rig applies so the projectile can be seen at all.
            float scale = Station.Rig.lossyScale.z;
            if (Mathf.Abs(scale) < 1e-6f) return;

            double along = Vector3.Dot(point - origin, axisWorld) / scale;

            Station.Apply(Operation, along);
            Station.Rebuild();
        }

        /// <summary>
        /// Point on the line (origin, direction) closest to the given ray.
        ///
        /// Standard closest-approach of two skew lines. Returns false when the ray and
        /// the axis are near parallel, where the solution is unbounded — which happens
        /// exactly when the camera is looking straight down the slide.
        /// </summary>
        private static bool ClosestPointOnLine(Vector3 origin, Vector3 direction, Ray ray, out Vector3 point)
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
