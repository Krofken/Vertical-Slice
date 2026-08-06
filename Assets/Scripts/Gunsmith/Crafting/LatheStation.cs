using Krofken.Ballistics;
using Krofken.Ballistics.UnityIntegration;
using UnityEngine;

namespace Gunsmith.Crafting
{
    /// <summary>Which dimension a handle cuts.</summary>
    public enum LatheOperation
    {
        MeplatDiameter,
        CavityMouth,
        CavityDepth,
        NoseLength,
        OgiveShape,
        BearingSurface,
        BoattailLength,
        BoattailAngle
    }

    /// <summary>
    /// The lathe: where a projectile is turned.
    ///
    /// THE DESIGN RULE THIS EXISTS TO SATISFY — sliders in a panel feel like tax
    /// software, and freehand drawing was considered and rejected as fiddly and at war
    /// with the parametric model. So the numbers live on the TOOL. You take hold of a
    /// dimension and move it, the bullet reshapes under your hand, and the only figure
    /// on screen is the one on the scale.
    ///
    /// That figure is the finished mass, and it is the one number the canon explicitly
    /// sanctions at the bench: it measures what you USED, not what the round will do.
    /// **Never add a predicted-performance readout here.** Muzzle velocity, pressure,
    /// penetration — showing any of them removes the reason to walk out to the range,
    /// which is the entire game.
    ///
    /// The geometry drives everything downstream: the same eleven numbers feed the mass
    /// integrator, the drag model and the mesh, so what is being turned is provably what
    /// will be simulated. Nothing here approximates the bullet for display.
    ///
    /// Runs in edit mode as well as play mode, so the handles can be dragged with
    /// Unity's own gizmo before any game input exists.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Gunsmith/Lathe Station")]
    public sealed class LatheStation : MonoBehaviour
    {
        [Header("Work")]
        [Tooltip("The projectile being turned. Every solver reads these same numbers.")]
        public ProjectileGeometry Geometry = ProjectileGeometry.Default9mmFmj;

        [Tooltip("What it is made of. Decides what the scale reads.")]
        public string CoreMaterialId = MaterialLibrary.Lead;
        public string JacketMaterialId = MaterialLibrary.GildingMetal;

        [Header("Rig")]
        [Tooltip("Parent of the mesh and the handles. Scaled up, because a real 9 mm " +
                 "projectile is 13 mm long and unusable at true size on screen.")]
        public Transform Rig;

        public MeshFilter BulletMesh;
        public MeshRenderer BulletRenderer;

        [Tooltip("World-space readout on the bench scale. Mass only.")]
        public TextMesh ScaleReadout;

        [Tooltip("Shown when the dimensions cannot be made. Not a performance readout — " +
                 "it is the tool refusing the cut.")]
        public TextMesh Complaint;

        [Header("Appearance")]
        public Material ValidMaterial;
        public Material InvalidMaterial;

        [Range(8, 64)] public int RadialSegments = 32;
        [Range(8, 64)] public int NoseSegments = 40;

        /// <summary>Handles, indexed by the operation they cut. Assigned by the setup
        /// tool; any that are null are simply not offered.</summary>
        public Transform[] Handles = new Transform[8];

        private readonly Vector3[] _placed = new Vector3[8];
        private readonly bool[] _hasPlaced = new bool[8];

        private Mesh _mesh;
        private ProjectileMeshBuilder.Buffers _buffers;

        /// <summary>Finished mass, kg. What the scale is showing.</summary>
        public double Mass { get; private set; }

        /// <summary>False when the dimensions describe something that cannot exist.</summary>
        public bool IsValid { get; private set; } = true;

        // ---- Dimension readouts -------------------------------------------
        // Plain numbers in bench units, so UI and tooling can read the work without
        // taking a dependency on the ballistics package. These are MEASUREMENTS of the
        // thing in the chuck — mass, length, diameter. None of them predicts what the
        // round will do, and none of them ever should.

        /// <summary>Finished mass in grains, which is what a loading scale reads.</summary>
        public double MassGrains => Units.KilogramsToGrains(Mass);

        public double OverallLengthMm => Geometry.OverallLength * 1000.0;
        public double CalibreMm => Geometry.Calibre * 1000.0;
        public double MeplatDiameterMm => Geometry.MeplatDiameter * 1000.0;
        public double BaseDiameterMm => Geometry.BaseDiameter * 1000.0;
        public double BoattailAngleDegrees => Geometry.BoattailAngle * RadiansToDegrees;
        public double NoseLengthInCalibres => Geometry.NoseLengthInCalibres;
        public bool IsHollowPoint => Geometry.IsHollowPoint;

        private const float MovedEpsilon = 1e-8f;

        // Angle conversion in DOUBLE. Mathf.Deg2Rad and Mathf.Rad2Deg are float
        // constants, and BoattailAngle is a double that the drag model and the mass
        // integrator read — rounding it through a float here would put roughly 1e-8 of
        // error into a solver input for no reason. Floats belong at the Unity boundary,
        // not inside the geometry.
        private const double DegreesToRadians = System.Math.PI / 180.0;
        private const double RadiansToDegrees = 180.0 / System.Math.PI;

        /// <summary>Past roughly 12 degrees a boattail's flow separates and the drag
        /// saving is lost, so no real design goes far beyond it. This is the end of the
        /// lathe's travel, not a rule about what is allowed to work.</summary>
        private const double MaxBoattailAngle = 20.0 * DegreesToRadians;

        private void OnEnable() => Rebuild();

        private void Update()
        {
            if (ReadHandles()) Rebuild();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) Rebuild();
        }

        /// <summary>
        /// Applies any handle the user has moved since it was last placed.
        /// Returns true if the geometry changed.
        /// </summary>
        private bool ReadHandles()
        {
            if (Handles == null) return false;

            bool changed = false;

            for (int i = 0; i < Handles.Length && i < 8; i++)
            {
                var handle = Handles[i];
                if (handle == null) continue;
                if (!_hasPlaced[i]) continue;

                Vector3 local = handle.localPosition;
                if ((local - _placed[i]).sqrMagnitude <= MovedEpsilon) continue;

                var operation = (LatheOperation)i;

                // How far the handle sits along the axis this cut runs on. The rest of
                // the movement is ignored, which is what makes a handle change exactly
                // ONE dimension — the property that lets a player run a controlled
                // experiment instead of changing five things and learning nothing.
                double along = Vector3.Dot(local, AxisOf(operation));
                Apply(operation, along);

                changed = true;
            }

            return changed;
        }

        /// <summary>Rebuilds the mesh, the scale and the handle positions.</summary>
        public void Rebuild()
        {
            if (Geometry.Calibre <= 0.0) return;

            IsValid = Geometry.Validate(out string error);

            if (Complaint != null)
                Complaint.text = IsValid ? string.Empty : error;

            if (BulletRenderer != null)
            {
                var material = IsValid ? ValidMaterial : InvalidMaterial;
                if (material != null) BulletRenderer.sharedMaterial = material;
            }

            if (IsValid) RebuildMesh();

            UpdateScale();
            PlaceHandles();
        }

        private void RebuildMesh()
        {
            if (BulletMesh == null) return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Turned Projectile" };
                _mesh.MarkDynamic();
            }

            _buffers ??= new ProjectileMeshBuilder.Buffers();

            // Pivot at the tip, so the tip sits at local z = 0 and the body runs along
            // -Z. Handles are positioned in the same frame.
            ProjectileMeshBuilder.Build(
                Geometry, _mesh, RadialSegments, NoseSegments, pivotFromTip: 0.0, buffers: _buffers);

            BulletMesh.sharedMesh = _mesh;
        }

        private void UpdateScale()
        {
            if (!IsValid) { Mass = 0.0; if (ScaleReadout != null) ScaleReadout.text = "--"; return; }

            var properties = MassPropertiesSolver.Compute(Geometry, new ProjectileMaterials
            {
                CoreMaterialId = CoreMaterialId,
                JacketMaterialId = JacketMaterialId
            });

            Mass = properties.Mass;

            // Grains, because that is the unit a scale on a loading bench reads in.
            if (ScaleReadout != null)
                ScaleReadout.text = $"{Units.KilogramsToGrains(Mass):F1} gr";
        }

        // ------------------------------------------------------------------
        // Where each handle sits, and which way it moves
        // ------------------------------------------------------------------

        /// <summary>Unit direction, rig-local, that this cut is measured along.</summary>
        public static Vector3 AxisOf(LatheOperation operation)
        {
            switch (operation)
            {
                // Lengths run down the body, which lies along -Z from the tip.
                case LatheOperation.CavityDepth:
                case LatheOperation.NoseLength:
                case LatheOperation.BearingSurface:
                case LatheOperation.BoattailLength:
                    return Vector3.back;

                // Diameters are radial. Spread around the axis so the handles do not
                // pile up on each other at the tip.
                case LatheOperation.MeplatDiameter: return Vector3.right;
                case LatheOperation.CavityMouth: return Vector3.left;
                case LatheOperation.OgiveShape: return Vector3.up;
                case LatheOperation.BoattailAngle: return Vector3.right;
            }

            return Vector3.right;
        }

        /// <summary>Where the handle belongs for the current geometry, rig-local metres.</summary>
        public Vector3 PositionOf(LatheOperation operation)
        {
            double radius = Geometry.Radius;
            double nose = Geometry.NoseLength;
            double shank = nose + Geometry.BearingSurfaceLength;
            double total = Geometry.OverallLength;

            // Lengthwise handles stand off the surface so they can be grabbed.
            float standoff = (float)(radius * 1.9);

            switch (operation)
            {
                case LatheOperation.MeplatDiameter:
                    return new Vector3((float)Geometry.MeplatRadius, 0f, 0f);

                case LatheOperation.CavityMouth:
                    return new Vector3(-(float)(Geometry.CavityMouthDiameter * 0.5), 0f, 0f);

                case LatheOperation.CavityDepth:
                    return new Vector3(0f, standoff * 0.6f, -(float)Geometry.CavityDepth);

                case LatheOperation.NoseLength:
                    return new Vector3(standoff, 0f, -(float)nose);

                case LatheOperation.OgiveShape:
                    return new Vector3(0f, (float)Geometry.RadiusAt(nose * 0.5), -(float)(nose * 0.5));

                case LatheOperation.BearingSurface:
                    return new Vector3(-standoff, 0f, -(float)shank);

                case LatheOperation.BoattailLength:
                    return new Vector3(0f, -standoff, -(float)total);

                case LatheOperation.BoattailAngle:
                    return new Vector3((float)(Geometry.BaseDiameter * 0.5), 0f, -(float)total);
            }

            return Vector3.zero;
        }

        private void PlaceHandles()
        {
            if (Handles == null) return;

            for (int i = 0; i < Handles.Length && i < 8; i++)
            {
                var handle = Handles[i];
                if (handle == null) continue;

                Vector3 position = PositionOf((LatheOperation)i);
                handle.localPosition = position;

                _placed[i] = position;
                _hasPlaced[i] = true;
            }
        }

        // ------------------------------------------------------------------
        // The cuts
        // ------------------------------------------------------------------

        /// <summary>
        /// Sets one dimension from a handle's position along its axis, in metres.
        ///
        /// Bounds here are the LATHE's limits, not the game's: you cannot cut a nose
        /// shorter than nothing, or a meplat wider than the bullet. Nothing is clamped
        /// because it would perform badly — the canon is explicit that physics and
        /// scarcity do the limiting, not arbitrary caps.
        /// </summary>
        public void Apply(LatheOperation operation, double along)
        {
            double calibre = Geometry.Calibre;
            double radius = Geometry.Radius;

            switch (operation)
            {
                case LatheOperation.MeplatDiameter:
                    Geometry.MeplatDiameter = Clamp(along * 2.0, calibre * 0.02, calibre * 0.98);
                    // The cavity cannot be wider than the flat it opens onto.
                    if (Geometry.CavityMouthDiameter > Geometry.MeplatDiameter)
                        Geometry.CavityMouthDiameter = Geometry.MeplatDiameter;
                    break;

                case LatheOperation.CavityMouth:
                    Geometry.CavityMouthDiameter = Clamp(along * 2.0, 0.0, Geometry.MeplatDiameter);
                    break;

                case LatheOperation.CavityDepth:
                    Geometry.CavityDepth = Clamp(along, 0.0, Geometry.NoseLength * 0.95);
                    break;

                case LatheOperation.NoseLength:
                    Geometry.NoseLength = Clamp(along, calibre * 0.15, calibre * 5.0);
                    if (Geometry.CavityDepth > Geometry.NoseLength * 0.95)
                        Geometry.CavityDepth = Geometry.NoseLength * 0.95;
                    break;

                case LatheOperation.OgiveShape:
                {
                    // The handle rides on the nose surface at mid-length. Pulling it out
                    // fills the nose towards a tangent ogive; pushing it in hollows it
                    // towards a cone. Mapped through the radius actually achievable at
                    // that station, so the handle stays under the cursor.
                    double station = Geometry.NoseLength * 0.5;
                    double cone = RadiusAtShape(station, 0.30);
                    double tangent = RadiusAtShape(station, 1.00);

                    double t = tangent > cone + 1e-9 ? (along - cone) / (tangent - cone) : 1.0;
                    Geometry.OgiveShapeParameter = Clamp(0.30 + t * 0.70, 0.30, 1.00);
                    break;
                }

                case LatheOperation.BearingSurface:
                    // Measured from the tip, so subtract the nose already cut.
                    Geometry.BearingSurfaceLength =
                        Clamp(along - Geometry.NoseLength, calibre * 0.02, calibre * 5.0);
                    break;

                case LatheOperation.BoattailLength:
                    Geometry.BoattailLength = Clamp(
                        along - Geometry.NoseLength - Geometry.BearingSurfaceLength,
                        0.0, calibre * 2.0);
                    break;

                case LatheOperation.BoattailAngle:
                {
                    // The handle is the base radius. The taper angle follows from how
                    // much narrower the base is than the shank, over the tail's length:
                    //     tan(angle) = (r_shank - r_base) / L_boattail
                    if (Geometry.BoattailLength <= 1e-9) break;

                    double baseRadius = Clamp(along, 0.0, radius);
                    double angle = System.Math.Atan2(radius - baseRadius, Geometry.BoattailLength);

                    Geometry.BoattailAngle = Clamp(angle, 0.0, MaxBoattailAngle);
                    break;
                }
            }
        }

        /// <summary>Body radius at a station for a hypothetical shape parameter, m.</summary>
        private double RadiusAtShape(double station, double shape)
        {
            var probe = Geometry;
            probe.OgiveShapeParameter = shape;
            return probe.RadiusAt(station);
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        }
    }
}
