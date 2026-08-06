using Gunsmith.Orders;
using Gunsmith.Range;
using Krofken.Ballistics;
using UnityEditor;
using UnityEngine;

namespace Gunsmith.EditorTools
{
    /// <summary>
    /// Fires a spread of rounds into gel blocks and lines the blocks up side by side.
    ///
    /// A dev tool, not part of the game — but it is deliberately the same arrangement
    /// the game wants: blocks on a rack, comparable at a glance. Walk down the row and
    /// the difference between an over-penetrating jacket and a frangible is obvious
    /// without a single number.
    ///
    /// Objects spawn into the OPEN SCENE and are disposable. Do not save the scene
    /// unless you want to keep them; "Clear" removes them again.
    ///
    /// Unlike the projectile preview, nothing is scaled up — a gel block is 15 cm
    /// square and half a metre long, which is already a sensible size in a scene view.
    /// </summary>
    public static class GelBlockPreview
    {
        private const string RootName = "~GelBlockPreview";
        private const float Spacing = 0.25f;
        private const double ImpactVelocity = 380.0;

        [MenuItem("Ballistics/Spawn Gel Block Preview", priority = 20)]
        public static void Spawn()
        {
            Clear();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Spawn Gel Block Preview");

            var gel = TargetMediumLibrary.Get(TargetMediumLibrary.Gelatin);
            var bare = TargetMediumLibrary.BareGelatinBlock();
            var clothed = TargetMediumLibrary.ClothedGelatinBlock();

            int index = 0;
            Add(root, ref index, "FMJ", Fmj(), bare, gel);
            Add(root, ref index, "Hollow Point", HollowPoint(), bare, gel);
            Add(root, ref index, "Frangible", Frangible(), bare, gel);
            Add(root, ref index, "Armour Piercing", ArmourPiercing(), bare, gel);
            Add(root, ref index, "HP through denim", HollowPoint(), clothed, gel);

            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();
        }

        [MenuItem("Ballistics/Clear Gel Block Preview", priority = 21)]
        public static void Clear()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);
        }

        private static void Add(
            GameObject root, ref int index, string label,
            CartridgeDesign design, TargetLayer[] target, in TargetMedium gel)
        {
            var baked = CartridgeBaker.Bake(design, BarrelLibrary.ServicePistol9mm);
            if (!baked.IsValid)
            {
                Debug.LogError($"[GelBlock] {label}: {string.Join("; ", baked.Issues)}");
                return;
            }

            var terminal = TerminalBallisticsSolver.Solve(baked.Terminal, target, ImpactVelocity);
            var measurement = ShotMeasurement.From(baked, terminal, 10.0, ImpactVelocity, 0.0, 0.03);

            var go = new GameObject(label);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(index * Spacing, 0f, 0f);

            var view = go.AddComponent<GelBlockView>();
            view.BlockMaterial = Translucent(new Color(0.70f, 0.78f, 0.72f, 0.16f));
            view.CavityMaterial = Solid(new Color(0.85f, 0.25f, 0.20f));
            view.BandMaterial = Solid(new Color(0.10f, 0.10f, 0.12f));
            view.CardMaterial = Solid(new Color(0.92f, 0.90f, 0.84f));
            view.ProjectileMaterial = Solid(new Color(0.75f, 0.58f, 0.30f));

            view.Show(measurement, design.Projectile, gel);

            index++;
        }

        // ------------------------------------------------------------------
        // Materials. URP, created on the fly and owned by the preview objects.
        // ------------------------------------------------------------------

        private static Material Solid(Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.color = colour;
            return material;
        }

        private static Material Translucent(Color colour)
        {
            var material = Solid(colour);

            // URP's Lit surface type is a material property plus a keyword plus a
            // render queue; setting only the colour alpha does nothing on its own.
            material.SetFloat("_Surface", 1f);              // 0 opaque, 1 transparent
            material.SetFloat("_Blend", 0f);                // alpha blend
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.color = colour;

            return material;
        }

        // ------------------------------------------------------------------
        // Designs, mirroring the test fixtures.
        // ------------------------------------------------------------------

        private static CartridgeDesign Fmj() => new CartridgeDesign
        {
            Name = "9mm FMJ",
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Projectile = ProjectileGeometry.Default9mmFmj,
            Materials = ProjectileMaterials.JacketedLead,
            PropellantId = PropellantLibrary.SingleBase,
            GrainShape = GrainShape.Sphere,
            WebThickness = 3.5e-5,
            DeterrentCoating = 0.3,
            ChargeMass = Units.GrainsToKilograms(5.5),
            SeatingDepth = 0.0030
        };

        private static CartridgeDesign HollowPoint()
        {
            var d = Fmj();
            d.Name = "9mm JHP";
            d.Projectile.MeplatDiameter = 0.005;
            d.Projectile.CavityDepth = 0.006;
            d.Projectile.CavityMouthDiameter = 0.004;
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.GildingMetal
            };
            return d;
        }

        private static CartridgeDesign Frangible()
        {
            var d = Fmj();
            d.Name = "9mm Frangible";
            d.Projectile.MeplatDiameter = 0.004;
            d.Projectile.CavityDepth = 0.005;
            d.Projectile.CavityMouthDiameter = 0.004;
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.SinteredIron,
                JacketMaterialId = MaterialLibrary.Copper
            };
            return d;
        }

        private static CartridgeDesign ArmourPiercing()
        {
            var d = Fmj();
            d.Name = "9mm AP";
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.HardenedSteel,
                JacketMaterialId = MaterialLibrary.GildingMetal
            };
            return d;
        }
    }
}
