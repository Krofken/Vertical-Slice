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
            var root = GameObject.Find(RootName);
            if (root == null) root = new GameObject(RootName);
            if (root.GetComponent<WorkshopBootstrap>() == null) root.AddComponent<WorkshopBootstrap>();

            // ALWAYS reset the transform, including on an object that was already there.
            //
            // The shop is built in this object's local space and the player spawns at a
            // point relative to it, so a stale position puts the whole game somewhere
            // arbitrary. An earlier version of this tool parked itself in front of
            // whatever camera it found and that position was saved into the scene, which
            // is how the gunsmith ended up two hundred metres from his own bench.
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

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