using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The seating die: a threaded stop the bullet is pressed against.
    ///
    /// You screw the stop up or down and run the case into the die. However far the
    /// stop is set, that is how deep the bullet seats — the depth is a property of the
    /// TOOL, not a field you fill in. Back the stop out and the round grows longer;
    /// screw it in and the bullet is driven deeper into the case.
    ///
    /// WHY THIS MATTERS RATHER THAN BEING SET DRESSING: seating depth is one of the
    /// sharpest pressure levers a handloader has. The powder burns in the space left
    /// between the case head and the base of the bullet, and pressure goes roughly as
    /// the inverse of that free volume. Seating a bullet a couple of millimetres deeper
    /// in a 9 mm case removes a large fraction of it, and the peak pressure climbs hard
    /// — it is the single change most likely to turn a working load into a flattened
    /// primer and then a ruptured case.
    ///
    /// The tool therefore shows exactly two numbers, both of them measurements of the
    /// thing in your hand: how deep the bullet sits, and how long the finished round is.
    /// Cartridge overall length is what a real loader measures with calipers, and it is
    /// what a chamber cares about. Neither predicts performance.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Gunsmith/Seating Stop")]
    public sealed class SeatingStop : MonoBehaviour
    {
        [Header("Travel")]
        [Tooltip("Shallowest seat the die can produce, metres. Too shallow and the " +
                 "bullet is barely held by the case neck.")]
        public double MinimumDepth = 0.0010;

        [Tooltip("Deepest seat the die can produce, metres.")]
        public double MaximumDepth = 0.0090;

        [Header("Work")]
        [Tooltip("Length of the case the bullet is seated into, metres.")]
        public double CaseLength = 0.0192;

        [Tooltip("The projectile being seated. Its length sets how far it protrudes.")]
        public ProjectileGeometry Projectile = ProjectileGeometry.Default9mmFmj;

        [Header("Parts")]
        [Tooltip("The stop itself. Driven along its local -Z as the depth increases.")]
        public Transform Stop;

        [Tooltip("The seated bullet, positioned to match the depth.")]
        public Transform SeatedBullet;

        public TextMesh DepthReadout;

        [SerializeField] private double _depth = 0.0030;

        /// <summary>How deep the bullet sits in the case, metres.</summary>
        public double Depth
        {
            get => _depth;
            set { _depth = Clamp(value, MinimumDepth, MaximumDepth); Refresh(); }
        }

        /// <summary>
        /// Cartridge overall length, metres — case length plus the part of the bullet
        /// standing proud of the case mouth. What calipers read.
        /// </summary>
        public double OverallLength => CaseLength + Projectile.OverallLength - _depth;

        public double DepthMm => _depth * 1000.0;
        public double OverallLengthMm => OverallLength * 1000.0;

        /// <summary>True when the bullet is seated so deep it is fully swallowed by the
        /// case. The die allows it; it just cannot be a sane round.</summary>
        public bool IsBuried => _depth >= Projectile.OverallLength;

        private void OnEnable()
        {
            EnsureHandle();
            Refresh();
        }

        private void OnValidate() => Refresh();

        /// <summary>
        /// Makes sure the stop can actually be taken hold of.
        ///
        /// WHY THE DIE FITS ITS OWN HANDLE INSTEAD OF THE BUILDER DOING IT: the shop the
        /// player walks around is a PREFAB INSTANCE, and <c>WorkshopBootstrap</c> adopts it
        /// rather than rebuilding. So a fixture added to <c>WorkshopBuilder</c> reaches a
        /// freshly-built shop and never reaches the authored one — the saved prefab is
        /// frozen at whatever the builder looked like the day it was written, and the only
        /// way to refresh it is to re-author the room, which throws away the hand-placed
        /// layout the prefab exists to preserve.
        ///
        /// That is a trap worth naming: a PlayMode test that builds its own shop will pass
        /// while the actual game stays broken, which is the same false green the canon
        /// records for staged cameras. A component that fits its own missing parts works in
        /// all three shops — code-built, prefab-restored, and hand-placed.
        ///
        /// RUNTIME ONLY. This is <c>[ExecuteAlways]</c>, and adding a component in edit mode
        /// would dirty the scene — which turns every domain reload into a "save your
        /// changes?" dialog and can reach a commit by accident. Nothing is serialised in
        /// Play, so doing it here is free.
        /// </summary>
        private void EnsureHandle()
        {
            if (!Application.isPlaying) return;
            if (Stop == null) return;
            if (Stop.GetComponent<SeatingHandle>() != null) return;

            // The stop needs something to be aimed at. The builder gives it a BoxCollider;
            // a die assembled by hand might not have.
            if (Stop.GetComponent<Collider>() == null) Stop.gameObject.AddComponent<BoxCollider>();

            var handle = Stop.gameObject.AddComponent<SeatingHandle>();
            handle.Die = this;
            handle.Rig = Stop.parent;
        }

        /// <summary>Screws the stop to a depth, in metres. Bound to a draggable
        /// handle so the depth is set by moving the tool.</summary>
        public void SetStop(double depth) => Depth = depth;

        /// <summary>Applies the seat to a design.</summary>
        public void ApplyTo(ref CartridgeDesign design) => design.SeatingDepth = _depth;

        /// <summary>Reads the seat back off an existing design, so opening a saved load
        /// puts the tool where that load left it.</summary>
        public void ReadFrom(in CartridgeDesign design)
        {
            Projectile = design.Projectile;
            Depth = design.SeatingDepth;
        }

        private void Refresh()
        {
            // Geometry of the die, with the case mouth at z = 0 and the case body
            // running down -Z:
            //
            //   the bullet's BASE is seated one depth INTO the case, at z = -depth
            //   the bullet runs from there back out along +Z
            //   so its tip — and therefore the stop it is pressed against — lands at
            //   z = length - depth
            //
            // The projectile mesh is lathed tip-at-origin running down -Z, so offsetting
            // it by (length - depth) puts its base exactly at the seating depth.
            float tip = (float)(Projectile.OverallLength - _depth);

            if (SeatedBullet != null)
                SeatedBullet.localPosition = new Vector3(0f, 0f, tip);

            // The die's stop is what the bullet nose runs up against. It belongs at the
            // tip, not at the case mouth — screwing it down is what seats deeper.
            if (Stop != null)
                Stop.localPosition = new Vector3(0f, 0f, tip);

            if (DepthReadout != null)
                DepthReadout.text = $"{DepthMm:F2} mm deep\n{OverallLengthMm:F2} mm overall";
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}
