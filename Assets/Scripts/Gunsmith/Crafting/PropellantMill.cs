using System;
using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The propellant mill: where powder is made rather than bought by name.
    ///
    /// Everything this station sets was previously hardcoded, which meant the single
    /// most important property of a load — how FAST the powder burns — was invisible and
    /// unreachable. Charge weight alone does not tell you much; 5.5 grains of a fast
    /// ball powder and 5.5 grains of a slow extruded stick behave nothing alike. This is
    /// the station that makes the powder balance mean something.
    ///
    /// FOUR THINGS ARE MADE HERE, and each is something you can SEE in the pan, which is
    /// why this can be a tool rather than a form:
    ///
    ///   BASE CHEMISTRY   which powder you start from. A named tin, not a number.
    ///   GRAIN SHAPE      spheres, flakes, cords, tubes. Visibly different in the pan,
    ///                    and it decides whether the burning surface shrinks, holds or
    ///                    grows as the grain is consumed.
    ///   WEB THICKNESS    how big the grains are. THE dominant control on burn speed:
    ///                    the web is the distance the flame front has to travel to eat
    ///                    the grain, so doubling it roughly doubles the burn time.
    ///                    Fast pistol powders sit near 25 micrometres, slow magnum rifle
    ///                    powders reach half a millimetre.
    ///   DETERRENT COAT   a surface inhibitor that slows the early burn, so the outside
    ///                    of the grain lags the core and the charge burns progressively.
    ///
    /// WHAT THIS STATION MUST NEVER SHOW: burn rate, impetus, expected pressure, or
    /// anything else that predicts what the powder will DO. Those live in the base
    /// chemistry and stay unnamed numbers inside the solver. The player learns that big
    /// sticks are slow by loading them and reading the fired case, not by reading a
    /// figure here. Grain size in millimetres is fine — it is a dimension of the thing in
    /// the pan, exactly like the lathe's dimensions.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Gunsmith/Propellant Mill")]
    public sealed class PropellantMill : MonoBehaviour
    {
        [Header("Recipe")]
        [Tooltip("Base chemistry, from PropellantLibrary. A tin you pick up, not a dial.")]
        public string BaseId = PropellantLibrary.SingleBase;

        [Tooltip("Grain form pressed by the extrusion die.")]
        public GrainShape Shape = GrainShape.Sphere;

        // DOUBLE, and deliberately: this is a solver input, and a serialised float would
        // put rounding noise straight into the interior ballistics ODE. Same trap that
        // caught the boattail angle and the powder beam.
        [SerializeField] private double _webThickness = 3.5e-5;
        [SerializeField] private double _deterrentCoating = 0.3;

        [Header("Travel")]
        [Tooltip("Finest web the mill can press, metres. Roughly a fast pistol powder.")]
        public double MinimumWeb = 2.0e-5;

        [Tooltip("Coarsest web the mill can press, metres. Roughly a slow rifle powder.")]
        public double MaximumWeb = 5.0e-4;

        [Header("Parts")]
        [Tooltip("Sample of the finished grains. Rebuilt whenever the recipe changes.")]
        public Transform GrainTray;

        [Tooltip("Grains to show in the tray. Presentation only.")]
        [Range(4, 64)] public int SampleGrains = 24;

        public Material GrainMaterial;

        [Tooltip("Reads the recipe as dimensions, never as performance.")]
        public TextMesh Readout;

        /// <summary>Web thickness, metres. The dominant control on burn speed.</summary>
        public double WebThickness
        {
            get => _webThickness;
            set { _webThickness = Clamp(value, MinimumWeb, MaximumWeb); Refresh(); }
        }

        /// <summary>Surface deterrent, 0 (untreated) to 1 (strongly deterred).</summary>
        public double DeterrentCoating
        {
            get => _deterrentCoating;
            set { _deterrentCoating = Clamp(value, 0.0, 1.0); Refresh(); }
        }

        /// <summary>Web in millimetres — a dimension of the grain, like a calliper
        /// reading. Not a performance figure.</summary>
        public double WebMillimetres => _webThickness * 1000.0;

        /// <summary>Name of the die currently fitted, for UI that should not need to
        /// know the ballistics package's enum.</summary>
        public string ShapeName => Shape.ToString();

        /// <summary>
        /// How the burning surface behaves as the grain is consumed, in the player's
        /// words rather than as chi/lambda/mu. This is an observation about the SHAPE
        /// sitting in the pan, not a prediction about the load.
        /// </summary>
        public string BurnCharacter
        {
            get
            {
                switch (Shape)
                {
                    case GrainShape.Sphere:
                    case GrainShape.Cord:
                        return "surface shrinks as it burns";
                    case GrainShape.Flake:
                    case GrainShape.SinglePerforated:
                        return "surface holds as it burns";
                    case GrainShape.SevenPerforated:
                        return "surface grows as it burns";
                    default:
                        return "surface behaviour set by hand";
                }
            }
        }

        /// <summary>
        /// How densely this grain form packs, 0..1. Not a chemical property — spheres
        /// tumble into a dense bed, flakes bridge and trap air. It has no effect on the
        /// burn at all, but it decides whether a charge PHYSICALLY FITS in the case,
        /// which is a hard wall the player runs into constantly with bulky powders.
        /// </summary>
        public double PackingFraction => GrainGeometry.Create(Shape, _webThickness, _deterrentCoating).PackingFraction;

        private bool _trayDirty;

        private void OnEnable() => Refresh();

        /// <summary>
        /// Unity forbids DestroyImmediate inside OnValidate, so the tray cannot be torn
        /// down here. Flag it and let Update do it a frame later — the readout is safe
        /// to update immediately either way.
        /// </summary>
        private void OnValidate()
        {
            RefreshReadout();
            _trayDirty = true;
        }

        private void Update()
        {
            if (!_trayDirty) return;

            _trayDirty = false;
            RebuildTray();
        }

        // ------------------------------------------------------------------

        /// <summary>Presses the grains finer or coarser. Bound to a draggable gauge, so
        /// grain size is set by moving a tool.</summary>
        public void SetWeb(double metres) => WebThickness = metres;

        /// <summary>Runs the batch through the coating drum. More passes, more
        /// deterrent.</summary>
        public void SetDeterrent(double fraction) => DeterrentCoating = fraction;

        /// <summary>Swaps the extrusion die. Discrete, so it is a selection rather than
        /// a drag — there is no halfway between a sphere and a flake.</summary>
        public void SetShape(GrainShape shape) { Shape = shape; Refresh(); }

        /// <summary>Cycles to the next die, for a single-button control.</summary>
        public void NextShape()
        {
            switch (Shape)
            {
                case GrainShape.Sphere: Shape = GrainShape.Flake; break;
                case GrainShape.Flake: Shape = GrainShape.Cord; break;
                case GrainShape.Cord: Shape = GrainShape.SinglePerforated; break;
                case GrainShape.SinglePerforated: Shape = GrainShape.SevenPerforated; break;
                default: Shape = GrainShape.Sphere; break;
            }

            Refresh();
        }

        /// <summary>Writes the milled powder into a design.</summary>
        public void ApplyTo(ref CartridgeDesign design)
        {
            design.PropellantId = BaseId;
            design.GrainShape = Shape;
            design.WebThickness = _webThickness;
            design.DeterrentCoating = _deterrentCoating;
        }

        /// <summary>Sets the mill up from an existing load, so opening a saved design
        /// puts the tools where that design left them.</summary>
        public void ReadFrom(in CartridgeDesign design)
        {
            BaseId = design.PropellantId;
            Shape = design.GrainShape;
            _webThickness = Clamp(design.WebThickness, MinimumWeb, MaximumWeb);
            _deterrentCoating = Clamp(design.DeterrentCoating, 0.0, 1.0);
            Refresh();
        }

        // ------------------------------------------------------------------

        private void Refresh()
        {
            RefreshReadout();
            RebuildTray();
        }

        private void RefreshReadout()
        {
            if (Readout != null)
            {
                // Dimensions and observations only. Nothing here says how it will shoot.
                string name = PropellantLibrary.TryGet(BaseId, out var properties)
                    ? properties.DisplayName
                    : BaseId;

                Readout.text =
                    $"{name}\n{Shape}\n{WebMillimetres:F3} mm grain\n{BurnCharacter}" +
                    (_deterrentCoating > 0.005 ? "\ncoated" : "\nuncoated");
            }
        }

        /// <summary>
        /// Lays a sample of the milled grains out in the tray at their true size, so a
        /// coarse powder visibly IS coarse. The grains are the readout.
        /// </summary>
        private void RebuildTray()
        {
            if (GrainTray == null) return;

            for (int i = GrainTray.childCount - 1; i >= 0; i--)
            {
                var child = GrainTray.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            // The web is a half-thickness for a flake and a radius for a sphere or cord,
            // so the visible grain is about twice the web across in its smallest
            // dimension. Good enough that coarse looks coarse.
            float size = (float)(_webThickness * 2.0);
            float spread = size * 6f;

            for (int i = 0; i < SampleGrains; i++)
            {
                var grain = GameObject.CreatePrimitive(PrimitiveForShape());
                grain.name = $"Grain {i + 1}";
                grain.transform.SetParent(GrainTray, false);
                grain.hideFlags = HideFlags.DontSave;

                // Deterministic golden-angle spiral. No random placement anywhere in
                // this game: the same recipe must always look the same.
                float angle = i * 2.399963f;
                float radius = spread * Mathf.Sqrt(i + 1) * 0.35f;
                grain.transform.localPosition =
                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                grain.transform.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
                grain.transform.localScale = ScaleForShape(size);

                if (GrainMaterial != null)
                    grain.GetComponent<MeshRenderer>().sharedMaterial = GrainMaterial;
            }
        }

        private PrimitiveType PrimitiveForShape()
            => Shape == GrainShape.Sphere ? PrimitiveType.Sphere : PrimitiveType.Cylinder;

        private Vector3 ScaleForShape(float size)
        {
            switch (Shape)
            {
                // A flake is a thin disc: wide across, barely anything through.
                case GrainShape.Flake: return new Vector3(size * 3f, size * 0.15f, size * 3f);

                // Cords and tubes are extruded lengths, several diameters long.
                case GrainShape.Cord:
                case GrainShape.SinglePerforated:
                case GrainShape.SevenPerforated:
                    return new Vector3(size, size * 2.5f, size);

                default: return Vector3.one * size;
            }
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}
