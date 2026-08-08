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
        [Tooltip("Sample of the finished powder. Rebuilt whenever the recipe changes.")]
        public Transform GrainTray;

        [Tooltip("Widest the pile may spread in the pan, metres. A ceiling, not the size — " +
                 "the pile's actual footprint is derived from the granules in it.")]
        public float HeapDiameter = 0.05f;

        // ---- How the powder is DRAWN -------------------------------------
        //
        // A FLAT EXAGGERATION FACTOR DOES NOT WORK, and this is what made the pan go strange
        // as the wheel was turned. Multiplying the web by a constant 14 spans the web's own
        // 25:1 range, so the coarse end came out as 14 mm boulders while the fine end pinned
        // against the granule cap and stopped changing at all — no visible difference across
        // the whole useful pistol range, then a collapse to six lumps.
        //
        // So the drawn granule is mapped LOGARITHMICALLY into a band of sizes real powder
        // actually comes in, and the count follows from conserving the sample's volume. Both
        // stay monotonic in the web, which is the only thing the player has to be able to
        // read: coarser is always bigger and always fewer.

        [Tooltip("The pinch of powder in the pan, cubic metres. CONSTANT — grinding rearranges " +
                 "this volume into more, smaller pieces and never creates or destroys any of it.")]
        public float PowderVolume = 3.0e-7f;

        [Tooltip("Granules drawn for the FINEST powder the mill can press.")]
        [Range(200, 4000)] public int MaxGrains = 2400;

        [Tooltip("Granules drawn for the COARSEST powder. The ratio between this and MaxGrains " +
                 "is what fixes the size range: conserving volume means the diameter can only " +
                 "change by the cube root of the change in count.")]
        [Range(20, 1000)] public int MinGrains = 200;

        [Tooltip("How loosely the pile sits, as a multiple of the ideal packed radius. Above 1 " +
                 "the pile is looser than solid-packed, which is what powder actually does.")]
        [Range(1f, 2.5f)] public float PileSpread = 1.35f;

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

        [Tooltip("Most times a batch can go through the coating drum. Only how the coat is " +
                 "COUNTED for the player — the deterrent itself stays continuous, because that " +
                 "is what the burn model reads.")]
        [Range(2, 12)] public int DrumPasses = 6;

        /// <summary>
        /// The coat, counted in trips through the drum.
        ///
        /// THE DRUM IS NOT A SWITCH, though its readout used to make it look like one. It printed
        /// "coated" above a threshold of 0.005 and "uncoated" below, which collapsed a continuous
        /// value into two words — so dragging it felt like operating a control with two positions
        /// and no reason to be a drag at all. The value itself was never binary:
        /// <see cref="DeterrentCoating"/> runs 0 to 1 and feeds the burn model, where it decides
        /// how far the outside of a granule lags its core and therefore how progressively the
        /// charge burns. Making the control a switch would have thrown that away.
        ///
        /// Passes are how a real batch is coated — you run it through again — so counting them is
        /// a dimension of the work rather than a prediction about it, which is the line the canon
        /// draws. Still nothing here about how it will shoot.
        /// </summary>
        public string Coating
        {
            get
            {
                int passes = (int)System.Math.Round(_deterrentCoating * DrumPasses);

                if (passes <= 0) return "uncoated";
                return passes == 1 ? "1 pass in the drum" : $"{passes} passes in the drum";
            }
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

        private void OnEnable()
        {
            EnsureControls();
            Refresh();
        }

        /// <summary>
        /// Fits the refiner if the mill has none.
        ///
        /// Self-fitting because the authored shop is a frozen prefab the bootstrap ADOPTS rather
        /// than rebuilds, so anything only the builder knows about never reaches the game being
        /// played. That has now caught the press's readout, the die's handle, the granule material
        /// and the charge dispenser, so it is the default assumption.
        ///
        /// RUNTIME ONLY -- this is [ExecuteAlways], and building objects in edit mode dirties the
        /// scene, which turns every domain reload into a "save your changes?" dialog.
        /// </summary>
        private void EnsureControls()
        {
            if (!Application.isPlaying) return;
            if (GetComponentInChildren<PowderRefiner>(true) != null) return;

            var machine = new GameObject("Powder refiner");
            machine.transform.SetParent(transform, false);
            machine.transform.localPosition = new Vector3(0f, 0f, 0.075f);

            var refiner = machine.AddComponent<PowderRefiner>();
            refiner.Mill = this;

            const float face = -0.0215f;

            refiner.Glass = BenchScreen.Cabinet(machine.transform,
                new Vector3(0f, 0.036f, 0.014f), new Vector3(0.130f, 0.072f, 0.070f), face);

            refiner.Labels = BenchScreen.Column(machine.transform, "Labels",
                TextAnchor.UpperLeft, TextAlignment.Left);

            refiner.Values = BenchScreen.Column(machine.transform, "Values",
                TextAnchor.UpperRight, TextAlignment.Right);

            // ROW, then LESS and MORE, then PRESS. Selecting a row and changing it are separate
            // because four properties would otherwise need eight buttons on a machine this size.
            refiner.RowButton = BenchScreen.Button(machine.transform, "Row", "▼",
                new Vector3(-0.050f, 0.013f, face), new Color(0.45f, 0.50f, 0.60f));

            refiner.LessButton = BenchScreen.Button(machine.transform, "Less", "◀",
                new Vector3(-0.024f, 0.013f, face), new Color(0.55f, 0.75f, 0.95f));

            refiner.MoreButton = BenchScreen.Button(machine.transform, "More", "▶",
                new Vector3(0.002f, 0.013f, face), new Color(0.55f, 0.75f, 0.95f));

            refiner.RefineButton = BenchScreen.Button(machine.transform, "Refine", "PRESS",
                new Vector3(0.042f, 0.013f, face), new Color(0.85f, 0.65f, 0.30f));

            refiner.Refresh();

            var interactable = GetComponent<Interaction.Interactable>();
            if (interactable != null) interactable.Prompt = "refine the powder";
        }

        // ------------------------------------------------------------------
        // What the refiner presses. These are the mill's actual settings; the machine at the
        // station composes a recipe and then calls them, which is why they take a value rather
        // than stepping one.
        // ------------------------------------------------------------------

        /// <summary>Presses the granules finer or coarser.</summary>
        public void SetWeb(double metres) => WebThickness = metres;

        /// <summary>Runs the batch through the coating drum. More passes, more deterrent.</summary>
        public void SetDeterrent(double fraction) => DeterrentCoating = fraction;

        /// <summary>Fits a different extrusion die. Discrete — there is no halfway between a
        /// sphere and a flake.</summary>
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

        /// <summary>Sets the mill up from an existing load, so opening a saved design puts the
        /// tools where that design left them.</summary>
        public void ReadFrom(in CartridgeDesign design)
        {
            BaseId = design.PropellantId;
            Shape = design.GrainShape;
            _webThickness = Clamp(design.WebThickness, MinimumWeb, MaximumWeb);
            _deterrentCoating = Clamp(design.DeterrentCoating, 0.0, 1.0);
            Refresh();
        }

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
                    $"{name}\n{Shape}\n{WebMillimetres:F3} mm grain\n{BurnCharacter}\n{Coating}";
            }
        }

        /// <summary>
        /// Lays the milled powder out in the pan as individual granules.
        ///
        /// ONE OBJECT PER GRAIN, and the COUNT is what carries the reading. The sample's
        /// volume is held constant, so grinding coarser does not merely inflate each granule —
        /// it produces FEWER of them, because the same pan of powder is now made of bigger
        /// pieces. Count falls as the cube of the diameter. That is the comparison the player
        /// is meant to see, it is physically honest, and it is legible at a glance in a way
        /// that neither two dozen scattered dots nor a textured dome ever was.
        ///
        /// POOLED, NOT REBUILT. The web changes every frame while the grinding wheel is being
        /// dragged, and creating several hundred GameObjects per frame would stall the drag —
        /// which is exactly why the controls appeared to move only after the mouse stopped.
        /// The granules are created once, up to MaxGrains, then only rescaled and shown or
        /// hidden. Rescaling a few hundred transforms per frame costs nothing.
        ///
        /// Presentation only, as the canon requires: nothing here touches a solver, and the
        /// sample is a fixed volume because the mill designs a RECIPE, not an amount. How much
        /// powder you use is the balance's business.
        /// </summary>
        private void RebuildTray()
        {
            if (GrainTray == null) return;

            // The tray is scaled up, so sizes have to be divided back out to land on the real
            // millimetres wanted in the pan.
            float tray = Mathf.Abs(GrainTray.lossyScale.x);
            if (tray < 1e-6f) tray = 1f;

            // COUNT comes from the web; SIZE comes from conserving the volume. That order is the
            // whole fix. Doing it the other way round — size from the web, count from volume —
            // needs a floor on the count to stop a coarse powder collapsing to a few lumps, and
            // that floor is exactly what inflated the pile to thirteen times its volume at the
            // coarse end. Derive count first and there is nothing left to clamp.
            int wanted = GrainCount;
            double apparent = DrawnGrainDiameter;

            float size = (float)(apparent / tray);

            // Constant, because the volume is constant. A pile of the same powder ground finer
            // occupies the same room — that is the point, and it is why this no longer depends on
            // the granule size at all.
            float radius = PileRadius / tray;

            EnsureGrains(wanted);

            for (int i = 0; i < _grains.Count; i++)
            {
                var grain = _grains[i];
                if (grain == null) continue;

                bool shown = i < wanted;
                if (grain.gameObject.activeSelf != shown) grain.gameObject.SetActive(shown);
                if (!shown) continue;

                grain.localPosition = HeapPosition(i, radius, size);
                grain.localRotation = Quaternion.Euler(0f, Hash01(i, 91) * 360f, Hash01(i, 57) * 360f);
                grain.localScale = ScaleForShape(size);
            }
        }

        /// <summary>
        /// How far along the mill's range the web currently sits, 0 (finest) to 1 (coarsest).
        ///
        /// Logarithmic because the range is 25:1 and burn time goes with the web
        /// proportionally, so equal fractions of the wheel's travel should be equal
        /// proportional changes rather than equal absolute ones.
        /// </summary>
        private double Coarseness
        {
            get
            {
                double min = System.Math.Log(MinimumWeb);
                double max = System.Math.Log(MaximumWeb);
                if (max - min < 1e-12) return 0.0;

                double t = (System.Math.Log(_webThickness) - min) / (max - min);
                return t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            }
        }

        /// <summary>
        /// How many granules the pinch is broken into. Many when fine, few when coarse.
        /// </summary>
        public int GrainCount
        {
            get
            {
                // Geometric, so the count falls smoothly rather than in a rush at one end.
                double many = System.Math.Log(System.Math.Max(1, MaxGrains));
                double few = System.Math.Log(System.Math.Max(1, MinGrains));

                return (int)System.Math.Round(System.Math.Exp(many + (few - many) * Coarseness));
            }
        }

        /// <summary>
        /// Diameter a granule is drawn at, metres — WHATEVER SIZE CONSERVES THE VOLUME.
        ///
        /// This is the piece that makes grinding look like grinding. The pinch of powder is a
        /// fixed volume; breaking it into N pieces makes each piece the cube root of 1/N of it.
        /// Grind a one-centimetre rock into dust and the dust, swept back together, is still a
        /// cubic centimetre — nothing is created and nothing destroyed, only rearranged. So the
        /// diameter is not a free parameter and is never chosen: it falls out of the count.
        ///
        /// The consequence, which is the tell that it is right: the PILE NEVER CHANGES SIZE. Only
        /// its granularity does. Before this, size was mapped from the web independently and the
        /// count was clamped to a floor, so at the coarse end the same pinch of powder silently
        /// became thirteen times as much of it and the whole heap swelled.
        /// </summary>
        public double DrawnGrainDiameter
        {
            get
            {
                int count = System.Math.Max(1, GrainCount);

                // Sphere of equal volume: V_each = V_total / N, and V_each = (pi/6) d^3.
                double each = PowderVolume / count;
                return System.Math.Pow(each * 6.0 / System.Math.PI, 1.0 / 3.0);
            }
        }

        /// <summary>
        /// Radius the pile spreads to, metres. Constant, because the volume is.
        ///
        /// Solved from the volume rather than from the granules: a pile roughly as tall as half
        /// its radius, loosened by the shape's own packing fraction, since flakes bridge and trap
        /// air where spheres tumble into a dense bed. Capped at the pan so a bulky powder cannot
        /// spill over the rim.
        /// </summary>
        public float PileRadius
        {
            get
            {
                // V = pi r^2 h * packing, with h = r / 2.
                double packed = System.Math.Max(0.05, PackingFraction);
                double solved = System.Math.Pow(
                    PowderVolume / (System.Math.PI * 0.5 * packed), 1.0 / 3.0);

                return Mathf.Min((float)solved * PileSpread, HeapDiameter * 0.5f);
            }
        }

        private readonly System.Collections.Generic.List<Transform> _grains =
            new System.Collections.Generic.List<Transform>();

        private GrainShape _pooledShape;

        /// <summary>Makes granules up to the count needed, reusing any that already exist.</summary>
        private void EnsureGrains(int wanted)
        {
            // A die change alters the primitive, so the pool has to go — but only then, never
            // on a mere size change, which is the common case while dragging.
            if (_grains.Count > 0 && _pooledShape != Shape) Discard();
            _pooledShape = Shape;

            for (int i = _grains.Count - 1; i >= 0; i--)
                if (_grains[i] == null) _grains.RemoveAt(i);

            while (_grains.Count < wanted)
            {
                var grain = GameObject.CreatePrimitive(PrimitiveForShape());
                grain.name = "Grain " + (_grains.Count + 1);
                grain.transform.SetParent(GrainTray, false);
                grain.hideFlags = HideFlags.DontSave;

                // NO COLLIDER. Nothing ever touches these, and a few hundred colliders in the
                // middle of the bench is something the character controller depenetrates itself
                // out of — which once flung the player two hundred metres across the map.
                var solid = grain.GetComponent<Collider>();
                if (solid != null)
                {
                    if (Application.isPlaying) Destroy(solid); else DestroyImmediate(solid);
                }

                if (GrainMaterial != null)
                {
                    var renderer = grain.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = GrainMaterial;

                    // GPU instancing, so several hundred granules cost a handful of draw calls
                    // rather than several hundred.
                    GrainMaterial.enableInstancing = true;
                }

                _grains.Add(grain.transform);
            }
        }

        private void Discard()
        {
            for (int i = 0; i < _grains.Count; i++)
            {
                if (_grains[i] == null) continue;

                var go = _grains[i].gameObject;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }

            _grains.Clear();
        }

        /// <summary>
        /// Where one granule sits in the mound.
        ///
        /// Deterministic, from an integer hash rather than Random: the same recipe must always
        /// look identical, which is the rule the rest of this project's presentation follows.
        /// Height is biased towards the base and the radius narrows towards the top, which is
        /// what makes it read as a poured pile rather than a slab.
        /// </summary>
        private static Vector3 HeapPosition(int index, float radius, float size)
        {
            float t = Hash01(index, 13);
            float height = t * t;

            float available = radius * (1f - height * 0.85f);

            // Golden-angle sweep with a hashed radius, so successive granules never line up
            // into visible spokes.
            float angle = index * 2.399963f;
            float r = available * Mathf.Sqrt(Hash01(index, 71));

            return new Vector3(
                Mathf.Cos(angle) * r,
                height * radius * 0.5f + size * 0.5f,
                Mathf.Sin(angle) * r);
        }

        /// <summary>Repeatable value in [0,1) from an index and a salt. No seed to lose.</summary>
        private static float Hash01(int index, int salt)
        {
            int h = index * 374761393 + salt * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        private PrimitiveType PrimitiveForShape()
            => Shape == GrainShape.Sphere ? PrimitiveType.Sphere : PrimitiveType.Cylinder;

        /// <summary>
        /// The granule's proportions, so the die you fitted is visible in the pan.
        ///
        /// A flake really is a thin disc and a cord really is an extruded length, and those are
        /// the shapes that decide whether the burning surface shrinks, holds or grows as the
        /// charge is consumed. Seeing them is the only way the die choice is legible at all.
        /// </summary>
        private Vector3 ScaleForShape(float size)
        {
            switch (Shape)
            {
                case GrainShape.Flake: return new Vector3(size * 2.6f, size * 0.22f, size * 2.6f);

                case GrainShape.Cord:
                case GrainShape.SinglePerforated:
                case GrainShape.SevenPerforated:
                    return new Vector3(size * 0.8f, size * 2.2f, size * 0.8f);

                default: return Vector3.one * size;
            }
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}
