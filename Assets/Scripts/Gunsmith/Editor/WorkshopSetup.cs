using Gunsmith.Interaction;
using UnityEditor;
using UnityEngine;

namespace Gunsmith.EditorTools
{
    /// <summary>
    /// Puts the shop in the scene so it can be played.
    ///
    /// This used to build the whole workshop itself and tag every object DontSave, which
    /// meant the shop was never in the scene at all - pressing Play reloaded the scene
    /// and the game was an empty room. Construction now lives in the runtime
    /// <see cref="WorkshopBuilder"/>, and this tool only drops in the one small component
    /// that starts it.
    ///
    /// That bootstrap IS saved with the scene, deliberately: it is the single object the
    /// game needs in order to exist. Everything it builds is still created at runtime and
    /// still marked DontSave, so nothing else is ever serialised.
    /// </summary>
    public static class WorkshopSetup
    {
        private const string RootName = "Workshop";

        [MenuItem("Gunsmith/Add Workshop To Scene", priority = -10)]
        public static void Add()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                Debug.Log("[Workshop] Already in the scene. Press Play.");
                return;
            }

            var root = new GameObject(RootName);
            root.AddComponent<WorkshopBootstrap>();

            Undo.RegisterCreatedObjectUndo(root, "Add Workshop");
            Selection.activeGameObject = root;

            Debug.Log("[Workshop] Added. PRESS PLAY - the shop builds itself on start. " +
                      "WASD to walk, mouse to look, Escape frees the cursor for the bench handles.");
        }

        /// <summary>Builds it in the editor too, for looking at without entering play
        /// mode. Everything it makes is DontSave, so this cannot dirty the scene.</summary>
        [MenuItem("Gunsmith/Preview Workshop (edit mode)", priority = -9)]
        public static void Preview()
        {
            ClearPreview();

            var root = new GameObject("~WorkshopPreview") { hideFlags = HideFlags.DontSave };
            WorkshopBuilder.Build(root.transform);

            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();
        }

        [MenuItem("Gunsmith/Clear Workshop Preview", priority = -8)]
        public static void ClearPreview()
        {
            var existing = GameObject.Find("~WorkshopPreview");
            if (existing != null) Object.DestroyImmediate(existing);
        }
    }
}