using Gunsmith.Economy;
using Gunsmith.GameLoop;
using Gunsmith.Workshop;
using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The press: where the four stations become a cartridge.
    ///
    /// Nothing is designed here. The press only gathers what the other tools made — the
    /// powder from the mill, the bullet from the core bench, the charge off the balance,
    /// the seat off the die — adds a case and a primer, and pulls the handle. That is
    /// exactly what a loading press does, and it is why the balance and the seating die
    /// belong to this station rather than standing alone.
    ///
    /// WHAT IT MAY SHOW: the bill. What a batch will consume is the definition of a
    /// consumption number, and it is the figure that makes the night's scarcity real —
    /// this batch costs you that much lead and those primers.
    ///
    /// WHAT IT MAY NOT SHOW: anything about how the round will shoot. The press knows
    /// the baked interior ballistics because it has to bake to build the bill, and it
    /// must keep that to itself. The player finds out at the range.
    /// </summary>
    [AddComponentMenu("Gunsmith/Loading Press")]
    public sealed class LoadingPress : MonoBehaviour
    {
        [Header("Stations feeding this press")]
        public PropellantMill Mill;
        public LatheStation CoreBench;
        public PowderBalance Balance;
        public SeatingStop Die;

        [Header("Batch")]
        [Tooltip("Rounds per pull of the handle. The night is finite, so a batch is one " +
                 "action rather than one round.")]
        [Min(1)] public int BatchSize = 20;

        [Tooltip("Case this press is set up for. 9 mm only, by scope.")]
        public string CaseId = CartridgeCaseLibrary.NineMillimetre;

        [Header("Readout")]
        public TextMesh Readout;

        /// <summary>
        /// What the last pull of the handle actually did, in the press's own words.
        ///
        /// THE HANDLE APPEARED TO DO NOTHING, and this is why. Pulling it ran the whole
        /// chain correctly — compose, commit, bake, consume, put rounds on the shelf — and
        /// then reported the outcome to <see cref="Debug.Log"/>, which a player standing in
        /// the shop cannot see. The builder never assigned <see cref="Readout"/> either, and
        /// the shop deliberately has no status board, so a successful batch and a failed one
        /// looked identical: nothing happened.
        ///
        /// It is a CONSUMPTION report and nothing else. How many rounds came off the press
        /// and what they ate is the definition of a consumption number and is what makes the
        /// night's scarcity real. Not one word about how they will shoot.
        /// </summary>
        private string _lastPull;

        /// <summary>
        /// Makes sure there is somewhere to report before the handle is ever pulled.
        ///
        /// Not left to the first pull: a press with no visible readout looks like a press
        /// that does nothing, which is precisely the complaint this is fixing. It should
        /// read "press empty" while it is empty.
        /// </summary>
        private void Start()
        {
            if (Readout == null) Readout = FindReadout();
            if (Readout != null && string.IsNullOrEmpty(Readout.text)) Readout.text = "press empty";
        }

        /// <summary>
        /// Gathers the four stations into one cartridge.
        ///
        /// Every field comes from a tool. Nothing is defaulted here that a station could
        /// have supplied, because a value the player never set is a value they cannot
        /// learn from.
        /// </summary>
        public CartridgeDesign Compose()
        {
            var design = new CartridgeDesign { CaseId = CaseId };

            if (CoreBench != null) CoreBench.ApplyTo(ref design);
            if (Mill != null) Mill.ApplyTo(ref design);
            if (Balance != null) Balance.ApplyTo(ref design);

            if (Die != null)
            {
                // The die seats whatever the core bench just turned, so it has to be
                // holding the current bullet before its depth means anything.
                Die.Projectile = design.Projectile;
                Die.ApplyTo(ref design);
            }

            return design;
        }

        /// <summary>Cartridge overall length of the composed round, mm. What calipers
        /// read, and what a chamber cares about.</summary>
        public double OverallLengthMm => Die != null ? Die.OverallLengthMm : 0.0;

        /// <summary>
        /// Saves the composed cartridge into the player's library, baking it.
        ///
        /// Naming it is the player's act of committing to a recipe, and it is what makes
        /// duplicate-and-tweak possible later — an unnamed load cannot be compared to
        /// its own Mk2.
        /// </summary>
        public SavedDesign Commit(GunsmithGame game, string id, string name)
        {
            if (game == null) return null;

            var design = Compose();
            design.Name = name;

            return game.SaveDesign(id, name, design);
        }

        /// <summary>
        /// Works out what a batch would consume without consuming it. Pure query.
        /// </summary>
        public BillOfMaterials Bill(GunsmithGame game, SavedDesign design)
            => game == null || design == null
                ? new BillOfMaterials()
                : game.Workshop.BuildBill(design, BatchSize);

        /// <summary>
        /// Pulls the handle. Consumes case, primer, powder and bullet stock, and puts
        /// finished rounds on the shelf.
        /// </summary>
        public CraftResult Press(GunsmithGame game, SavedDesign design)
        {
            if (game == null || design == null)
            {
                var nothing = new CraftResult { Message = "Nothing set up in the press." };
                _lastPull = nothing.Message;
                RefreshReadout(game, design);
                return nothing;
            }

            var result = game.Workshop.Craft(design, BatchSize);

            // Say what came off the press, where the player can see it. On a refusal this
            // is the assembly fault or the shortage — both facts about objects on the
            // bench, which the bench is entitled to state.
            _lastPull = result.Success
                ? $"pulled: {result.RoundsProduced} rounds of {design.Name}"
                : $"pulled: nothing. {result.Message}";

            RefreshReadout(game, design);
            return result;
        }

        /// <summary>Composes, commits and presses in one action, which is what a single
        /// click on the handle should do.</summary>
        public CraftResult PressBatch(GunsmithGame game, string id, string name)
        {
            var design = Commit(game, id, name);
            return Press(game, design);
        }

        /// <summary>
        /// Writes the bill onto the press.
        ///
        /// Consumption only. If a predicted velocity, pressure or penetration figure
        /// ever appears in this method, the reason to walk out to the range has gone and
        /// the game has quietly become a spreadsheet.
        /// </summary>
        public void RefreshReadout(GunsmithGame game, SavedDesign design)
        {
            if (Readout == null) Readout = FindReadout();
            if (Readout == null) return;

            if (game == null || design == null)
            {
                Readout.text = string.IsNullOrEmpty(_lastPull) ? "press empty" : _lastPull;
                return;
            }

            var bill = Bill(game, design);
            var text = new System.Text.StringBuilder();

            text.Append(design.Name).Append('\n');
            text.Append($"{BatchSize} rounds\n");
            text.Append($"{OverallLengthMm:F2} mm overall\n");

            for (int i = 0; i < bill.Lines.Count; i++)
            {
                var line = bill.Lines[i];

                text.Append(line.IsCounted
                    ? $"{line.Count} x {line.DisplayName}\n"
                    : $"{Units.KilogramsToGrains(line.Mass):F0} gr {line.DisplayName}\n");
            }

            if (!bill.CanBuild) text.Append($"short of {bill.FirstShortage}\n");

            // Last, so the eye lands on it: what the handle just did.
            if (!string.IsNullOrEmpty(_lastPull)) text.Append(_lastPull);

            Readout.text = text.ToString();
        }

        /// <summary>
        /// Adopts the readout under the press, or makes one.
        ///
        /// The recurring shape in this project: a component must find its own parts rather
        /// than trusting whoever built it. The press was handed every station it feeds off
        /// and never a readout, so the one piece of feedback the player gets was null.
        ///
        /// ADOPTING IS NOT ENOUGH HERE, and that is the interesting part. The shop the player
        /// walks around is a PREFAB INSTANCE that <c>WorkshopBootstrap</c> adopts rather than
        /// rebuilds, so a label added to <c>WorkshopBuilder</c> appears in a freshly-built
        /// shop and never in the authored one — there is nothing to adopt, because the prefab
        /// was saved before the label existed. Re-authoring the room would fix it and would
        /// also discard the hand-placed layout the prefab exists to preserve.
        ///
        /// So the press builds its own if it has none. RUNTIME ONLY: nothing is serialised in
        /// Play, whereas creating an object in edit mode would dirty the scene and turn every
        /// domain reload into a save prompt. Anything explicitly assigned still wins.
        /// </summary>
        private TextMesh FindReadout()
        {
            foreach (var candidate in GetComponentsInChildren<TextMesh>(includeInactive: true))
                if (candidate.name == "Press readout") return candidate;

            return Application.isPlaying ? BuildReadout() : null;
        }

        /// <summary>
        /// Hangs a readout at the right-hand end of the bench, where the handle is.
        ///
        /// Character size is picked to be legible from standing rather than leaning in —
        /// the press handle is pulled from where you stand, not from a lean-in pose, so this
        /// is the one bench label read at full distance.
        /// </summary>
        private TextMesh BuildReadout()
        {
            var go = new GameObject("Press readout");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0.95f, 0.34f, -0.24f);

            // NO ROTATION, and that is measured rather than assumed. A TextMesh is readable
            // from its own -Z side: its glyphs are laid out towards +X and its forward
            // vector points AWAY from whoever is reading it. The player stands on the -Z
            // side of this bench, so an unrotated label already faces him correctly, and
            // turning it to "face" the player would be what mirrors it.
            //
            // Rendered both ways to check, because the intuition is backwards: identical
            // lit-pixel counts from either side (GUI/Text Shader does not cull), with the
            // glyphs landing correctly only from -Z. Every other label the builder makes
            // is unrotated for the same reason and they are all right.

            var text = go.AddComponent<TextMesh>();
            text.characterSize = 0.006f;
            text.fontSize = 72;
            text.color = new Color(0.95f, 0.93f, 0.88f);
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            return text;
        }
    }
}
