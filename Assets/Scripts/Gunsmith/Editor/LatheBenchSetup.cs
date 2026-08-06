using Gunsmith.Crafting;
using Krofken.Ballistics;
using UnityEditor;
using UnityEngine;

namespace Gunsmith.EditorTools
{
    /// <summary>
    /// Builds a working lathe bench in the open scene.
    ///
    /// This is a dev tool in the same family as the preview spawners: it assembles
    /// throwaway objects so the bench can be used before any of the real workshop scene
    /// exists. Clear it before saving.
    ///
    /// The bench itself is not throwaway — <see cref="LatheStation"/> and
    /// <see cref="LatheHandle"/> are game components. Only this arrangement of them is.
    /// </summary>
    public static class LatheBenchSetup
    {
        private const string RootName = "~LatheBench";

        /// <summary>A 9 mm projectile is 13 mm long. At true size on screen it is a
        /// speck, so the whole rig is scaled up. The MESH is still generated at true
        /// size — only the transform is exaggerated, so nothing the solvers read is
        /// touched.</summary>
        private const float DisplayScale = 40f;

        [MenuItem("Gunsmith/Open Lathe Bench", priority = 0)]
        public static void Spawn()
        {
            Clear();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Open Lathe Bench");

            var rig = new GameObject("Rig").transform;
            rig.SetParent(root.transform, false);
            rig.localScale = Vector3.one * DisplayScale;

            var station = root.AddComponent<LatheStation>();
            station.Rig = rig;
            station.Geometry = ProjectileGeometry.Default9mmFmj;
            station.ValidMaterial = Solid(new Color(0.76f, 0.60f, 0.32f));
            station.InvalidMaterial = Solid(new Color(0.85f, 0.25f, 0.20f));

            // ---- the work ------------------------------------------------
            var bullet = new GameObject("Projectile");
            bullet.transform.SetParent(rig, false);
            station.BulletMesh = bullet.AddComponent<MeshFilter>();
            station.BulletRenderer = bullet.AddComponent<MeshRenderer>();

            // ---- the handles ---------------------------------------------
            station.Handles = new Transform[8];

            AddHandle(station, rig, LatheOperation.MeplatDiameter, "Meplat", new Color(0.95f, 0.80f, 0.25f));
            AddHandle(station, rig, LatheOperation.CavityMouth, "Cavity mouth", new Color(0.95f, 0.45f, 0.25f));
            AddHandle(station, rig, LatheOperation.CavityDepth, "Cavity depth", new Color(0.90f, 0.35f, 0.45f));
            AddHandle(station, rig, LatheOperation.NoseLength, "Nose length", new Color(0.35f, 0.75f, 0.95f));
            AddHandle(station, rig, LatheOperation.OgiveShape, "Ogive shape", new Color(0.45f, 0.90f, 0.60f));
            AddHandle(station, rig, LatheOperation.BearingSurface, "Bearing surface", new Color(0.60f, 0.60f, 0.95f));
            AddHandle(station, rig, LatheOperation.BoattailLength, "Boattail length", new Color(0.80f, 0.55f, 0.95f));
            AddHandle(station, rig, LatheOperation.BoattailAngle, "Boattail angle", new Color(0.95f, 0.95f, 0.95f));

            // ---- the scale -----------------------------------------------
            // The one number the bench is allowed to show: what the finished bullet
            // weighs. It measures material used, never performance.
            station.ScaleReadout = AddLabel(root.transform, "Scale",
                new Vector3(0f, -0.34f, 0f), 0.012f, new Color(0.95f, 0.95f, 0.90f), TextAnchor.UpperCenter);

            station.Complaint = AddLabel(root.transform, "Complaint",
                new Vector3(0f, -0.46f, 0f), 0.007f, new Color(0.95f, 0.35f, 0.30f), TextAnchor.UpperCenter);

            // Put the bench in front of the camera so it is usable immediately.
            var camera = Camera.main;
            if (camera != null)
            {
                root.transform.position = camera.transform.position + camera.transform.forward * 1.4f;

                // Match the camera's orientation rather than turning to face it. A
                // TextMesh is legible only when read along its own +Z, so spinning the
                // root round to look at the camera renders the scale mirror-written.
                root.transform.rotation = camera.transform.rotation;

                foreach (var handle in root.GetComponentsInChildren<LatheHandle>())
                    handle.Rig = camera;
            }

            // Only the work turns broadside, so the profile is across the view instead
            // of pointing away down the camera axis. The labels stay square to the
            // viewer because they are not under the rig.
            rig.localRotation = Quaternion.Euler(0f, -90f, 0f);

            station.Rebuild();

            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();

            Debug.Log("[Lathe] Bench open. Drag the coloured handles — in play mode with the " +
                      "mouse, or in edit mode with the move gizmo. The scale reads the finished mass.");
        }

        [MenuItem("Gunsmith/Clear Lathe Bench", priority = 1)]
        public static void Clear()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);
        }

        private static void AddHandle(
            LatheStation station, Transform rig, LatheOperation operation, string label, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = label;
            go.transform.SetParent(rig, false);

            // In rig-local metres, so about 1.4 mm across on a 9 mm bullet — small
            // enough to sit on the profile without hiding it.
            go.transform.localScale = Vector3.one * 0.0014f;

            go.GetComponent<MeshRenderer>().sharedMaterial = Solid(colour);

            var handle = go.AddComponent<LatheHandle>();
            handle.Operation = operation;
            handle.Station = station;

            station.Handles[(int)operation] = go.transform;
        }

        private static TextMesh AddLabel(
            Transform parent, string name, Vector3 localPosition, float size, Color colour, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var text = go.AddComponent<TextMesh>();
            text.characterSize = size;
            text.fontSize = 96;
            text.color = colour;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;

            return text;
        }

        private static Material Solid(Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.color = colour;
            return material;
        }
    }
}
