using UnityEditor;
using UnityEngine;
using Krofken.Ballistics;
using Krofken.Ballistics.UnityIntegration;

namespace Gunsmith.EditorTools
{
    /// <summary>
    /// Spawns projectile meshes in the open scene so geometry can be eyeballed.
    ///
    /// A dev tool, not part of the game. It exists because the lathe generates every
    /// projectile at runtime from eleven numbers, and the fastest way to check that a
    /// geometry change did what you meant is to look at it.
    ///
    /// Objects are spawned into the OPEN SCENE and are disposable -- do not save the
    /// scene unless you want to keep them. "Clear" removes them again.
    ///
    /// Everything is scaled up on spawn: a real 9 mm projectile is 13 mm long, which
    /// is a speck at scene-view distances. The mesh itself is generated at true size;
    /// only the transform scale is exaggerated.
    /// </summary>
    public static class ProjectilePreview
    {
        private const string RootName = "~ProjectilePreview";
        private const float DisplayScale = 60f;
        private const float Spacing = 1.4f;

        [MenuItem("Ballistics/Spawn Projectile Preview", priority = 10)]
        public static void Spawn()
        {
            Clear();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Spawn Projectile Preview");

            int index = 0;
            Add(root, ref index, "FMJ", Fmj(), new Color(0.72f, 0.55f, 0.28f));
            Add(root, ref index, "Hollow Point", HollowPoint(), new Color(0.80f, 0.45f, 0.22f));
            Add(root, ref index, "Boattail Rifle", Boattail(), new Color(0.70f, 0.60f, 0.35f));
            Add(root, ref index, "Wadcutter", Wadcutter(), new Color(0.55f, 0.55f, 0.58f));
            Add(root, ref index, "Secant VLD", Vld(), new Color(0.62f, 0.66f, 0.70f));

            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();
        }

        [MenuItem("Ballistics/Clear Projectile Preview", priority = 11)]
        public static void Clear()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);
        }

        private static void Add(GameObject root, ref int index, string label,
            ProjectileGeometry geometry, Color colour)
        {
            if (!geometry.Validate(out string error))
            {
                Debug.LogError($"[Preview] {label}: {error}");
                return;
            }

            // Mass properties come from the same geometry, so the pivot really is the
            // centre of gravity -- the point the projectile would spin about.
            var mass = MassPropertiesSolver.Compute(geometry, ProjectileMaterials.JacketedLead);

            var mesh = ProjectileMeshBuilder.Create(geometry, 48, 40, mass.CentreOfGravity);
            mesh.name = label;

            var go = new GameObject($"{label}  ({Units.KilogramsToGrains(mass.Mass):F0} gr)");
            go.transform.SetParent(root.transform);
            go.transform.localPosition = new Vector3(index * Spacing, 0f, 0f);

            // Nose up, so the silhouette reads at a glance.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = Vector3.one * DisplayScale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = label };
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);
            material.SetFloat("_Metallic", 0.9f);
            material.SetFloat("_Smoothness", 0.65f);
            material.SetFloat("_Glossiness", 0.65f);

            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            index++;
        }

        // ---- Five shapes spanning the parameter space -----------------------

        private static ProjectileGeometry Fmj() => ProjectileGeometry.Default9mmFmj;

        private static ProjectileGeometry HollowPoint()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            g.MeplatDiameter = 0.0042;
            g.CavityDepth = 0.0055;
            g.CavityMouthDiameter = 0.0042;
            g.JacketThickness = 0.0003;
            return g;
        }

        private static ProjectileGeometry Boattail() => new ProjectileGeometry
        {
            Calibre = 0.00782,
            NoseLength = 0.00782 * 2.5,
            OgiveShapeParameter = 1.0,
            MeplatDiameter = 0.00782 * 0.08,
            BearingSurfaceLength = 0.00782 * 2.9,
            BoattailLength = 0.00782 * 0.7,
            BoattailAngle = Units.DegreesToRadians(9.0),
            JacketThickness = 0.0006
        };

        /// <summary>Flat-nosed target bullet: nearly all meplat. Cuts clean holes in
        /// paper and is aerodynamically dreadful, which the drag model agrees with.</summary>
        private static ProjectileGeometry Wadcutter() => new ProjectileGeometry
        {
            Calibre = 0.00902,
            NoseLength = 0.0006,
            OgiveShapeParameter = 1.0,
            MeplatDiameter = 0.0080,
            BearingSurfaceLength = 0.0100,
            BoattailLength = 0.0,
            BoattailAngle = 0.0,
            JacketThickness = 0.0,
            BaseCavityDepth = 0.0035,
            BaseCavityDiameter = 0.0055
        };

        /// <summary>Very low drag: long secant ogive, tiny meplat, long boattail.
        /// The shoulder where the arc meets the shank is the secant signature.</summary>
        private static ProjectileGeometry Vld() => new ProjectileGeometry
        {
            Calibre = 0.00782,
            NoseLength = 0.00782 * 3.6,
            OgiveShapeParameter = 0.55,
            MeplatDiameter = 0.00782 * 0.05,
            BearingSurfaceLength = 0.00782 * 2.2,
            BoattailLength = 0.00782 * 0.9,
            BoattailAngle = Units.DegreesToRadians(8.0),
            JacketThickness = 0.0006
        };
    }
}
