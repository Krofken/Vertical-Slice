using System.Collections.Generic;
using Gunsmith.Orders;
using Krofken.Ballistics;
using Krofken.Ballistics.UnityIntegration;
using UnityEngine;

namespace Gunsmith.Range
{
    /// <summary>
    /// The physical readout of a shot: a gelatin block holding the cavity the round
    /// cut, graduated so depth can be read off it, with the witness card it punched
    /// and the slug recovered from it.
    ///
    /// THIS IS THE RANGE'S ENTIRE OUTPUT. No numbers are displayed anywhere in here,
    /// on purpose — every figure the sim computed is expressed as something the player
    /// looks at instead:
    ///
    ///     energy profile      the shape of the cavity
    ///     penetration depth   which graduated band it stopped in
    ///     perforation         a hole out the back face
    ///     expansion ratio     the width of the recovered slug
    ///     fragmentation       a tray of pieces instead of a slug
    ///     stability factor    a round hole or an oval slot in the card
    ///
    /// Blocks are meant to PERSIST. Build one per shot and leave it on the rack; the
    /// player learns by walking down the row and comparing, not by remembering.
    ///
    /// ORIENTATION: the entry face sits at local z = 0 and depth runs along local -Z.
    /// Point the object's -Z downrange. The witness card stands just in front, at +Z.
    /// </summary>
    public sealed class GelBlockView : MonoBehaviour
    {
        [Header("Materials")]
        [Tooltip("Transparent gel. Needs a see-through material or the cavity is invisible.")]
        public Material BlockMaterial;
        public Material CavityMaterial;
        public Material BandMaterial;
        public Material CardMaterial;
        public Material ProjectileMaterial;

        [Header("Block")]
        [Tooltip("Cross-section of the block, metres. Standard ordnance blocks are 15 cm square.")]
        public float BlockWidth = 0.15f;
        public float BlockHeight = 0.15f;

        [Tooltip("Spacing of the depth graduations, metres.")]
        public float BandSpacing = 0.05f;

        [Tooltip("How far in front of the entry face the witness card stands, metres.")]
        public float CardStandoff = 0.02f;

        [Header("Mesh detail")]
        public int RadialSegments = 24;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Depth the round reached, metres. Read by the rack for labelling.</summary>
        public double PenetrationDepth { get; private set; }

        /// <summary>
        /// Rebuilds the block for one shot.
        /// </summary>
        /// <param name="measurement">What the instruments read.</param>
        /// <param name="loaded">Geometry as it was loaded, for the recovered slug.</param>
        /// <param name="medium">Medium the block is made of — supplies the cavity
        /// expansion pressure the silhouette is derived from.</param>
        public void Show(
            in ShotMeasurement measurement,
            in ProjectileGeometry loaded,
            in TargetMedium medium)
        {
            Clear();

            PenetrationDepth = measurement.PenetrationDepth;

            double blockLength = Mathf.Max((float)measurement.PenetrationDepth * 1.1f, 0.05f);
            if (measurement.Perforated) blockLength = measurement.PenetrationDepth;

            BuildBlock(blockLength);
            BuildBands(blockLength);
            BuildCavity(measurement, loaded, medium);
            BuildWitnessCard(measurement, loaded);
            BuildRecovered(measurement, loaded);
        }

        /// <summary>Destroys everything this view generated. Safe to call repeatedly.</summary>
        public void Clear()
        {
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        private void OnDestroy() => Clear();

        // ------------------------------------------------------------------
        // Pieces
        // ------------------------------------------------------------------

        private void BuildCavity(
            in ShotMeasurement measurement, in ProjectileGeometry loaded, in TargetMedium medium)
        {
            // The channel cannot be narrower than the thing that cut it.
            double bulletRadius = Mathf.Max(
                (float)measurement.ExpandedDiameter, (float)loaded.Calibre) * 0.5;

            var buffer = new ProfilePoint[measurement.EnergyProfileBinCount + 2];
            int count = WoundCavity.Build(
                measurement.EnergyProfile,
                measurement.EnergyProfileBinCount,
                measurement.EnergyProfileBinWidth,
                measurement.PenetrationDepth,
                measurement.Perforated,
                medium.StrengthTerm,
                buffer,
                bulletRadius);

            if (count < 2) return;

            // Rendered as a SOLID suspended in the transparent block rather than as a
            // hole cut through it. Same silhouette, no CSG, and it reads better: the
            // shape is a positive object the eye can follow.
            var mesh = ProjectileMeshBuilder.CreateFromProfile(
                buffer, count, "Cavity", RadialSegments, pivotFromStart: 0.0,
                inward: false, capEnds: true);

            Spawn("Cavity", mesh, CavityMaterial, Vector3.zero);
        }

        private void BuildRecovered(in ShotMeasurement measurement, in ProjectileGeometry loaded)
        {
            // Nothing single survives a frangible. The player gets a tray of pieces,
            // which is its own unmistakable readout.
            if (measurement.Fragmented)
            {
                BuildFragmentTray(measurement, loaded);
                return;
            }

            var result = new TerminalResult
            {
                MaxExpandedDiameter = measurement.ExpandedDiameter,
                ExpansionRatio = measurement.ExpansionRatio,
                Fragmented = false
            };

            var buffer = new ProfilePoint[RecoveredProjectile.RequiredCapacity()];
            int count = RecoveredProjectile.Build(loaded, result, buffer);
            if (count < 2) return;

            var mesh = ProjectileMeshBuilder.CreateFromProfile(
                buffer, count, "Recovered", RadialSegments, pivotFromStart: 0.0,
                inward: false, capEnds: true);

            // Sits on the bench beside the block, not inside it — it has been dug out.
            Spawn("Recovered Slug", mesh, ProjectileMaterial,
                new Vector3(BlockWidth * 0.5f + 0.04f, -BlockHeight * 0.5f, 0f));
        }

        private void BuildFragmentTray(in ShotMeasurement measurement, in ProjectileGeometry loaded)
        {
            var tray = new GameObject("Recovered Fragments");
            tray.transform.SetParent(transform, false);
            tray.transform.localPosition =
                new Vector3(BlockWidth * 0.5f + 0.04f, -BlockHeight * 0.5f, 0f);
            _spawned.Add(tray);

            int pieces = Mathf.Clamp(measurement.FragmentCount, 2, 40);

            // Total volume is conserved, so each piece is sized from the loaded
            // projectile's volume divided among them — a round that came apart into
            // more pieces visibly came apart into SMALLER pieces.
            double volume = MassPropertiesSolver.Compute(loaded, 1.0, 1.0, 0.0).Volume;
            float radius = Mathf.Pow((float)(volume / pieces) * 3f / (4f * Mathf.PI), 1f / 3f);

            for (int i = 0; i < pieces; i++)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                piece.name = $"Fragment {i + 1}";
                piece.transform.SetParent(tray.transform, false);

                // Laid out on a deterministic spiral. No random placement anywhere in
                // this game — the same shot must always look the same.
                float angle = i * 2.399963f;                 // golden angle, radians
                float r = radius * 3f * Mathf.Sqrt(i + 1);
                piece.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                piece.transform.localScale = Vector3.one * radius * 2f;

                if (ProjectileMaterial != null)
                    piece.GetComponent<MeshRenderer>().sharedMaterial = ProjectileMaterial;
            }
        }

        /// <summary>
        /// The witness card: paper in front of the block that records HOW the bullet
        /// was flying when it arrived.
        ///
        /// A gyroscopically stable round punches a clean round hole of its own calibre.
        /// One that has fallen below stability is tumbling, and hits sideways — the
        /// hole stretches into a slot as long as the bullet. Keyholing, and it is
        /// instantly legible with nothing to explain.
        ///
        /// The stability-to-shape mapping below is PRESENTATION, not physics: the
        /// physics is the stability factor itself, computed upstream. Sg is
        /// conventionally marginal at 1.0 and comfortable by about 1.4.
        /// </summary>
        private void BuildWitnessCard(in ShotMeasurement measurement, in ProjectileGeometry loaded)
        {
            float calibre = (float)loaded.Calibre;
            float length = (float)loaded.OverallLength;

            float sg = (float)measurement.StabilityFactor;
            float tumble = Mathf.Clamp01(Mathf.InverseLerp(1.4f, 1.0f, sg));

            float major = Mathf.Lerp(calibre, length, tumble) * 0.5f;
            float minor = calibre * 0.5f;

            var mesh = BuildCardMesh(BlockWidth * 0.6f, BlockHeight * 0.6f, major, minor, 32);
            Spawn("Witness Card", mesh, CardMaterial, new Vector3(0f, 0f, CardStandoff));
        }

        private void BuildBlock(double length)
        {
            var mesh = BuildBox(BlockWidth, BlockHeight, (float)length);
            Spawn("Block", mesh, BlockMaterial, Vector3.zero);
        }

        /// <summary>
        /// Depth graduations down both side faces, so the player reads "it stopped in
        /// the fourth band" instead of "it stopped at 21.4 cm". Every fifth band is
        /// doubled in width as a coarse ruler.
        /// </summary>
        private void BuildBands(double length)
        {
            if (BandSpacing <= 0f) return;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            float halfWidth = BlockWidth * 0.5f;
            float halfHeight = BlockHeight * 0.5f;
            int bands = Mathf.FloorToInt((float)length / BandSpacing);

            for (int i = 1; i <= bands; i++)
            {
                float z = -i * BandSpacing;
                float thickness = (i % 5 == 0) ? 0.003f : 0.0015f;

                // One strip on each side face, offset a hair outside the surface so it
                // does not fight the block for depth.
                AddBandQuad(vertices, normals, uvs, triangles,
                    halfWidth + 0.0005f, halfHeight, z, thickness, facingRight: true);
                AddBandQuad(vertices, normals, uvs, triangles,
                    -halfWidth - 0.0005f, halfHeight, z, thickness, facingRight: false);
            }

            if (triangles.Count == 0) return;

            var mesh = new Mesh { name = "Depth Bands" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();

            Spawn("Depth Bands", mesh, BandMaterial, Vector3.zero);
        }

        // ------------------------------------------------------------------
        // Mesh helpers
        // ------------------------------------------------------------------

        private static void AddBandQuad(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            float x, float halfHeight, float z, float thickness, bool facingRight)
        {
            int start = vertices.Count;
            var normal = new Vector3(facingRight ? 1f : -1f, 0f, 0f);

            vertices.Add(new Vector3(x, -halfHeight, z - thickness));
            vertices.Add(new Vector3(x, halfHeight, z - thickness));
            vertices.Add(new Vector3(x, halfHeight, z + thickness));
            vertices.Add(new Vector3(x, -halfHeight, z + thickness));

            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));

            if (facingRight)
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            }
        }

        /// <summary>
        /// A rectangle with an elliptical hole, facing +Z.
        ///
        /// Built by walking one ring of angles and, for each, pairing the point on the
        /// ellipse with the point where that same ray leaves the rectangle. Consecutive
        /// pairs stitch into quads, which fills the card without touching the hole.
        /// </summary>
        private static Mesh BuildCardMesh(
            float halfWidth, float halfHeight, float major, float minor, int segments)
        {
            if (segments < 3) segments = 3;

            // Keep the hole inside the card, or the triangulation inverts.
            major = Mathf.Min(major, halfWidth * 0.95f);
            minor = Mathf.Min(minor, halfHeight * 0.95f);

            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[segments * 2];
            var uvs = new Vector2[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);

                // Where this ray exits the rectangle: whichever edge it reaches first.
                float tx = Mathf.Abs(cos) > 1e-6f ? halfWidth / Mathf.Abs(cos) : float.MaxValue;
                float ty = Mathf.Abs(sin) > 1e-6f ? halfHeight / Mathf.Abs(sin) : float.MaxValue;
                float t = Mathf.Min(tx, ty);

                vertices[i] = new Vector3(major * cos, minor * sin, 0f);              // hole rim
                vertices[segments + i] = new Vector3(t * cos, t * sin, 0f);            // card edge

                normals[i] = Vector3.forward;
                normals[segments + i] = Vector3.forward;

                uvs[i] = new Vector2(0.5f + 0.5f * cos, 0.5f + 0.5f * sin);
                uvs[segments + i] = new Vector2(cos > 0 ? 1f : 0f, sin > 0 ? 1f : 0f);
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                int innerA = i, innerB = next;
                int outerA = segments + i, outerB = segments + next;

                // Cross(outward, counter-clockwise tangent) points along +Z, so this
                // winding is front-facing to a viewer standing downrange of the card.
                int t = i * 6;
                triangles[t] = innerA; triangles[t + 1] = outerA; triangles[t + 2] = innerB;
                triangles[t + 3] = innerB; triangles[t + 4] = outerA; triangles[t + 5] = outerB;
            }

            var mesh = new Mesh { name = "Witness Card" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Box spanning the cross-section and running from z = 0 to z = -length.</summary>
        private static Mesh BuildBox(float width, float height, float length)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            const float front = 0f;
            float back = -length;

            var corners = new[]
            {
                new Vector3(-hw, -hh, front), new Vector3(hw, -hh, front),
                new Vector3(hw,  hh, front), new Vector3(-hw,  hh, front),
                new Vector3(-hw, -hh, back),  new Vector3(hw, -hh, back),
                new Vector3(hw,  hh, back),   new Vector3(-hw,  hh, back)
            };

            // Faces as quads of corner indices, each wound counter-clockwise seen from
            // outside, plus that face's outward normal.
            int[][] faces =
            {
                new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 },
                new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 },
                new[] { 1, 2, 6, 5 }, new[] { 0, 4, 7, 3 }
            };

            var faceNormals = new[]
            {
                Vector3.forward, Vector3.back, Vector3.down,
                Vector3.up, Vector3.right, Vector3.left
            };

            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var triangles = new List<int>(36);

            for (int f = 0; f < faces.Length; f++)
            {
                int start = vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    vertices.Add(corners[faces[f][i]]);
                    normals.Add(faceNormals[f]);
                }

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));

                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            var mesh = new Mesh { name = "Gel Block" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Spawn(string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;

            _spawned.Add(go);
        }
    }
}
