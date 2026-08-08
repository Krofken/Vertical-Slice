using Gunsmith.Interaction;
using Krofken.Ballistics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The powder dispenser: a machine with a screen. Choose a charge, press the button, and that
    /// much propellant goes into the case.
    ///
    /// WHY A MACHINE. Charging was twice attempted as a feel-based act — a poise you slid until a
    /// beam levelled, then a tin you tipped with several thousand simulated grains falling into a
    /// pan. The second one worked as physics and failed as a game: the weight fell outside the
    /// lean-in frame, the pile stopped growing partway through, the grains flickered. A mechanism
    /// that has to be perfect before it is usable at all is the wrong shape for the one number on
    /// the bench the player most needs to control.
    ///
    /// So the charge is now STATED. You dial it, you read it, you press the button. That is not a
    /// retreat from the canon's "operate tools, don't fill in a form" — a dispenser is a real tool
    /// a real handloader owns, and it is the same class of thing as the sanctioned chronograph: an
    /// instrument bought to remove guesswork, diegetic, and honest about what it reports.
    ///
    /// It also removes a hazard. A charge inferred from where rigid bodies came to rest is not
    /// reproducible frame to frame, and the canon forbids hidden variation — especially the kind
    /// that looks like physics rather than a bug.
    ///
    /// WHAT IT MAY SAY, and this is the line that matters: the charge, and whether it FITS. Both
    /// are facts about objects in your hands. Whether a case can physically swallow a charge is the
    /// one refusal the bench is explicitly allowed to make, because a gunsmith can see it — the
    /// interior solver refuses the same load for the same reason. What it must never say is what
    /// the charge will DO. No pressure, no velocity, no warning that a load is hot. That is what the
    /// range is for.
    /// </summary>
    [AddComponentMenu("Gunsmith/Powder Dispenser")]
    public sealed class PowderDispenser : MonoBehaviour
    {
        [Header("Feeds")]
        [Tooltip("Where the dispensed charge is recorded.")]
        public PowderBalance Charge;

        [Tooltip("The mill, for which powder is being thrown. Its bulk decides how much of a " +
                 "given weight will physically fit.")]
        public PropellantMill Mill;

        [Tooltip("The die, so the space the bullet takes up in the case is accounted for. " +
                 "Optional.")]
        public SeatingStop Die;

        [Tooltip("Case being charged. 9 mm only, by scope.")]
        public string CaseId = CartridgeCaseLibrary.NineMillimetre;

        [Header("Controls")]
        [Tooltip("Grains added or removed per press.")]
        public double Step = 0.1;

        [Tooltip("Grains per press while held down.")]
        public double FastStep = 0.5;

        [Tooltip("How close you have to be to work it, metres.")]
        public float Reach = 2.4f;

        [Header("Screen")]
        [Tooltip("The row labels, down the left of the screen. Left-aligned.")]
        public TextMesh Labels;

        [Tooltip("The figures, down the right of the screen. Right-aligned, so the numbers " +
                 "line up under each other instead of wandering with their own width.")]
        public TextMesh Values;

        [Header("Buttons")]
        public Transform UpButton;
        public Transform DownButton;
        public Transform DispenseButton;

        [Header("Appearance")]
        public Color Normal = new Color(0.62f, 0.95f, 0.68f);
        public Color Warning = new Color(1f, 0.45f, 0.35f);

        private Camera _eye;

        private Camera Aiming => _eye != null && _eye.isActiveAndEnabled ? _eye : Camera.main;

        // ------------------------------------------------------------------

        /// <summary>Charge currently dialled up, grains.</summary>
        public double Selected => Charge != null ? Charge.SettingGrains : 0.0;

        /// <summary>
        /// Space in the case the powder has to fit into, cubic metres.
        ///
        /// The case's own capacity less whatever the seated bullet occupies — which is why seating
        /// deeper leaves less room, and why it is the sharpest pressure lever on the bench. Taken
        /// from the same library row the interior solver reads, so the machine and the physics can
        /// never disagree about how big the case is.
        /// </summary>
        public double CaseVolume
        {
            get
            {
                if (!CartridgeCaseLibrary.TryGet(CaseId, out var brass)) return 0.0;

                double seated = 0.0;
                if (Die != null)
                    seated = CartridgeBaker.SeatedVolume(Die.Projectile, Die.Depth);

                double room = brass.Capacity - seated;
                return room < 0.0 ? 0.0 : room;
            }
        }

        /// <summary>
        /// Room the selected charge would actually take up, cubic metres.
        ///
        /// BULK, not solid. Powder is poured, so what matters is the space the granules occupy
        /// INCLUDING the air trapped between them — which is why a bulky flake powder runs out of
        /// case before a dense ball powder of the same weight does. The mill's own packing fraction
        /// is the number that says so.
        /// </summary>
        public double ChargeVolume
        {
            get
            {
                if (Charge == null) return 0.0;
                if (!PropellantLibrary.TryGet(PowderId, out var powder)) return 0.0;
                if (powder.SolidDensity <= 0.0) return 0.0;

                double packing = Mill != null ? Mill.PackingFraction : 0.6;
                if (packing < 0.05) packing = 0.05;

                return Charge.SettingGrains > 0.0
                    ? Units.GrainsToKilograms(Charge.SettingGrains) / (powder.SolidDensity * packing)
                    : 0.0;
            }
        }

        private string PowderId => Mill != null ? Mill.BaseId : PropellantLibrary.SingleBase;

        /// <summary>How full of powder the case would be, 1.0 being level with the mouth.</summary>
        public double Fill
        {
            get
            {
                double room = CaseVolume;
                return room > 0.0 ? ChargeVolume / room : 0.0;
            }
        }

        /// <summary>True when the selected charge simply will not go in the case.</summary>
        public bool Overfull => Fill > 1.0;

        // ------------------------------------------------------------------

        private void OnEnable()
        {
            if (Charge == null) Charge = GetComponentInParent<PowderBalance>();
            Refresh();
        }

        /// <summary>
        /// One more refresh once everything exists.
        ///
        /// OnEnable runs while the machine is still being assembled, so the bounds every fit
        /// depends on are not yet real. Start is the first moment they are.
        /// </summary>
        private void Start() => Refresh();

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool held = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
            double step = held ? FastStep : Step;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (Pressed(UpButton, mouse)) Select(Selected + step);
                else if (Pressed(DownButton, mouse)) Select(Selected - step);
                else if (Pressed(DispenseButton, mouse)) Throw();
            }
        }

        private bool Pressed(Transform button, Mouse mouse)
            => button != null && Aim.IsUnderAim(Aiming, button.gameObject, Reach, mouse);

        /// <summary>Dials the charge up or down.</summary>
        public void Select(double grains)
        {
            if (Charge == null) return;

            Charge.SettingGrains = grains;
            Refresh();
        }

        /// <summary>
        /// Throws the selected charge into the case.
        ///
        /// REFUSES AN OVERFULL CASE, and only that. A charge that will not physically fit is an
        /// assembly fault — a fact about objects on the bench that the player could see for
        /// themselves — and the bench is entitled to stop them. A charge that fits and will burst
        /// the case goes in without comment, because the only honest way to learn that is to fire
        /// it and read the brass.
        /// </summary>
        public void Throw()
        {
            if (Charge == null) return;

            if (Overfull)
            {
                Refresh();
                return;
            }

            Charge.Dispense(Charge.SettingGrains);
            Refresh();
        }

        /// <summary>Empties the case back out.</summary>
        public void Dump()
        {
            if (Charge == null) return;

            Charge.Empty();
            Refresh();
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Writes the screen.
        ///
        /// LABELS AND FIGURES ARE SEPARATE, and both are one block of lines rather than a
        /// sentence. A sentence per row wandered across the machine and read as text floating in
        /// the air; a label column and a figure column read as an instrument, because the numbers
        /// sit under each other and the eye can compare them. The figures are right-aligned for the
        /// same reason a real readout is: "5.5" and "10.2" must end in the same place or the
        /// decimal point moves as you dial.
        ///
        /// LOAD DENSITY is the handloader's own term for how full of powder a case is, so it is
        /// what the label says. It is a measurement of the thing on the bench, not a prediction.
        /// </summary>
        public void Refresh()
        {
            bool charged = Charge != null && Charge.PouredGrains > 0.0;

            if (Labels != null)
                Labels.text = "charge\nload density\nin case\n";

            if (Values != null)
            {
                var figures = new System.Text.StringBuilder();

                figures.Append($"{Selected:F1} gr\n");
                figures.Append($"{Fill * 100.0:F0} %\n");
                figures.Append(charged ? $"{Charge.PouredGrains:F1} gr\n" : "—\n");

                // The one refusal the bench is allowed to make, and it goes where the eye already
                // is rather than in a fourth column.
                figures.Append(Overfull ? "WILL NOT FIT" : string.Empty);

                Values.text = figures.ToString();
                Values.color = Overfull ? Warning : Normal;
            }

            if (Labels != null) Labels.color = Overfull ? Warning : Normal;

            FitToGlass();
            FitMarks();
        }

        /// <summary>
        /// Puts each button's arrow on its face.
        ///
        /// IN A SECOND PASS, and it has to be. Renderer.bounds is meaningless in the frame the
        /// object was created — the hierarchy has not been updated and the renderer is not yet
        /// registered — so fitting the marks at build time put all three of them a hand's width
        /// above the machine at a scale of 0.001. This project already had that lesson written down
        /// for the order cards: bounds are only valid once the object is fully built, so fit
        /// afterwards.
        /// </summary>
        private void FitMarks()
        {
            Place(UpButton);
            Place(DownButton);
            Place(DispenseButton);
        }

        private static void Place(Transform button)
        {
            if (button == null || button.parent == null) return;

            var mark = button.parent.Find(button.name + " mark");
            if (mark == null) return;

            var text = mark.GetComponent<TextMesh>();
            var face = button.GetComponent<Renderer>();
            if (text == null || face == null) return;

            var bounds = face.bounds;
            if (bounds.size.x <= 1e-5f) return;

            // SIZED DIRECTLY, not through TextFit. Fitting is the right tool for a block of text
            // whose length changes; for a single glyph on a known face it overshot hard and left
            // the arrows a millimetre tall. A TextMesh glyph is about characterSize high, so
            // deriving it from the button is both simpler and stable.
            // A TextMesh glyph renders roughly SIX TIMES its characterSize at fontSize 72, not one
            // times — measured, after a first attempt left arrows the size of the machine. Divided
            // again by the number of characters so "FILL" fits the same face a single arrow does.
            mark.localScale = Vector3.one;

            float across = Mathf.Max(1, text.text.Length);
            text.characterSize = bounds.size.y * 0.62f / 6f / Mathf.Sqrt(across);

            mark.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 0.0012f);
        }

        [Tooltip("The glass the readout is printed on. The text is fitted to it rather than " +
                 "sized by hand.")]
        public Renderer Glass;

        [Tooltip("Fraction of the glass width each column may use.")]
        [Range(0.3f, 0.7f)] public float ColumnWidth = 0.46f;

        /// <summary>
        /// Scales the readout to actually fit the screen.
        ///
        /// NEVER A HAND-PICKED CHARACTER SIZE, which is a rule this project learned the hard way
        /// and which I broke again here: a TextMesh has no layout, does not wrap and does not know
        /// how big the panel it is printed on is, so a constant that looks right for "5.5 gr" is
        /// wrong for "load density" — and the first version of this screen rendered four times the
        /// size of the machine, floating above it, with the two columns printed over each other.
        ///
        /// Fitting has two traps, both already documented for the order cards. Bounds are only
        /// valid once the text has been rendered, so this runs on every refresh rather than once at
        /// build time; and fitting MULTIPLIES scale, so each column is reset to its resting scale
        /// first or it ratchets smaller every time the charge changes.
        /// </summary>
        private void FitToGlass()
        {
            if (Glass == null) return;

            var bounds = Glass.bounds;
            var area = new Vector2(bounds.size.x * ColumnWidth, bounds.size.y);

            Fit(Labels, area, ref _labelRest, ref _labelRestKnown);
            Fit(Values, area, ref _valueRest, ref _valueRestKnown);

            // PLACED FROM THE GLASS, not from numbers I worked out by hand. Hand-computed local
            // offsets put the readout floating in the air above the machine — the same class of
            // error as hand-picking the character size, and fixed the same way: measure the surface
            // and put the text on it.
            //
            // The player stands on -Z, so the front of the glass is its minimum Z, and the text
            // sits a shade in front of that to win the depth test.
            float inset = bounds.size.y * 0.06f;
            float front = bounds.min.z - 0.0015f;

            if (Labels != null)
                Labels.transform.position =
                    new Vector3(bounds.min.x + inset, bounds.max.y - inset, front);

            if (Values != null)
                Values.transform.position =
                    new Vector3(bounds.max.x - inset, bounds.max.y - inset, front);
        }

        private static void Fit(TextMesh text, Vector2 area, ref Vector3 rest, ref bool known)
        {
            if (text == null) return;

            if (!known) { rest = text.transform.localScale; known = true; }

            text.transform.localScale = rest;
            Interaction.TextFit.Fit(text, area);
        }

        private Vector3 _labelRest, _valueRest;
        private bool _labelRestKnown, _valueRestKnown;
    }
}
