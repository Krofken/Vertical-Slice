using UnityEngine;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// Every surface in the shop, as real Material assets you can edit by hand.
    ///
    /// THIS EXISTS SO THE SHOP CAN BE SAVED. The builder used to make its materials
    /// with `new Material(shader)` and tag them `HideFlags.DontSave`, which is fine for
    /// a throwaway preview and fatal for a prefab: a runtime material instance has no
    /// asset behind it, so nothing referencing it can be serialised. Every station was
    /// therefore un-prefabbable by construction.
    ///
    /// It is also the art-direction surface. Swapping the bench from flat brown to a
    /// real wood material is now selecting a slot in an inspector, not editing
    /// <see cref="WorkshopBuilder"/>. That is the whole point of the exercise — you
    /// cannot art-direct a room you have to read source code to change.
    ///
    /// EMPTY SLOTS ARE FINE. Anything left unassigned falls back to the flat colour the
    /// builder used before, generated at runtime. So a half-filled palette works, and
    /// you can replace surfaces one at a time instead of all at once.
    /// </summary>
    [CreateAssetMenu(menuName = "Gunsmith/Workshop Palette", fileName = "WorkshopPalette")]
    public sealed class WorkshopPalette : ScriptableObject
    {
        [Header("Room")]
        public Material Floor;
        public Material Wall;
        public Material BenchTop;
        public Material Fixture;

        [Header("Order board and delivery notes")]
        public Material Card;
        public Material CardAccepted;
        public Material Note;
        public Material NoteDisaster;

        [Header("Evidence rack")]
        [Tooltip("Must be a TRANSPARENT material — the wound cavity is suspended inside it.")]
        public Material GelBlock;
        public Material Cavity;
        public Material DepthBand;
        public Material WitnessCard;
        public Material RecoveredSlug;
        public Material Brass;
        public Material Primer;
        public Material Mark;

        [Header("Bench")]
        public Material Projectile;
        public Material ProjectileInvalid;
        public Material PowderGrain;
        public Material Metal;
        public Material Poise;
        public Material Case;
        public Material SeatingStop;

        [Header("Lathe handles")]
        [Tooltip("One per operation, in LatheOperation order. Short arrays fall back to " +
                 "the built-in colours, so this can be left empty.")]
        public Material[] Handles;

        // ------------------------------------------------------------------
        // Resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the assigned material, or builds the flat fallback colour.
        /// Static so callers with no palette at all take the same path.
        /// </summary>
        public static Material Resolve(Material assigned, Color fallback)
            => assigned != null ? assigned : Flat(fallback);

        /// <summary>Handle material for an operation index, falling back to its colour.</summary>
        public Material ResolveHandle(int index, Color fallback)
            => Handles != null && index >= 0 && index < Handles.Length && Handles[index] != null
                ? Handles[index]
                : Flat(fallback);

        /// <summary>
        /// A flat unlit-looking lit material in the given colour.
        ///
        /// Marked DontSave deliberately: this is the FALLBACK path, used when no asset
        /// was assigned, and a generated material must never end up referenced by
        /// something that gets written to disk. If you are seeing these in a saved
        /// prefab, a palette slot is empty that should not be.
        /// </summary>
        public static Material Flat(Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.color = colour;
            return material;
        }

        /// <summary>Flat, but alpha-blended. The gel block is the only thing that needs it.</summary>
        public static Material Translucent(Color colour)
        {
            var material = Flat(colour);

            // URP's Lit shader switches to transparent through these properties rather
            // than a separate shader, and all of them are required — setting the blend
            // modes without _Surface leaves it opaque.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.color = colour;

            return material;
        }

        // ------------------------------------------------------------------
        // The colours the shop used before there was a palette.
        //
        // Kept here rather than scattered through the builder so the fallback and the
        // generated asset set cannot drift apart -- the editor tool that authors the
        // .mat files reads exactly these.
        // ------------------------------------------------------------------

        public static class Defaults
        {
            public static readonly Color Floor = new Color(0.28f, 0.26f, 0.24f);
            public static readonly Color Wall = new Color(0.34f, 0.31f, 0.28f);
            public static readonly Color BenchTop = new Color(0.36f, 0.27f, 0.19f);
            public static readonly Color Fixture = new Color(0.42f, 0.33f, 0.24f);

            public static readonly Color Card = new Color(0.90f, 0.87f, 0.78f);
            public static readonly Color CardAccepted = new Color(0.74f, 0.80f, 0.70f);
            public static readonly Color Note = new Color(0.92f, 0.90f, 0.82f);
            public static readonly Color NoteDisaster = new Color(0.88f, 0.72f, 0.68f);

            public static readonly Color GelBlock = new Color(0.70f, 0.78f, 0.72f, 0.16f);
            public static readonly Color Cavity = new Color(0.85f, 0.25f, 0.20f);
            public static readonly Color DepthBand = new Color(0.10f, 0.10f, 0.12f);
            public static readonly Color WitnessCard = new Color(0.92f, 0.90f, 0.84f);
            public static readonly Color RecoveredSlug = new Color(0.75f, 0.58f, 0.30f);
            public static readonly Color Brass = new Color(0.80f, 0.66f, 0.30f);
            public static readonly Color Primer = new Color(0.66f, 0.64f, 0.60f);
            public static readonly Color Mark = new Color(0.20f, 0.18f, 0.16f);

            public static readonly Color Projectile = new Color(0.76f, 0.60f, 0.32f);
            public static readonly Color ProjectileInvalid = new Color(0.85f, 0.25f, 0.20f);
            public static readonly Color PowderGrain = new Color(0.24f, 0.22f, 0.20f);
            public static readonly Color Metal = new Color(0.55f, 0.58f, 0.62f);
            public static readonly Color Poise = new Color(0.90f, 0.75f, 0.30f);
            public static readonly Color Case = new Color(0.72f, 0.60f, 0.25f);
            public static readonly Color SeatingStop = new Color(0.85f, 0.35f, 0.35f);

            /// <summary>One per <c>LatheOperation</c>, in enum order.</summary>
            public static readonly Color[] Handles =
            {
                new Color(0.95f, 0.80f, 0.25f), // meplat diameter
                new Color(0.95f, 0.45f, 0.25f), // cavity mouth
                new Color(0.90f, 0.35f, 0.45f), // cavity depth
                new Color(0.35f, 0.75f, 0.95f), // nose length
                new Color(0.45f, 0.90f, 0.60f), // ogive shape
                new Color(0.60f, 0.60f, 0.95f), // bearing surface
                new Color(0.80f, 0.55f, 0.95f), // boattail length
                new Color(0.95f, 0.95f, 0.95f), // boattail angle
                new Color(0.95f, 0.55f, 0.75f)  // jacket thickness
            };
        }
    }
}
