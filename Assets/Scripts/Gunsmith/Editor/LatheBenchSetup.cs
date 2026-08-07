using Gunsmith.Crafting;
using Krofken.Ballistics;
using Krofken.Ballistics.UnityIntegration;
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
            station.Handles = new Transform[LatheStation.OperationCount];

            AddHandle(station, rig, LatheOperation.MeplatDiameter, "Meplat", new Color(0.95f, 0.80f, 0.25f));
            AddHandle(station, rig, LatheOperation.CavityMouth, "Cavity mouth", new Color(0.95f, 0.45f, 0.25f));
            AddHandle(station, rig, LatheOperation.CavityDepth, "Cavity depth", new Color(0.90f, 0.35f, 0.45f));
            AddHandle(station, rig, LatheOperation.NoseLength, "Nose length", new Color(0.35f, 0.75f, 0.95f));
            AddHandle(station, rig, LatheOperation.OgiveShape, "Ogive shape", new Color(0.45f, 0.90f, 0.60f));
            AddHandle(station, rig, LatheOperation.BearingSurface, "Bearing surface", new Color(0.60f, 0.60f, 0.95f));
            AddHandle(station, rig, LatheOperation.BoattailLength, "Boattail length", new Color(0.80f, 0.55f, 0.95f));
            AddHandle(station, rig, LatheOperation.BoattailAngle, "Boattail angle", new Color(0.95f, 0.95f, 0.95f));
            AddHandle(station, rig, LatheOperation.JacketThickness, "Jacket thickness", new Color(0.95f, 0.55f, 0.75f));

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

            BuildPropellantMill(root.transform);
            BuildBalance(root.transform, station);
            BuildSeatingDie(root.transform, station);

            station.Rebuild();

            MakeDisposable(root);

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

        /// <summary>
        /// The propellant mill, bottom left. Shows a magnified sample of the grains it
        /// presses, because grain form and grain size ARE the readout — a coarse powder
        /// has to visibly be coarse.
        /// </summary>
        private static void BuildPropellantMill(Transform parent)
        {
            var root = new GameObject("Propellant Mill");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(-1.20f, -0.42f, 0f);

            var mill = root.AddComponent<PropellantMill>();
            mill.GrainMaterial = Solid(new Color(0.24f, 0.22f, 0.20f));

            // Propellant grains run from 25 to 500 micrometres. Even at the bench's 40x
            // they would be specks, so the tray is a magnifying glass over the pan
            // rather than part of the same scale as the bullet.
            var tray = new GameObject("Grain Tray").transform;
            tray.SetParent(root.transform, false);
            tray.localScale = Vector3.one * 900f;
            mill.GrainTray = tray;

            var pan = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pan.name = "Pan";
            pan.transform.SetParent(root.transform, false);
            pan.transform.localPosition = new Vector3(0f, -0.012f, 0f);
            pan.transform.localScale = new Vector3(0.30f, 0.008f, 0.30f);
            pan.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.50f, 0.52f, 0.56f));

            mill.Readout = AddLabel(root.transform, "Mill readout",
                new Vector3(0f, -0.10f, 0f), 0.008f, new Color(0.95f, 0.92f, 0.80f), TextAnchor.UpperCenter);

            // The calibrated baseline powder, so the bench opens on a working load.
            mill.SetShape(GrainShape.Sphere);
            mill.SetWeb(3.5e-5);
            mill.SetDeterrent(0.3);
        }

        /// <summary>
        /// The powder scale, to the left of the lathe. The poise slides on the beam and
        /// the beam tips as powder goes in the pan; the engraving under the poise is the
        /// setting, not the contents.
        /// </summary>
        private static void BuildBalance(Transform parent, LatheStation station)
        {
            var root = new GameObject("Powder Balance");
            root.transform.SetParent(parent, false);
            // Far enough left that the beam cannot be mistaken for part of the lathe.
            root.transform.localPosition = new Vector3(-1.15f, 0.30f, 0f);

            var balance = root.AddComponent<PowderBalance>();
            balance.BeamTravel = 0.30;
            balance.MaxSettingGrains = 12.0;

            // The beam pivots on a knife edge. Everything hangs off this transform, so
            // rotating it tips the whole assembly the way a real beam does.
            var beam = new GameObject("Beam").transform;
            beam.SetParent(root.transform, false);
            balance.Beam = beam;

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            bar.transform.SetParent(beam, false);
            bar.transform.localPosition = new Vector3(0.10f, 0f, 0f);
            bar.transform.localScale = new Vector3(0.44f, 0.012f, 0.012f);
            bar.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.62f, 0.64f, 0.68f));

            var poise = GameObject.CreatePrimitive(PrimitiveType.Cube);
            poise.name = "Poise";
            poise.transform.SetParent(beam, false);
            poise.transform.localScale = new Vector3(0.022f, 0.05f, 0.05f);
            poise.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.90f, 0.75f, 0.30f));
            balance.Poise = poise.transform;

            var pan = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pan.name = "Pan";
            pan.transform.SetParent(beam, false);
            pan.transform.localPosition = new Vector3(-0.13f, -0.05f, 0f);
            pan.transform.localScale = new Vector3(0.10f, 0.012f, 0.10f);
            pan.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.55f, 0.58f, 0.62f));
            balance.Pan = pan.transform;

            balance.BeamReadout = AddLabel(root.transform, "Beam readout",
                new Vector3(0.10f, -0.16f, 0f), 0.010f, new Color(0.95f, 0.92f, 0.80f), TextAnchor.UpperCenter);

            // A charge that suits the default load, already trickled, so the beam is
            // level when the bench opens rather than sitting on its stop.
            balance.SettingGrains = 5.5;
            balance.Trickle(5.5);
        }

        /// <summary>
        /// The seating die, to the right. The stop sets how deep the bullet goes; the
        /// readout is depth and cartridge overall length, both of them measurements.
        /// </summary>
        private static void BuildSeatingDie(Transform parent, LatheStation station)
        {
            var root = new GameObject("Seating Die");
            root.transform.SetParent(parent, false);
            // The die builds a whole cartridge, roughly 30 mm at 40x, and its bullet
            // stands on the side facing the lathe. Set well clear or the two read as a
            // single object.
            root.transform.localPosition = new Vector3(1.25f, -0.30f, 0f);

            var die = root.AddComponent<SeatingStop>();
            die.Projectile = station.Geometry;

            var rig = new GameObject("Rig").transform;
            rig.SetParent(root.transform, false);
            rig.localScale = Vector3.one * DisplayScale;
            rig.localRotation = Quaternion.Euler(0f, -90f, 0f);

            var casing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            casing.name = "Case";
            casing.transform.SetParent(rig, false);
            // Unity's cylinder is 2 units tall and stands on Y, so half the length and
            // a quarter turn puts it along the die's axis.
            casing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            casing.transform.localPosition = new Vector3(0f, 0f, -(float)(die.CaseLength * 0.5));
            casing.transform.localScale = new Vector3(0.0095f, (float)(die.CaseLength * 0.5), 0.0095f);
            casing.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.72f, 0.60f, 0.25f));

            // The real projectile, lathed from the same geometry the lathe is turning.
            var bullet = new GameObject("Bullet");
            bullet.transform.SetParent(rig, false);
            bullet.AddComponent<MeshFilter>().sharedMesh =
                ProjectileMeshBuilder.Create(die.Projectile, 24, 24, 0.0);
            bullet.AddComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.78f, 0.62f, 0.34f));
            die.SeatedBullet = bullet.transform;

            var stop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stop.name = "Stop";
            stop.transform.SetParent(rig, false);
            stop.transform.localScale = new Vector3(0.011f, 0.011f, 0.0012f);
            stop.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.85f, 0.35f, 0.35f));
            die.Stop = stop.transform;

            die.DepthReadout = AddLabel(root.transform, "Die readout",
                new Vector3(0f, -0.16f, 0f), 0.008f, new Color(0.95f, 0.92f, 0.80f), TextAnchor.UpperCenter);

            die.Depth = 0.0030;
        }

        /// <summary>
        /// Marks a whole preview hierarchy as never-saved.
        ///
        /// WHY THIS MATTERS MORE THAN IT LOOKS: without it, spawning a preview dirties
        /// the open scene, and then every domain reload — every script edit, every entry
        /// into play mode, every test run — stops and asks "save your changes?". That
        /// modal blocks the editor until a human clicks it, which makes the bench
        /// unusable while anyone is doing anything else.
        ///
        /// HideFlags.DontSave takes the objects out of serialisation entirely, so the
        /// scene never becomes dirty, nothing prompts, and a preview can never be
        /// committed by accident. The trade is that they vanish on a domain reload —
        /// which is correct: they are disposable, and the menu item rebuilds them.
        /// </summary>
        private static void MakeDisposable(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                transform.gameObject.hideFlags = HideFlags.DontSave;
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
