using System.IO;
using Gunsmith.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Defaults = Gunsmith.Interaction.WorkshopPalette.Defaults;

namespace Gunsmith.EditorTools
{
    /// <summary>
    /// Turns the code-built shop into real, hand-editable scene objects.
    ///
    /// THE PROBLEM THIS SOLVES: the workshop only existed while the game was running.
    /// Every object was constructed on Awake and tagged so it could never be saved, so
    /// in the editor there was nothing to select, nothing to move, and nothing to swap a
    /// mesh on. You cannot art-direct a room you have to read source code to change.
    ///
    /// Run these in order. Afterwards the shop is ordinary scene content: move the
    /// bench, delete the placeholder wall, drop a real model in place of a cube. The
    /// bootstrap will use what it finds instead of rebuilding over the top of it.
    ///
    /// WHAT STAYS PROCEDURAL, and should: the projectile mesh, the wound cavity, the
    /// recovered slug, the fired case, the order cards. Those are pictures OF A
    /// SIMULATION RESULT and have to be generated from it. A bench is not.
    /// </summary>
    public static class WorkshopAuthoring
    {
        private const string MaterialFolder = "Assets/Art/Materials/Workshop";
        private const string PaletteFolder = "Assets/Art";
        private const string PalettePath = PaletteFolder + "/WorkshopPalette.asset";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string PrefabPath = PrefabFolder + "/Workshop Shop.prefab";
        private const string RootName = "Workshop";

        // ------------------------------------------------------------------
        // 1. Materials
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes a .mat asset for every surface and a palette that references them.
        ///
        /// Materials must be ASSETS for any of this to work. The builder used to create
        /// them with `new Material(shader)` at runtime, which has no asset behind it, so
        /// anything referencing one could not be serialised — every station was
        /// un-saveable by construction.
        /// </summary>
        [MenuItem("Gunsmith/Author/1. Generate Materials And Palette", priority = 20)]
        public static void GenerateMaterials()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(PaletteFolder);

            var palette = AssetDatabase.LoadAssetAtPath<WorkshopPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<WorkshopPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }

            palette.Floor = Opaque("Floor", Defaults.Floor);
            palette.Wall = Opaque("Wall", Defaults.Wall);
            palette.BenchTop = Opaque("Bench Top", Defaults.BenchTop);
            palette.Fixture = Opaque("Fixture", Defaults.Fixture);

            palette.Card = Opaque("Card", Defaults.Card);
            palette.CardAccepted = Opaque("Card Accepted", Defaults.CardAccepted);
            palette.Note = Opaque("Note", Defaults.Note);
            palette.NoteDisaster = Opaque("Note Disaster", Defaults.NoteDisaster);

            // The one surface that must be alpha-blended: the cavity is a solid
            // suspended inside the block, and you have to be able to see through to it.
            palette.GelBlock = Transparent("Gel Block", Defaults.GelBlock);

            palette.Cavity = Opaque("Cavity", Defaults.Cavity);
            palette.DepthBand = Opaque("Depth Band", Defaults.DepthBand);
            palette.WitnessCard = Opaque("Witness Card", Defaults.WitnessCard);
            palette.RecoveredSlug = Opaque("Recovered Slug", Defaults.RecoveredSlug);
            palette.Brass = Opaque("Brass", Defaults.Brass);
            palette.Primer = Opaque("Primer", Defaults.Primer);
            palette.Mark = Opaque("Mark", Defaults.Mark);

            palette.Projectile = Opaque("Projectile", Defaults.Projectile);
            palette.ProjectileInvalid = Opaque("Projectile Invalid", Defaults.ProjectileInvalid);
            palette.PowderGrain = Opaque("Powder Grain", Defaults.PowderGrain);
            palette.Metal = Opaque("Metal", Defaults.Metal);
            palette.Poise = Opaque("Poise", Defaults.Poise);
            palette.Case = Opaque("Case", Defaults.Case);
            palette.SeatingStop = Opaque("Seating Stop", Defaults.SeatingStop);

            palette.Handles = new Material[Defaults.Handles.Length];
            for (int i = 0; i < Defaults.Handles.Length; i++)
                palette.Handles[i] = Opaque($"Handle {i}", Defaults.Handles[i]);

            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = palette;
            Debug.Log($"[Workshop] Palette written to {PalettePath}. " +
                      "Edit these materials, or replace any slot with your own — the shop reads them.");
        }

        // ------------------------------------------------------------------
        // 2. The shop, as real objects
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the shop into the open scene as ordinary, saveable objects.
        /// </summary>
        [MenuItem("Gunsmith/Author/2. Build Editable Workshop", priority = 21)]
        public static void BuildEditable()
        {
            var palette = LoadPalette();
            if (palette == null)
            {
                Debug.LogError("[Workshop] No palette. Run '1. Generate Materials And Palette' first.");
                return;
            }

            var root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Build Editable Workshop");
            }

            // The shop is built in this object's local space and the player spawns
            // relative to it, so a stale transform moves the entire game.
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            var bootstrap = root.GetComponent<WorkshopBootstrap>();
            if (bootstrap == null) bootstrap = Undo.AddComponent<WorkshopBootstrap>(root);

            // Replace any previous build rather than stacking a second shop on top.
            var existing = root.GetComponentInChildren<WorkshopController>(true);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var shop = WorkshopBuilder.Build(root.transform, palette, persistent: true);

            Undo.RegisterCreatedObjectUndo(shop.gameObject, "Build Editable Workshop");
            Undo.RecordObject(bootstrap, "Build Editable Workshop");
            bootstrap.Shop = shop;
            bootstrap.Palette = palette;
            EditorUtility.SetDirty(bootstrap);

            EditorSceneManager.MarkSceneDirty(root.scene);

            Selection.activeGameObject = shop.gameObject;
            SceneView.FrameLastActiveSceneView();

            Debug.Log("[Workshop] Built as real scene objects. Move them, replace the meshes, " +
                      "delete what you do not want — the bootstrap now uses what it finds " +
                      "instead of rebuilding. SAVE THE SCENE to keep it.");
        }

        // ------------------------------------------------------------------
        // 3. Prefab
        // ------------------------------------------------------------------

        /// <summary>
        /// Saves the built shop as a prefab, and leaves the scene copy as an instance
        /// of it. Optional — the shop is editable without this — but it makes the
        /// layout reusable across scenes and lets edits propagate.
        /// </summary>
        [MenuItem("Gunsmith/Author/3. Save Workshop As Prefab", priority = 22)]
        public static void SaveAsPrefab()
        {
            var root = GameObject.Find(RootName);
            var shop = root != null ? root.GetComponentInChildren<WorkshopController>(true) : null;

            if (shop == null)
            {
                Debug.LogError("[Workshop] Nothing to save. Run '2. Build Editable Workshop' first.");
                return;
            }

            EnsureFolder(PrefabFolder);

            // Strip anything that cannot be written before saving.
            //
            // A DontSave object is by definition not serialisable, so leaving one in the
            // hierarchy makes the saved prefab's layout differ from the scene instance's
            // and Unity warns that "data might be lost". Here it is the propellant
            // mill's two dozen cosmetic grains, which are a picture of the powder and
            // are regenerated whenever the mill runs, so throwing them away costs
            // nothing and the warning goes with them.
            int stripped = StripTransients(shop.transform);
            if (stripped > 0)
                Debug.Log($"[Workshop] Dropped {stripped} transient objects before saving; " +
                          "they are regenerated at runtime.");

            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                shop.gameObject, PrefabPath, InteractionMode.UserAction, out bool success);

            if (!success || prefab == null)
            {
                Debug.LogError($"[Workshop] Could not write {PrefabPath}.");
                return;
            }

            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;

            Debug.Log($"[Workshop] Saved to {PrefabPath}, and the scene copy is now an " +
                      "instance of it. Edit it in prefab mode to change every scene at once.");
        }

        [MenuItem("Gunsmith/Author/3. Save Workshop As Prefab", validate = true)]
        private static bool CanSaveAsPrefab()
        {
            var root = GameObject.Find(RootName);
            return root != null && root.GetComponentInChildren<WorkshopController>(true) != null;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Destroys every descendant flagged <c>DontSave</c>. Returns how many went.
        /// Collected first, because destroying while walking the hierarchy invalidates
        /// the iteration.
        /// </summary>
        private static int StripTransients(Transform root)
        {
            var doomed = new System.Collections.Generic.List<GameObject>();

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) doomed.Add(t.gameObject);
            }

            foreach (var go in doomed)
                if (go != null) Object.DestroyImmediate(go);

            return doomed.Count;
        }

        private static WorkshopPalette LoadPalette()
        {
            var direct = AssetDatabase.LoadAssetAtPath<WorkshopPalette>(PalettePath);
            if (direct != null) return direct;

            // Fall back to any palette in the project, so a renamed or relocated one
            // still works.
            foreach (string guid in AssetDatabase.FindAssets("t:WorkshopPalette"))
            {
                var found = AssetDatabase.LoadAssetAtPath<WorkshopPalette>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (found != null) return found;
            }

            return null;
        }

        private static Material Opaque(string name, Color colour) => Write(name, WorkshopPalette.Flat(colour));

        private static Material Transparent(string name, Color colour)
            => Write(name, WorkshopPalette.Translucent(colour));

        /// <summary>
        /// Writes a generated material to disk, reusing the existing asset if there is
        /// one so hand edits are not thrown away on a re-run.
        /// </summary>
        private static Material Write(string name, Material generated)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                Object.DestroyImmediate(generated);
                return existing;
            }

            // The generated material is tagged DontSave so a fallback can never leak
            // into something serialised. An asset is exactly the opposite case.
            generated.hideFlags = HideFlags.None;
            generated.name = name;

            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        /// <summary>Creates a folder and any missing parents.</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
