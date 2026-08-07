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
                return new CraftResult { Message = "Nothing set up in the press." };

            var result = game.Workshop.Craft(design, BatchSize);
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
            if (Readout == null) return;

            if (game == null || design == null)
            {
                Readout.text = "press empty";
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

            if (!bill.CanBuild) text.Append($"short of {bill.FirstShortage}");

            Readout.text = text.ToString();
        }
    }
}
