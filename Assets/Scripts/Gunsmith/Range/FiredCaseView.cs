using Krofken.Ballistics;
using Krofken.Ballistics.UnityIntegration;
using UnityEngine;

namespace Gunsmith.Range
{
    /// <summary>
    /// The fired case, as an object on the bench.
    ///
    /// This is the range's pressure gauge and it has no numbers on it. Everything the
    /// player learns about how hard the load ran, they learn by looking at the brass:
    /// a primer that has gone from domed to flat, a raised pip where brass extruded into
    /// the ejector hole, a split down the neck, or a case that simply came apart.
    ///
    /// The case is lathed from a profile like everything else in this project, so a
    /// bulged or split case is a different SHAPE rather than a different texture.
    /// </summary>
    [AddComponentMenu("Gunsmith/Fired Case View")]
    public sealed class FiredCaseView : MonoBehaviour
    {
        [Header("Materials")]
        public Material BrassMaterial;
        public Material PrimerMaterial;
        public Material MarkMaterial;

        [Header("Geometry")]
        [Tooltip("Case length, metres.")]
        public double CaseLength = 0.0192;

        [Tooltip("Case body diameter, metres.")]
        public double BodyDiameter = 0.0098;

        [Tooltip("Rim diameter, metres.")]
        public double RimDiameter = 0.0100;

        [Range(8, 48)] public int RadialSegments = 24;

        /// <summary>What this case is showing. Read-only once shown.</summary>
        public FiredCase Condition { get; private set; }

        /// <summary>Builds the case for a shot.</summary>
        public void Show(in FiredCase fired)
        {
            Condition = fired;
            Clear();

            BuildBody(fired);
            BuildPrimer(fired);

            if (fired.Head == CaseHeadCondition.EjectorMark ||
                fired.Head == CaseHeadCondition.IncipientSeparation)
                BuildEjectorMark();

            if (fired.NeckSplit) BuildSplit(fired);
        }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
        }

        /// <summary>
        /// The case body. A rim at the head, then the body, then the mouth.
        ///
        /// Pressure past the case's limit bulges the body — brass is being pushed out
        /// against the chamber wall and does not come back — so an overpressure case is
        /// visibly fatter than one that ran clean.
        /// </summary>
        private void BuildBody(in FiredCase fired)
        {
            double body = BodyDiameter * 0.5;
            double rim = RimDiameter * 0.5;

            // Only what went past the limit shows as bulge, capped so a wildly hot load
            // still looks like a cartridge case rather than a balloon.
            double over = fired.PressureFraction > 1.0 ? fired.PressureFraction - 1.0 : 0.0;
            if (over > 0.35) over = 0.35;
            double bulged = body * (1.0 + over * 0.20);

            var profile = new[]
            {
                new ProfilePoint { X = 0.0, OuterRadius = rim },
                new ProfilePoint { X = 0.0012, OuterRadius = rim },
                new ProfilePoint { X = 0.0016, OuterRadius = body },
                new ProfilePoint { X = CaseLength * 0.45, OuterRadius = bulged },
                new ProfilePoint { X = CaseLength, OuterRadius = body }
            };

            var mesh = ProjectileMeshBuilder.CreateFromProfile(
                profile, profile.Length, "Fired Case", RadialSegments);

            Spawn("Case", mesh, BrassMaterial, Vector3.zero);
        }

        /// <summary>
        /// The primer.
        ///
        /// A healthy primer keeps the radius on its corner and reads as a domed disc. As
        /// pressure rises the cup irons out against the bolt face and the dome
        /// disappears — that flattening is the single sign handloaders read first, and
        /// here it is literally the shape of the object.
        /// </summary>
        private void BuildPrimer(in FiredCase fired)
        {
            float diameter = (float)(BodyDiameter * 0.52);

            // Domed when healthy, flat once the cup has yielded, dished once it is gone.
            float dome;
            switch (fired.Primer)
            {
                case PrimerCondition.Rounded: dome = 0.45f; break;
                case PrimerCondition.Flattened: dome = 0.16f; break;
                case PrimerCondition.Cratered: dome = 0.10f; break;
                case PrimerCondition.Pierced: dome = 0.07f; break;
                default: dome = 0.05f; break;
            }

            var primer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            primer.name = "Primer";
            primer.transform.SetParent(transform, false);
            primer.transform.localScale = new Vector3(diameter, diameter * dome, diameter);
            primer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Sits in the head, at the base of the case. The mesh runs along -Z from the
            // rim, so the head is at z = 0.
            primer.transform.localPosition = new Vector3(0f, 0f, 0.0004f);

            if (PrimerMaterial != null)
                primer.GetComponent<MeshRenderer>().sharedMaterial = PrimerMaterial;

            // A loose pocket no longer grips: the primer sits proud and crooked, which
            // is exactly how it presents itself on a real case.
            if (fired.Primer == PrimerCondition.PocketLoose)
            {
                primer.transform.localPosition += new Vector3(0.0004f, 0.0002f, 0.0006f);
                primer.transform.localRotation = Quaternion.Euler(78f, 0f, 9f);
            }
        }

        /// <summary>Brass extruded into the ejector hole leaves a raised pip on the head.
        /// It is small, and finding it is the point.</summary>
        private void BuildEjectorMark()
        {
            var pip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pip.name = "Ejector mark";
            pip.transform.SetParent(transform, false);

            float size = (float)(BodyDiameter * 0.16);
            pip.transform.localScale = new Vector3(size, size, size * 0.35f);
            pip.transform.localPosition = new Vector3((float)(BodyDiameter * 0.30), 0f, 0.0006f);

            if (MarkMaterial != null)
                pip.GetComponent<MeshRenderer>().sharedMaterial = MarkMaterial;
        }

        /// <summary>A split runs down the neck, where the brass is thinnest and most
        /// work-hardened.</summary>
        private void BuildSplit(in FiredCase fired)
        {
            var split = GameObject.CreatePrimitive(PrimitiveType.Cube);
            split.name = fired.Ruptured ? "Rupture" : "Neck split";
            split.transform.SetParent(transform, false);

            float length = (float)(CaseLength * (fired.Ruptured ? 0.55 : 0.30));
            float width = (float)(BodyDiameter * (fired.Ruptured ? 0.30 : 0.12));

            split.transform.localScale = new Vector3(width, (float)(BodyDiameter * 0.6), length);
            split.transform.localPosition = new Vector3(0f, 0f, -(float)(CaseLength - length * 0.5));

            if (MarkMaterial != null)
                split.GetComponent<MeshRenderer>().sharedMaterial = MarkMaterial;
        }

        private void Spawn(string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
        }
    }
}
