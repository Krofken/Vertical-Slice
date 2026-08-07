using System.Collections.Generic;
using Gunsmith.GameLoop;
using Gunsmith.Orders;
using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Range
{
    /// <summary>
    /// The rack: every block the player has ever shot, lined up in the order they shot
    /// them.
    ///
    /// THE BLOCKS DO NOT DISAPPEAR, and that is the point. Block 7 stands next to block
    /// 4 with its recovered slug beside it, and the difference between them is visible
    /// by walking down the row. This is what turns "I need to remember what happened"
    /// into "I can look", and it is why the notebook is an index to physical evidence
    /// rather than a table of numbers.
    ///
    /// It is also what makes duplicate-and-tweak pay off: change one thing, fire it, and
    /// the two blocks stand side by side differing in exactly one variable.
    /// </summary>
    [AddComponentMenu("Gunsmith/Evidence Rack")]
    public sealed class EvidenceRack : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Spacing between blocks along the rack, metres.")]
        public float Spacing = 0.25f;

        [Tooltip("Blocks per row before the rack wraps to a second shelf.")]
        [Min(1)] public int BlocksPerShelf = 8;

        [Tooltip("Drop between shelves, metres.")]
        public float ShelfDrop = 0.45f;

        [Header("Block appearance")]
        public Material BlockMaterial;
        public Material CavityMaterial;
        public Material BandMaterial;
        public Material CardMaterial;
        public Material ProjectileMaterial;

        private readonly List<GelBlockView> _blocks = new List<GelBlockView>();

        /// <summary>Blocks currently on the rack.</summary>
        public IReadOnlyList<GelBlockView> Blocks => _blocks;

        /// <summary>
        /// Adds the block for a shot that has already been fired.
        /// </summary>
        /// <param name="entry">The notebook entry for the shot. The entry is the record;
        /// this is the physical evidence it indexes.</param>
        /// <param name="loaded">Geometry as loaded, for the recovered slug.</param>
        /// <param name="medium">Medium the block is made of.</param>
        public GelBlockView Add(NotebookEntry entry, in ProjectileGeometry loaded, in TargetMedium medium)
        {
            if (entry == null) return null;

            var go = new GameObject($"Shot {entry.ShotNumber} — {entry.DesignName}");
            go.transform.SetParent(transform, false);

            int index = _blocks.Count;
            int shelf = index / BlocksPerShelf;
            int slot = index % BlocksPerShelf;

            go.transform.localPosition = new Vector3(slot * Spacing, -shelf * ShelfDrop, 0f);

            var view = go.AddComponent<GelBlockView>();
            view.BlockMaterial = BlockMaterial;
            view.CavityMaterial = CavityMaterial;
            view.BandMaterial = BandMaterial;
            view.CardMaterial = CardMaterial;
            view.ProjectileMaterial = ProjectileMaterial;

            view.Show(entry.Measurement, loaded, medium);

            _blocks.Add(view);
            return view;
        }

        /// <summary>Clears the rack. Deliberately NOT called when a shot is fired — the
        /// whole design depends on evidence persisting.</summary>
        public void Clear()
        {
            foreach (var block in _blocks)
            {
                if (block == null) continue;
                if (Application.isPlaying) Destroy(block.gameObject); else DestroyImmediate(block.gameObject);
            }

            _blocks.Clear();
        }
    }

    /// <summary>
    /// The yard: fires a load and puts the block on the rack.
    ///
    /// This is the join between the bench and the range. Everything upstream of it made
    /// a cartridge; everything downstream of it is evidence the player reads by looking.
    /// No numbers are surfaced here at all — the notebook keeps the record, and the rack
    /// holds the thing that happened.
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

            if (Rack != null)
                Rack.Add(entry, design.Design.Projectile, TargetMediumLibrary.Get(MediumId));

            return true;
        }
    }
}
