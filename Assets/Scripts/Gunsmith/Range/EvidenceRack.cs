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

        [Header("Fired case")]
        public Material BrassMaterial;
        public Material PrimerMaterial;
        public Material MarkMaterial;

        private readonly List<GelBlockView> _blocks = new List<GelBlockView>();
        private readonly List<FiredCaseView> _cases = new List<FiredCaseView>();

        /// <summary>Fired cases on the rack, one per shot, in shot order.</summary>
        public IReadOnlyList<FiredCaseView> Cases => _cases;

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
            => Add(entry, loaded, medium, default, false);

        /// <summary>
        /// Adds the block AND the brass for a shot.
        ///
        /// The case is the other half of the evidence. The block says what the round did
        /// to the target; the case says what it did to the gun, and it is the only thing
        /// that ever tells the player their load was running hot.
        /// </summary>
        public GelBlockView Add(
            NotebookEntry entry, in ProjectileGeometry loaded, in TargetMedium medium,
            in FiredCase fired, bool includeCase)
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

            if (includeCase)
            {
                // Stands just in front of its own block, so brass and block are read
                // together rather than being two separate lists to cross-reference.
                var brass = new GameObject("Fired case");
                brass.transform.SetParent(go.transform, false);
                brass.transform.localPosition = new Vector3(0f, -0.10f, 0.06f);

                var caseView = brass.AddComponent<FiredCaseView>();
                caseView.BrassMaterial = BrassMaterial;
                caseView.PrimerMaterial = PrimerMaterial;
                caseView.MarkMaterial = MarkMaterial;
                caseView.Show(fired);

                _cases.Add(caseView);
            }

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
}
