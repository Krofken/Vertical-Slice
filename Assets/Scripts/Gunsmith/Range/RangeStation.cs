using Gunsmith.GameLoop;
using Gunsmith.Orders;
using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Range
{
    /// <summary>
    /// The yard: fires a load and puts the block on the rack.
    ///
    /// This is the join between the bench and the range. Everything upstream of it made
    /// a cartridge; everything downstream of it is evidence the player reads by looking.
    /// No numbers are surfaced here at all — the notebook keeps the record, and the rack
    /// holds the thing that happened.
    ///
    /// WHY THIS IS ITS OWN FILE, AND WHY THAT IS NOT A TIDINESS DECISION:
    ///
    /// This class used to live at the bottom of <c>EvidenceRack.cs</c>. Unity resolves a
    /// MonoBehaviour's script reference BY FILE NAME, so a behaviour whose class name does
    /// not match its file cannot be serialised at all: the editor writes
    /// <c>m_Script: {fileID: 0}</c> and the component comes back as "the referenced script
    /// on this Behaviour is missing". Everything else about it round-trips — the yard's
    /// Range, MediumId, BlockThickness and its Rack pointer were all sitting correctly in
    /// <c>Workshop Shop.prefab</c> next to a dead script pointer.
    ///
    /// The visible consequence was that firing never worked in the authored shop.
    /// <c>WorkshopController.AdoptStations</c> looks for a RangeStation, cannot find one
    /// because the loaded component is not a RangeStation any more, and answers
    /// "[Shop] No RangeStation anywhere under the shop — firing will refuse." forever.
    ///
    /// That is recorded in the canon as an already-fixed bug — resolve lazily rather than
    /// trusting the builder — and the lazy resolve was necessary but could never have been
    /// sufficient, because there was nothing left in the hierarchy to adopt. A component
    /// that cannot be saved is not a wiring problem, and no amount of adopting fixes it.
    ///
    /// So: ONE MONOBEHAVIOUR PER FILE, NAMED AFTER IT. The failure is silent, survives a
    /// full green test suite, and only shows up once something is saved.
    /// </summary>
    [AddComponentMenu("Gunsmith/Range Station")]
    public sealed class RangeStation : MonoBehaviour
    {
        [Tooltip("Where fired blocks are kept. They are never cleared automatically.")]
        public EvidenceRack Rack;

        [Tooltip("Distance to the block, metres.")]
        public double Range = 10.0;

        [Tooltip("Medium the test block is made of.")]
        public string MediumId = TargetMediumLibrary.Gelatin;

        [Tooltip("Thickness of the block, metres.")]
        public double BlockThickness = 0.80;

        [Tooltip("Fire through four layers of denim, the standard heavy-clothing test.")]
        public bool ThroughClothing;

        /// <summary>The brass from the last shot. Read it, do not print it.</summary>
        public FiredCase LastCase { get; private set; }

        /// <summary>
        /// Finds the rack it racks onto, rather than trusting whoever built it.
        ///
        /// Same reason as everywhere else in this project: a yard placed by hand, restored
        /// from a prefab or duplicated needs to work without being re-wired. Anything
        /// explicitly assigned is left alone.
        /// </summary>
        private EvidenceRack Racking
        {
            get
            {
                if (Rack == null) Rack = GetComponentInChildren<EvidenceRack>(true);
                return Rack;
            }
        }

        /// <summary>
        /// Fires one round of a design into a block and racks the result.
        ///
        /// Consumes a round from the workshop's stock, which is what makes testing cost
        /// something — the player is spending ammunition to buy information.
        /// </summary>
        public bool TryFire(GunsmithGame game, string designId, out NotebookEntry entry, out string why)
        {
            entry = null;
            why = null;

            if (game == null) { why = "No game."; return false; }

            var design = game.Designs.Get(designId);
            if (design == null) { why = "No such load."; return false; }

            var target = ThroughClothing
                ? TargetMediumLibrary.ClothedGelatinBlock(BlockThickness)
                : TargetMediumLibrary.BareGelatinBlock(BlockThickness);

            string targetName = ThroughClothing ? "clothed block" : "bare block";

            if (!game.Range.TryFire(design, Range, target, targetName, game.Day, out entry, out var failure))
            {
                why = failure.ToString();
                return false;
            }

            // The brass is read from what the interior solve actually did, so the case
            // in the player's hand is the pressure gauge for the round they just fired.
            LastCase = FiredCaseReader.Read(entry.Measurement.PeakPressure, design.Baked.Case);

            var rack = Racking;
            if (rack != null)
                rack.Add(entry, design.Design.Projectile, TargetMediumLibrary.Get(MediumId), LastCase, true);

            return true;
        }
    }
}
