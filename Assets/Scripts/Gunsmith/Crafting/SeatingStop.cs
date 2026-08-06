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

        private void OnEnable() => Refresh();
        private void OnValidate() => Refresh();

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
