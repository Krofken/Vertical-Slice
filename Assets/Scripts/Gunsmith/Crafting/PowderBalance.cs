using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// What the charge weighs.
    ///
    /// This is now a plain holder for one number — the mass of propellant going into the case —
    /// and the thing that hands it to a design. It is deliberately no longer an interaction.
    ///
    /// WHAT WAS HERE BEFORE, AND WHY IT IS GONE. Charging went through three shapes and the first
    /// two both failed for the same underlying reason: they asked the player to arrive at a number
    /// by feel, through a mechanism that had to be perfect before it was usable at all.
    ///
    ///   A BEAM AND A SLIDING POISE. You dialled a target and trickled until the beam levelled.
    ///     It was never wired to any input at all — SlidePoise and Trickle were called only by the
    ///     builders and the tests — so the charge could not be changed by playing the game.
    ///
    ///   A TIN YOU TIPPED, with flow going as the cube of the tilt, and later several thousand
    ///     individually simulated grains falling into the pan. The pour worked; everything around
    ///     it did not. The weight readout fell off the edge of the lean-in frame, the pile stopped
    ///     growing partway through, and the grains flickered — so it never felt like pouring, which
    ///     is the only thing that would have justified the machinery.
    ///
    /// Both are replaced by <see cref="PowderDispenser"/>: a machine with a screen, where you
    /// choose the charge, read it plainly, and press a button. The number is now stated rather than
    /// inferred from where grains came to rest, which also removes a real hazard — a charge derived
    /// from settled rigid bodies is not reproducible, and the canon forbids hidden variation.
    ///
    /// The beam, poise and pan fields survive because the authored shop is a frozen prefab that
    /// still contains those objects. Nothing drives them now.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Gunsmith/Powder Balance")]
    public sealed class PowderBalance : MonoBehaviour
    {
        [Header("Range")]
        [Tooltip("Largest charge this bench will handle, grains.")]
        public double MaxSettingGrains = 12.0;

        // DOUBLE, not float, and deliberately. This charge goes straight into the interior
        // ballistics ODE, and a serialised float is not exactly the value it prints — which once
        // made a 6-grain charge weigh 6.0000001 grains, precision noise injected into a solver
        // input by a display field. Floats belong at the Unity boundary; this is not the boundary.
        [Tooltip("Legacy: the poise's travel along the beam, metres. Nothing drives the poise " +
                 "any more; kept so saved scenes and the test suite still resolve.")]
        public double BeamTravel = 0.12;

        [Tooltip("Legacy: how far the beam swung when out of balance, degrees.")]
        public float SwingDegrees = 9f;

        [Tooltip("Smallest difference the scale can resolve, grains. A real powder scale reads " +
                 "about a tenth of a grain, which is what makes a charge a quantity rather than " +
                 "an exact figure.")]
        public double SaturationGrains = 0.1;

        [Header("Parts")]
        public Transform Beam;
        public Transform Poise;
        public Transform Pan;

        [Tooltip("Reads what the charge weighs. Consumption only — never what it will do.")]
        public TextMesh BeamReadout;

        // ------------------------------------------------------------------

        [SerializeField] private double _settingGrains = 5.5;
        [SerializeField] private double _pouredGrains;

        /// <summary>Charge selected, grains. What the dispenser is set to.</summary>
        public double SettingGrains
        {
            get => _settingGrains;
            set { _settingGrains = Clamp(value, 0.0, MaxSettingGrains); Refresh(); }
        }

        /// <summary>Charge actually dispensed, grains. This is what gets loaded.</summary>
        public double PouredGrains
        {
            get => _pouredGrains;
            private set { _pouredGrains = value < 0.0 ? 0.0 : value; Refresh(); }
        }

        /// <summary>Charge in kilograms, which is what a cartridge design wants.</summary>
        public double PouredCharge => Units.GrainsToKilograms(_pouredGrains);

        /// <summary>Difference between what was dispensed and what was selected, grains.</summary>
        public double Imbalance => _pouredGrains - _settingGrains;

        /// <summary>True when what was dispensed matches what was asked for.</summary>
        public bool IsLevel => System.Math.Abs(Imbalance) <= SaturationGrains * 0.25;

        /// <summary>True once more has been dispensed than was selected. Never blocked — an
        /// overcharge assembles and fires, and the player finds out from the fired case.</summary>
        public bool IsOver => Imbalance > SaturationGrains * 0.25;

        /// <summary>Legacy beam tilt, degrees. Nothing reads this any more.</summary>
        public float BeamAngle
        {
            get
            {
                if (SaturationGrains <= 0.0) return 0f;

                double t = Imbalance / SaturationGrains;
                if (t > 1.0) t = 1.0;
                else if (t < -1.0) t = -1.0;

                return (float)(t * SwingDegrees);
            }
        }

        private void OnEnable()
        {
            EnsureDispenser();
            Refresh();
        }

        private void OnValidate() => Refresh();

        /// <summary>
        /// Fits the dispenser, and retires the beam it replaced.
        ///
        /// Self-fitting because the authored shop is a frozen prefab that WorkshopBootstrap adopts
        /// rather than rebuilds — so anything only the builder knows about never reaches the game
        /// the player opens. That has now caught the press's readout, the die's handle, the mill's
        /// controls and the granule material, so it is the default assumption here.
        ///
        /// RUNTIME ONLY. This is [ExecuteAlways], and building objects in edit mode dirties the
        /// scene, which turns every domain reload into a "save your changes?" dialog.
        /// </summary>
        private void EnsureDispenser()
        {
            if (!Application.isPlaying) return;
            if (GetComponentInChildren<PowderDispenser>(true) != null) return;

            // The beam and poise are dead weight now — a balance you cannot operate, sitting where
            // the machine goes. Hidden rather than destroyed, because they belong to the saved
            // prefab and destroying prefab contents at runtime is a fight not worth having.
            if (Beam != null) Beam.gameObject.SetActive(false);

            // The old engraved readout goes too. The dispenser's screen says the same thing on a
            // surface, so leaving this one on hangs a second copy of the charge in mid-air beside
            // the machine.
            if (BeamReadout != null) BeamReadout.gameObject.SetActive(false);

            var machine = new GameObject("Powder dispenser");
            machine.transform.SetParent(transform, false);
            machine.transform.localPosition = Vector3.zero;

            var dispenser = machine.AddComponent<PowderDispenser>();
            dispenser.Charge = this;

            Body(machine.transform, "Cabinet", new Vector3(0f, 0.034f, 0.014f),
                new Vector3(0.130f, 0.068f, 0.070f), new Color(0.20f, 0.21f, 0.24f));

            // A REAL SCREEN: a dark recessed panel with the readout ON it. The readout used to be
            // a bare TextMesh hanging in front of the machine, which reads as text floating in the
            // air rather than as an instrument — there was nothing for it to be printed on.
            //
            // The player always stands on -Z, and a TextMesh is readable from its own -Z side, so
            // the panel faces -Z and the text is unrotated. Turning either to "face" the player is
            // what mirrors it.
            const float face = -0.0215f;

            Body(machine.transform, "Screen bezel", new Vector3(0f, 0.044f, face),
                new Vector3(0.104f, 0.040f, 0.004f), new Color(0.08f, 0.09f, 0.10f));

            var glass = Body(machine.transform, "Screen glass", new Vector3(0f, 0.044f, face - 0.0025f),
                new Vector3(0.096f, 0.032f, 0.002f), new Color(0.045f, 0.075f, 0.06f));

            // Nothing should aim at the glass — the buttons are what you press.
            var glassCollider = glass.GetComponent<Collider>();
            if (glassCollider != null) Destroy(glassCollider);

            // Labels down the left, figures down the right, both starting at the top edge of the
            // glass so the rows line up across. Sizes are NOT set here — PowderDispenser fits them
            // to the glass every refresh, because a hand-picked character size on a TextMesh is
            // wrong the moment the text changes.
            dispenser.Glass = glass.GetComponent<Renderer>();

            dispenser.Labels = ScreenText(machine.transform, "Labels",
                new Vector3(-0.045f, 0.058f, face - 0.004f), TextAnchor.UpperLeft, TextAlignment.Left);

            dispenser.Values = ScreenText(machine.transform, "Values",
                new Vector3(0.045f, 0.058f, face - 0.004f), TextAnchor.UpperRight, TextAlignment.Right);

            // Three buttons along the front, each marked with what it does. Arrows rather than
            // colour alone, because two grey cubes tell you nothing about which way is up.
            dispenser.UpButton = Button(machine.transform, "More", "▲",
                new Vector3(-0.048f, 0.014f, face), new Color(0.55f, 0.75f, 0.95f));

            dispenser.DownButton = Button(machine.transform, "Less", "▼",
                new Vector3(-0.022f, 0.014f, face), new Color(0.45f, 0.50f, 0.60f));

            dispenser.DispenseButton = Button(machine.transform, "Dispense", "FILL",
                new Vector3(0.040f, 0.014f, face), new Color(0.85f, 0.65f, 0.30f));

            // The die, for the space the seated bullet takes out of the case.
            //
            // NO POWDER COLUMN. There was one, and it was wrong twice over: its scale was set to
            // the fill FRACTION rather than a fraction OF the case length, so a full case grew a
            // two-metre spike across the bench — and even at the right size it sat inside an opaque
            // case where nothing could see it. Load density on the screen is the honest readout.
            dispenser.Die = FindAnyObjectByType<SeatingStop>();
            dispenser.Mill = FindAnyObjectByType<PropellantMill>();
            dispenser.Refresh();

            // The station's caption, corrected for what it now is.
            var interactable = GetComponent<Interaction.Interactable>();
            if (interactable != null) interactable.Prompt = "set the powder charge";

            // Frame the machine, not the retired beam — the beam's 26 cm span used to dominate the
            // bounds and put the eye 86 cm away, which is why the readout fell off the screen.
            var view = GetComponent<Interaction.StationView>();
            if (view != null)
            {
                view.Work = machine.transform;
                view.Remeasure();
            }
        }

        /// <summary>A coloured solid, with no collider unless it is something you press.</summary>
        private static Transform Body(Transform parent, string name, Vector3 position,
            Vector3 scale, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = WorkshopPaletteColour(colour);

            return go.transform;
        }

        /// <summary>One line of readout on the screen's glass.</summary>
        private static TextMesh ScreenText(Transform parent, string name, Vector3 position,
            TextAnchor anchor, TextAlignment alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var text = go.AddComponent<TextMesh>();
            text.characterSize = 0.0030f;
            text.fontSize = 72;
            text.anchor = anchor;
            text.alignment = alignment;
            text.lineSpacing = 1.0f;
            return text;
        }

        /// <summary>A button with its job written on the face of it.</summary>
        private static Transform Button(Transform parent, string name, string mark, Vector3 position,
            Color colour)
        {
            var button = Body(parent, name, position, new Vector3(0.020f, 0.014f, 0.008f), colour);

            // The mark goes on a child of the BENCH rather than of the button, because the button
            // is a non-uniformly scaled cube — parenting text to it would stretch the glyph with
            // it, and a squashed arrow is worse than no arrow.
            var cap = new GameObject(name + " mark");
            cap.transform.SetParent(button.parent, false);

            var text = cap.AddComponent<TextMesh>();
            text.text = mark;
            text.fontSize = 72;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.06f, 0.06f, 0.07f);

            // FITTED AND PLACED FROM THE BUTTON'S OWN BOUNDS. Working the position out by hand put
            // the marks in the sky the first time; measuring the thing the text goes on cannot.
            // Same rule as the screen, and the same reason: a TextMesh knows nothing about the
            // surface it is printed on, so ask the surface.
            var face = button.GetComponent<Renderer>();
            if (face != null)
            {
                var bounds = face.bounds;

                Interaction.TextFit.Fit(text,
                    new Vector2(bounds.size.x, bounds.size.y), margin: 0.62f);

                // Centred on the face, a hair in front of it. The player is on -Z.
                cap.transform.position = new Vector3(
                    bounds.center.x, bounds.center.y, bounds.min.z - 0.0012f);
            }

            return button;
        }

        private static Material WorkshopPaletteColour(Color colour)
            => Interaction.WorkshopPalette.Flat(colour);

        // ------------------------------------------------------------------

        /// <summary>Puts a measured charge in, grains. What the dispenser calls.</summary>
        public void Dispense(double grains) => PouredGrains = grains;

        /// <summary>Adds to the charge, grains.</summary>
        public void Trickle(double grains)
        {
            if (grains <= 0.0) return;
            PouredGrains = _pouredGrains + grains;
        }

        /// <summary>Throws the charge away and starts again.</summary>
        public void Empty() => PouredGrains = 0.0;

        /// <summary>
        /// Legacy: sets the selection from a distance along the old beam.
        ///
        /// Kept because the test suite drives it and because the arithmetic is still the honest
        /// mapping — a real powder beam is evenly divided, so the setting is linear in how far the
        /// poise has slid. Nothing in the shop calls it.
        /// </summary>
        public void SlidePoise(double distanceAlongBeam)
        {
            if (BeamTravel <= 0.0) return;
            SettingGrains = MaxSettingGrains * Clamp(distanceAlongBeam / BeamTravel, 0.0, 1.0);
        }

        /// <summary>Applies the dispensed charge to a design. Hands over what was actually
        /// measured, which with a dispenser is what was asked for.</summary>
        public void ApplyTo(ref CartridgeDesign design) => design.ChargeMass = PouredCharge;

        // ------------------------------------------------------------------

        private void Refresh()
        {
            // WHAT IS IN THE CASE, not what a poise was set to. A consumption figure, which is the
            // sanctioned kind: it says how much powder this round costs you and nothing about how
            // it will shoot.
            if (BeamReadout != null)
                BeamReadout.text = $"{_pouredGrains:F1} gr";
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}
